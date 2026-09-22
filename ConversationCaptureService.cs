using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using NAudio.Wave;
using Vosk;

namespace TimeTask
{
    /// <summary>
    /// V2 交流/会议采集服务（Audio Engine）。
    ///
    /// 设计原则（来自产品策略与代码评审）：
    /// 1. 录音层永远不依赖 ASR 是否可用——ASR 崩了，录音照常进行并落盘。
    /// 2. 双轨分别写盘：麦克风 = mic.wav，系统播放音频 = system.wav。
    ///    两轨分离便于将来做“我/对方”区分与说话人识别；实时转写时再做混音喂给 ASR。
    /// 3. 流式写盘：开始记录即创建文件，数据到达即写入，停止只做 Close/Finalize，
    ///    绝不把 1~2 小时会议塞进 List&lt;byte&gt; 内存里。
    /// 4. Pre-roll：从用户点击“开始”的第一帧起就连续写盘 + 喂 ASR，天然不丢首字；
    ///    ASR 模型若晚于首帧就绪，未识别的音频进入有界 pending 缓冲，就绪后回灌。
    /// 5. 自适应静音检测（替代固定 -30dB）：用环境噪声基底动态判断“有没有人在说”，
    ///    仅用于快捷口述的自动结束，不污染会议录音。
    /// 6. 两个完全不同的入口：快捷口述（仅麦克风、静默自动结束）与会记录（麦克风+系统音频、可数小时）。
    ///
    /// == 状态机（v4：会话严格隔离）==
    /// - 唯一权威状态：_recording（bool）。没有“正在停止”这种中间态来卡住“再次开始”。
    /// - Stop() 在调用线程（通常是 UI/热键线程）**同步**把 _recording 置 false，并在同一锁内把
    ///   “旧会话的全部资源（设备/写入器/识别器/转写/泵）”快照成局部变量，交后台线程释放；
    ///   新会话在 Start 里创建全新资源 → 新旧会话严格隔离，停止后点击“记录”可立即开录。
    /// - Vosk 的 Model 常驻内存（首次加载后复用），每会话 new 一个轻量 VoskRecognizer
    ///   （停止后由收尾线程释放；下一轮 Start 自动重建）；识别器的 FinalResult 只在后台线程、
    ///   泵退出后执行，且所有 Vosk 调用经 _voskIoLock 串行化 → 无死锁、无并发。
    /// </summary>
    public sealed class ConversationCaptureService : IDisposable
    {
        public enum CaptureMode
        {
            Quick,        // 快捷口述：麦克风，静默自动结束，直接进草稿
            Conversation, // 交流记录：麦克风 + 系统音频
            Meeting       // 会议记录：麦克风 + 系统音频（标记为会议类型）
        }

        public class TranscriptTurn
        {
            public DateTime Time { get; set; }
            public string Text { get; set; }
            public string SpeakerId { get; set; } = "me";
        }

        public class ConversationCaptureResult
        {
            public ConversationType Type { get; set; } = ConversationType.Unknown;
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string Summary { get; set; }
            public List<TranscriptTurn> Transcript { get; set; } = new List<TranscriptTurn>();
            public List<TaskDraft> Actions { get; set; } = new List<TaskDraft>();
            public string AudioFolderPath { get; set; }
            public bool AsrAvailable { get; set; }
            /// <summary>本次是否经过 LLM 上下文精修（即用户所说的“脑补”补正）。</summary>
            public bool LlmRefined { get; set; }
            public MeetingState MeetingState { get; set; }
        }

        // ---- 结构化会议状态：会议助手的实时信息提示来源；增量更新、规模恒定，长会议也安全 ----
        public class MeetingConcept
        {
            public string Term { get; set; }
            public string Explanation { get; set; }
        }
        public class MeetingQuestion
        {
            public string Question { get; set; }
            public bool Resolved { get; set; }
        }
        public class MeetingState
        {
            public List<MeetingConcept> Concepts { get; set; } = new List<MeetingConcept>();
            public List<MeetingQuestion> Questions { get; set; } = new List<MeetingQuestion>();
            public List<string> Decisions { get; set; } = new List<string>();
            public List<string> Actions { get; set; } = new List<string>();
            public string Summary { get; set; } = string.Empty; // 紧凑摘要，作为下一轮上下文锚
        }

        public event Action<ConversationCaptureResult> ConversationCaptured;
        public event Action StatusChanged;
        public event Action<MeetingState> MeetingStateChanged;   // 状态更新时广播给 UI（录制中实时刷新面板）
        public event Action<string> MeetingPrompt;              // 出现新概念/问题时广播（用于气泡提示）

        private readonly object _lock = new object();

        private readonly TaskDraftManager _draftManager;
        private readonly IntentRecognizer _intentRecognizer = new IntentRecognizer();
        private readonly VoiceReminderTimeParser _reminderParser = new VoiceReminderTimeParser();

        private readonly bool _includeSystemAudio;
        private readonly TimeSpan _maxMeetingDuration;
        private readonly TimeSpan _quickSilenceTimeout;
        private readonly TimeSpan _quickMaxWait;
        private readonly int _sampleRate = 16000;

        // ---- 采集设备 ----
        private WaveInEvent _mic;
        private WasapiLoopbackCapture _loopback;
        private BufferedWaveProvider _loopbackBuffer;
        private MediaFoundationResampler _loopbackResampler;

        // ---- 双轨流式写盘 ----
        private WaveFileWriter _micWriter;
        private WaveFileWriter _sysWriter;
        private string _sessionFolder;

        // ---- ASR（Vosk，独立于录音）----
        // _voskModel 常驻（首次加载后复用）；_voskRecognizer 每会话一个，停止时置 null 交后台收尾。
        private Model _voskModel;
        private VoskRecognizer _voskRecognizer;
        private bool _voskReady;
        // Vosk 调用互斥：识别器/模型的一切原生调用（AcceptWaveform / FinalResult / Dispose）经此锁串行化，
        // 作为“同一时刻只有一个线程触碰 Vosk”的纵深防御——新旧会话交替的极端时序下也不会并发。
        private readonly object _voskIoLock = new object();

        // 模型引导：单例复用 SpeechModelManager，避免每次录音都从头重新下载（旧实现每次 new 会导致 .tmp 被截断、永远下不完）。
        // _asrStatus：0=未初始化 1=模型加载中 2=实时转写就绪 3=不可用
        private SpeechModelManager _modelManager;
        private Task<SpeechModelBootstrapResult> _bootstrapTask;
        private bool _initStarted;
        private int _asrStatus;

        // ---- 混音队列（统一 16k/16bit/mono 的 short 样本）----
        // 关键：必须是有界队列。实时转写允许在跟不上时丢最旧帧，绝不允许无限堆积导致 OOM。
        private readonly Queue<short> _micQueue = new Queue<short>();
        private readonly Queue<short> _sysQueue = new Queue<short>();
        private const int AsrQueueHardCap = 160000; // ~10s @16k/单轨，超过即丢最旧帧

        // ---- ASR 晚就绪时的有界回灌缓冲 ----
        private readonly Queue<byte[]> _asrPending = new Queue<byte[]>();
        private int _asrPendingBytes;
        private const int AsrPendingCapBytes = 960000; // ~30s @16k/16bit/mono

        // ---- 状态 ----
        private CaptureMode _mode = CaptureMode.Conversation;
        private bool _recording;
        private DateTime _startTime = DateTime.MinValue;
        private DateTime _lastActivity = DateTime.MinValue;
        private bool _hadSpeech;
        private double _noiseFloor = double.MaxValue;
        private bool _micActive;
        private bool _sysActive;
        // 注意：不是 Clear() 而是「每会话换新列表引用」——停止后仍在途的 FunASR 分段识别
        // 持有旧列表引用写入，快照也持同一引用，新旧会话的转写绝不串台。
        private List<TranscriptTurn> _turns = new List<TranscriptTurn>();
        private readonly System.Timers.Timer _watchdog;

        // ---- ASR 引擎选择（Vosk 实时流式 / FunASR 高精度分段）----
        // 用户反馈「识别能力太弱」：Vosk 小模型精度有限；FunASR(SenseVoice) 中文精度高得多，
        // 但以「分段文件识别」方式工作。ConversationCaptureAsrEngine = auto|funasr|vosk（默认 auto：
        // 优先 FunASR，启动超时自动回落 Vosk，本次录音不受影响）。
        private AsrEngineKind _engineKind = AsrEngineKind.Vosk;
        private bool _engineAllowVoskFallback;
        private bool _funasrReady;
        private readonly FunAsrEngine _funasr = new FunAsrEngine();
        // FunASR 分段缓冲（16k/16bit/mono 混音 PCM，与泵输出一致）
        private readonly object _segLock = new object();
        private readonly Queue<byte[]> _segChunks = new Queue<byte[]>();
        private int _segBufferedBytes;
        private int _segQuietTailBytes;
        private int _funasrInFlight;
        private int _segSeq;
        private string _funasrSegDir;
        private const int FunAsrSegCapSeconds = 900; // worker 暂不可用时最多囤 15 分钟，超过丢最旧（防 OOM）
        private const int FunAsrStartTimeoutSeconds = 60;  // 会话内等引擎就绪的上限，超时回落 Vosk
        private const int FunAsrStopWaitSeconds = 15;      // 停止时等在途分段识别完成的上限
        // 会话代数：每 Start 递增；异步任务（会议状态更新/收尾）据此丢弃过期结果，绝不污染新会话。
        private int _sessionGen;
        // 最近一次停止的收尾任务（Dispose 时做有界等待：WAV 头写完整、收尾不半途而废）。
        private Task _stopTask = Task.CompletedTask;

        // ---- 上下文感知的转写精修（LLM“脑补”）：录制中滚动精修 + 停止时终次精修 ----
        // 模拟人类听众：用前后文/主题/术语把听不清、识别错的句子补正，而非机械逐句解析。
        private LlmService _llmService;

        /// <summary>会话级取消：停止采集时取消在途的 LLM 精修请求，长会议里点停止不必等模型跑完。</summary>
        private volatile System.Threading.CancellationTokenSource _llmLifecycleCts = new System.Threading.CancellationTokenSource();
        private string _contextTopic = string.Empty;   // 会议主题/术语（录音前由用户填入，作精修锚点）
        private string _contextGlossary = string.Empty;
        // ---- 会议实时状态机（有状态增量更新：无论会议 1 小时还是 3 小时，单次 LLM 调用规模恒定）----
        private MeetingState _meetingState;             // 跨 tick 持久化的结构化会议状态（概念/问题/决策/行动项/摘要）
        private bool _refining;                         // 防止并发精修
        private int _lastStateTurnCount;                // 已并入状态的 _turns 数量（用于计算增量 delta）
        private System.Timers.Timer _refineTimer;       // 录制中定时触发状态更新
        // 上一轮快照，用于检测“新增项”以触发气泡提示（仅本机呈现，不对外）
        private List<MeetingConcept> _prevConcepts = new List<MeetingConcept>();
        private List<MeetingQuestion> _prevQuestions = new List<MeetingQuestion>();
        private List<string> _prevDecisions = new List<string>();
        private List<string> _prevActions = new List<string>();

        // ---- ASR 推理后台线程（与音频回调线程解耦，避免阻塞 UI / 音频线程）----
        // 每个会话一个独立泵对象（停止时随会话快照移交后台收尾）：
        // 新会话永远创建全新泵，旧会话的收尾不可能“误关”新会话的泵（消弭共享旗标的隐患）。
        private sealed class AsrPump
        {
            public volatile bool Stop;
            public Thread Thread;
        }
        private AsrPump _pump;
        private readonly AutoResetEvent _asrSignal = new AutoResetEvent(false);

        public ConversationCaptureService()
        {
            _draftManager = new TaskDraftManager();
            _includeSystemAudio = ReadBool("ConversationCaptureIncludeSystemAudio", true);
            _maxMeetingDuration = TimeSpan.FromMinutes(ReadInt("ConversationCaptureAutoStopMinutes", 120));
            _quickSilenceTimeout = TimeSpan.FromSeconds(ReadInt("ConversationCaptureQuickSilenceSeconds", 8));
            _quickMaxWait = TimeSpan.FromSeconds(ReadInt("ConversationCaptureQuickMaxWaitSeconds", 60));

            _watchdog = new System.Timers.Timer(1000) { AutoReset = true };
            _watchdog.Elapsed += OnWatchdogTick;

            // 启动即开始在后台加载/下载 ASR 模型，这样用户第一次按下“记录”时模型多半已就绪，
            // 避免“录音能录、但收件箱永远为空”的问题。初始化本身是异步的，不阻塞构造函数。
            try { InitRecognizerBackground(); }
            catch { }

            // 高精度引擎（auto/funasr 时）也在应用启动即预热：首次需下载约 230MB 模型，
            // 从启动就开始下载（而不是等第一场录音），用户开录时大概率已就绪。
            if (AsrEngineChoice.Resolve(ConfigurationManager.AppSettings["ConversationCaptureAsrEngine"]).Kind
                == AsrEngineKind.FunAsr)
            {
                _ = Task.Run(async () =>
                {
                    try { await _funasr.EnsureReadyAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                    catch { }
                });
            }
        }

        public bool IsRecording => _recording;
        public CaptureMode CurrentMode => _mode;
        public bool MicActive => _micActive;
        public bool SystemActive => _sysActive;
        public TimeSpan Elapsed => _recording ? DateTime.Now - _startTime : TimeSpan.Zero;
        public bool AsrAvailable => _voskReady || _funasrReady;
        /// <summary>ASR 状态文本（供状态栏/收件箱空态展示）：引擎中立，不露实现名词。</summary>
        public string AsrStatusText => _asrStatus switch
        {
            1 => _engineKind == AsrEngineKind.FunAsr ? "高精度识别启动中…" : "语音模型加载中…",
            2 => "实时转写就绪",
            3 => "转写暂不可用（录音仍保存）",
            _ => "转写未初始化"
        };

        public void Start(CaptureMode mode)
        {
            // 唯一的闸门：正在录就忽略（避免重复开始叠设备）。
            // 注意：停止后 _recording 会**同步**立即变 false，所以“停止后再点记录”可立即开录。
            if (_recording) return;

            lock (_lock)
            {
                if (_recording) return;

                // 每次开新会话换新的取消源：上一会话遗留的取消态不影响本轮 LLM 精修
                _llmLifecycleCts?.Dispose();
                _llmLifecycleCts = new System.Threading.CancellationTokenSource();

                _mode = mode;
                _recording = true;
                _startTime = DateTime.Now;
                _lastActivity = DateTime.Now;
                _hadSpeech = false;
                _noiseFloor = double.MaxValue;
                _sessionGen++;
                // 换新列表（不是 Clear）：在途识别闭包持有旧引用，新旧会话转写绝不串台
                _turns = new List<TranscriptTurn>();
                _micQueue.Clear();
                _sysQueue.Clear();
                _asrPending.Clear();
                _asrPendingBytes = 0;

                // 引擎选择（每会话解析一次，可配置切换）：auto=优先高精度、失败回落 Vosk
                var engineChoice = AsrEngineChoice.Resolve(ConfigurationManager.AppSettings["ConversationCaptureAsrEngine"]);
                _engineKind = engineChoice.Kind;
                _engineAllowVoskFallback = engineChoice.AllowVoskFallback;
                _funasrReady = _engineKind == AsrEngineKind.FunAsr && _funasr.IsRunning;
                if (_engineKind == AsrEngineKind.FunAsr)
                {
                    lock (_segLock)
                    {
                        _segChunks.Clear();
                        _segBufferedBytes = 0;
                        _segQuietTailBytes = 0;
                    }
                }

                PrepareSessionFolder();

                VoiceListenerStatusCenter.Publish(VoiceListenerState.Recognizing,
                    _mode == CaptureMode.Quick ? "快捷口述采集中" : "交流记录采集中");
                Console.WriteLine($"[ConversationCapture] Start({_mode}).");

                // 设备启动（失败不致命，至少保留可用的一路）。这些调用很快，UI 不会感知卡顿。
                try { StartMic(); _micActive = true; }
                catch (Exception ex)
                {
                    _micActive = false;
                    VoiceRuntimeLog.Error("麦克风采集启动失败，无法记录。", ex);
                }

                if (_includeSystemAudio && _mode != CaptureMode.Quick && _micActive)
                {
                    try { StartLoopback(); _sysActive = true; }
                    catch (Exception ex)
                    {
                        _sysActive = false;
                        VoiceRuntimeLog.Error("系统音频（Loopback）采集启动失败，仅使用麦克风。", ex);
                    }
                }

                RaiseStatus();

                if (!_micActive)
                {
                    // 连麦克风都没有，直接放弃（走 Stop 的干净路径，异步收尾，不阻塞）
                    Task.Run(() => Stop(false));
                    VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "无可用音频输入设备");
                    return;
                }

                // ASR 在后台初始化（首跑需下载/解压模型，可能耗时；之后复用常驻 Model 很快）。
                // 录音从第一帧就落盘；识别器就绪前音频进 pending 缓冲，就绪后回灌。
                Task.Run(() => InitRecognizerBackground());

                // 启动 ASR 推理后台线程（与音频回调解耦，避免录制时卡 UI）
                StartPump();

                // 启动“会议实时状态机”计时器：每 60s 把【新增转写 delta + 当前状态】交 LLM 增量刷新。
                // 只发增量，单次调用规模恒定，长会议（1-2h）也不会超出上下文窗口或成本爆炸。
                _meetingState = null;
                _lastStateTurnCount = 0;
                _prevConcepts.Clear();
                _prevQuestions.Clear();
                _prevDecisions.Clear();
                _prevActions.Clear();
                _refining = false;
                if (_refineTimer == null)
                {
                    _refineTimer = new System.Timers.Timer(60000) { AutoReset = true };
                    _refineTimer.Elapsed += RefineTick;
                }
                _refineTimer.Start();

                _watchdog.Start();
            }
        }

        /// <summary>
        /// 停止记录。耗时收尾（停设备、Vosk FinalResult、NLP 行动项抽取）全部放到后台线程，
        /// 调用线程立即返回——按“停止”时界面绝不卡死，且状态栏瞬间翻转为“记录已结束”。
        /// </summary>
        public void Stop()
        {
            Stop(true);
        }

        private void Stop(bool raiseEvent)
        {
            SessionSnapshot snap;
            // 同步、立即翻转状态 + 同步冻结旧会话资源：
            // 1) 无论后台收尾是否卡顿，UI/按钮/状态栏在“停止”被按下瞬间就更新；
            // 2) 快照在同一把锁内完成，新会话（Start 创建全新资源）与后台收尾（只认快照里的旧资源）
            //    严格隔离——“停止后立即再开录”时二者绝不互相踩踏。
            lock (_lock)
            {
                if (!_recording) return;
                _recording = false;
                _watchdog.Stop();
                _refineTimer?.Stop();
                snap = TakeSessionSnapshotLocked();
            }

            try { _asrSignal.Set(); } catch { } // 唤醒（已 detach 的）泵线程检查停止旗标

            VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, "记录已结束");
            Console.WriteLine("[ConversationCapture] Stop requested; status flipped to 记录已结束.");
            RaiseStatus();

            // 收尾（停设备、Vosk 收尾、NLP 抽取、弹收件箱）放到后台线程，失败也不影响状态。
            var task = Task.Run(async () => await StopInternal(snap, raiseEvent));
            lock (_lock) { _stopTask = task; }
        }

        /// <summary>
        /// 旧会话的完整资源快照（在 Stop 的锁内同步冻结）：
        /// 后台收尾线程只操作这里面的对象，绝不触碰新会话的资源。
        /// </summary>
        private sealed class SessionSnapshot
        {
            public int Gen;
            public List<TranscriptTurn> Turns;
            public CaptureMode Mode;
            public DateTime StartTime;
            public string Folder;
            public bool VoskAvailable;
            public VoskRecognizer Recognizer;
            public WaveInEvent Mic;
            public WasapiLoopbackCapture Loopback;
            public MediaFoundationResampler LoopbackResampler;
            public WaveFileWriter MicWriter;
            public WaveFileWriter SysWriter;
            public AsrPump Pump;
            public MeetingState MeetingState;
            public int LastStateTurnCount;
            // FunASR 引擎的会话状态
            public AsrEngineKind Engine;
            public List<byte[]> FunasrSegments; // 停止时需冲洗的剩余分段缓冲
        }

        // 必须在持有 _lock 时调用：把当前会话的全部可变状态/资源移交快照，并把共享字段清零。
        private SessionSnapshot TakeSessionSnapshotLocked()
        {
            var snap = new SessionSnapshot
            {
                Gen = _sessionGen,
                Turns = _turns,   // 持引用：停止后仍在途的 FunASR 分段结果会写入此列表，收尾统一读取
                Mode = _mode,
                StartTime = _startTime,
                Folder = _sessionFolder,
                VoskAvailable = _voskReady || _funasrReady,
                Recognizer = _voskRecognizer,
                Mic = _mic,
                Loopback = _loopback,
                LoopbackResampler = _loopbackResampler,
                MicWriter = _micWriter,
                SysWriter = _sysWriter,
                Pump = _pump,
                MeetingState = _meetingState,
                LastStateTurnCount = _lastStateTurnCount,
                Engine = _engineKind
            };

            // FunASR：把分段缓冲移交快照（停止收尾时冲洗成最后一段）
            if (_engineKind == AsrEngineKind.FunAsr)
            {
                lock (_segLock)
                {
                    snap.FunasrSegments = new List<byte[]>(_segChunks);
                    _segChunks.Clear();
                    _segBufferedBytes = 0;
                    _segQuietTailBytes = 0;
                }
            }
            _funasrReady = false;

            _mic = null;
            _loopback = null;
            _loopbackResampler = null;
            _micWriter = null;
            _sysWriter = null;
            _voskRecognizer = null;
            _voskReady = false;
            _micActive = false;
            _sysActive = false;
            _startTime = DateTime.MinValue;
            _sessionFolder = null;
            _pump = null;
            if (snap.Pump != null) snap.Pump.Stop = true; // 通知旧泵退出（Join 交收尾线程完成）

            return snap;
        }

        /// <summary>
        /// 全局快捷键/托盘兜底的切换入口：未录音则按默认模式开始，正在录音则停止。
        /// Start/Stop 均已是非阻塞的轻量调用，无需额外 Task.Run 包裹。
        /// </summary>
        public void Toggle()
        {
            if (IsRecording) Stop();
            else Start(DefaultMode);
        }

        /// <summary>
        /// 一键开始时的默认模式（可在 App.config 用 ConversationCaptureDefaultMode 配置，
        /// 取值 Quick / Conversation / Meeting，默认 Conversation）。
        /// 注意：Quick 模式停止时不弹行动收件箱，故默认不推荐 Quick。
        /// </summary>
        public CaptureMode DefaultMode
        {
            get
            {
                var raw = ConfigurationManager.AppSettings["ConversationCaptureDefaultMode"];
                if (!string.IsNullOrWhiteSpace(raw)
                    && Enum.TryParse<CaptureMode>(raw, true, out var parsed))
                {
                    return parsed;
                }
                return CaptureMode.Conversation;
            }
        }

        /// <summary>
        /// 后台收尾：只操作 Stop 时同步快照出来的旧会话资源（设备/写入器/识别器/转写/泵），
        /// 因此即便用户此刻已开启新会话，二者也绝不互相踩踏。
        /// </summary>
        private async Task StopInternal(SessionSnapshot snap, bool raiseEvent)
        {
            var turns = snap.Turns;
            var mode = snap.Mode;
            var startTime = snap.StartTime;
            var folder = snap.Folder;
            var voskAvailable = snap.VoskAvailable;
            var recog = snap.Recognizer;
            var mic = snap.Mic;
            var loopback = snap.Loopback;
            var loopbackResampler = snap.LoopbackResampler;
            var micWriter = snap.MicWriter;
            var sysWriter = snap.SysWriter;

            try
            {
                // 1) 等本会话的 ASR 泵线程退出（最多 2s）。泵退出后不再触碰本会话识别器。
                StopPump(snap.Pump);

                // 1.5) FunASR 收尾：把剩余分段缓冲冲洗成最后一段识别掉 + 有界等在途识别完成。
                // 在途识别闭包持有本会话转写列表引用，结果会直接落到 snap.Turns，随后统一进入结果构建。
                if (snap.Engine == AsrEngineKind.FunAsr)
                {
                    await FinalizeFunAsrSessionAsync(snap).ConfigureAwait(false);
                }

                // 2) 让旧识别器完成最后一句：泵线程已退出，且所有 Vosk 调用经 _voskIoLock 串行化，绝无并发。
                if (recog != null)
                {
                    bool tookVoskLock = false;
                    try
                    {
                        tookVoskLock = Monitor.TryEnter(_voskIoLock, 3000);
                        if (tookVoskLock)
                        {
                            string finalJson = recog.FinalResult();
                            if (!string.IsNullOrWhiteSpace(finalJson))
                            {
                                try
                                {
                                    var root = Newtonsoft.Json.Linq.JObject.Parse(finalJson);
                                    string text = (string)root["text"] ?? string.Empty;
                                    text = CleanText(text);
                                    if (!string.IsNullOrWhiteSpace(text) && text.Length >= 2)
                                        turns.Add(new TranscriptTurn { Time = DateTime.Now, Text = text, SpeakerId = "me" });
                                }
                                catch { }
                            }
                            // 会话识别器用毕即弃：原生句柄及时释放（下一会话会另建新识别器）。
                            try { recog.Dispose(); } catch { }
                        }
                        else
                        {
                            VoiceRuntimeLog.Info("Vosk 收尾锁等待超时：跳过末句收尾（录音已保存，不影响收件箱生成）。");
                        }
                    }
                    catch { }
                    finally { if (tookVoskLock) { try { Monitor.Exit(_voskIoLock); } catch { } } }
                }

                // 3) 停止并释放旧设备（带超时保护，避免 StopRecording 在某些实现下长时间阻塞）
                TryStopRecording(mic, 2000);
                TryStopRecording(loopback, 2000);
                try { mic?.Dispose(); } catch { }
                try { loopback?.Dispose(); } catch { }
                try { loopbackResampler?.Dispose(); } catch { }
                try { micWriter?.Dispose(); } catch { }
                try { sysWriter?.Dispose(); } catch { }

                // 4) 终次增量刷新会议状态（仅未并入的 delta + 本会话当前状态，规模恒定，长会议也安全）
                List<TranscriptTurn> finalTurns = turns;
                bool llmRefined = false;
                MeetingState finalStateForResult = snap.MeetingState;
                try
                {
                    var finalDelta = turns.Skip(snap.LastStateTurnCount).ToList();
                    var finalState = await TryUpdateStateAsync(snap.MeetingState ?? new MeetingState(), finalDelta);
                    if (finalState != null)
                    {
                        llmRefined = true;
                        finalStateForResult = finalState;
                        // 仅当期间没有开启新会话时才写回共享状态（避免污染新一轮会议）。
                        if (snap.Gen == _sessionGen)
                        {
                            _meetingState = finalState;
                            lock (_lock) { _lastStateTurnCount = snap.LastStateTurnCount + finalDelta.Count; }
                        }
                        var finalText = string.IsNullOrWhiteSpace(finalState.Summary)
                            ? string.Join("\n", finalDelta.Select(t => t.Text))
                            : finalState.Summary + "\n" + string.Join("\n", finalDelta.Select(t => t.Text));
                        finalTurns = SplitIntoLines(finalText)
                            .Select(s => new TranscriptTurn { Time = DateTime.Now, Text = s })
                            .ToList();
                    }
                }
                catch { }

                // 5) 结果抽取与弹收件箱（NLP 不阻塞“停止”）
                var result = BuildResult(finalTurns, voskAvailable, folder, startTime, mode, llmRefined);
                result.MeetingState = finalStateForResult;
                Console.WriteLine($"[ConversationCapture] Result: Turns={result.Transcript.Count}, Actions={result.Actions.Count}, Asr={result.AsrAvailable}, LlmRefined={result.LlmRefined}");
                if (raiseEvent && (mode == CaptureMode.Conversation || mode == CaptureMode.Meeting))
                {
                    var captured = result;
                    _ = Task.Run(() =>
                    {
                        try { ConversationCaptured?.Invoke(captured); }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("停止收尾异常。", ex);
            }
        }

        private void PrepareSessionFolder()
        {
            try
            {
                // 录音目录遵循 AppPaths 约定：便携模式 = exe 旁 Recordings\；漫游模式 = %AppData%\TimeTask\Recordings
                // （安装到 Program Files 等只读位置时也能正常落盘）。
                string baseDir = AppPaths.RecordingsDir;
                _sessionFolder = Path.Combine(baseDir, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(_sessionFolder);

                var fmt = new WaveFormat(_sampleRate, 16, 1);
                _micWriter = new WaveFileWriter(Path.Combine(_sessionFolder, "mic.wav"), fmt);
                // system.wav 仅在需要系统音频时创建
                if (_includeSystemAudio && _mode != CaptureMode.Quick)
                    _sysWriter = new WaveFileWriter(Path.Combine(_sessionFolder, "system.wav"), fmt);
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("创建录音目录/文件失败。", ex);
                _micWriter = null;
                _sysWriter = null;
            }
        }

        // ---------- 设备启动 ----------

        private void StartMic()
        {
            _mic = new WaveInEvent
            {
                WaveFormat = new WaveFormat(_sampleRate, 16, 1),
                BufferMilliseconds = 200
            };
            _mic.DataAvailable += OnMicData;
            _mic.RecordingStopped += OnMicStopped;
            _mic.StartRecording();
        }

        private void StartLoopback()
        {
            _loopback = new WasapiLoopbackCapture();
            var srcFormat = _loopback.WaveFormat;
            _loopbackBuffer = new BufferedWaveProvider(srcFormat);
            _loopbackResampler = new MediaFoundationResampler(_loopbackBuffer, new WaveFormat(_sampleRate, 16, 1));
            _loopback.DataAvailable += OnLoopbackData;
            _loopback.RecordingStopped += OnLoopbackStopped;
            _loopback.StartRecording();
        }

        private void OnMicStopped(object sender, StoppedEventArgs e) { }
        private void OnLoopbackStopped(object sender, StoppedEventArgs e) { }

        // 有界入队：实时转写允许在跟不上时丢最旧帧，绝不允许无限堆积导致 OOM。
        private void EnqueueBounded(Queue<short> q, short[] items)
        {
            if (items == null || items.Length == 0) return;
            lock (_lock)
            {
                foreach (var s in items) q.Enqueue(s);
                int over = q.Count - AsrQueueHardCap;
                if (over > 0)
                    for (int i = 0; i < over; i++) q.Dequeue();
            }
        }

        // ---------- 数据到达 ----------

        private void OnMicData(object sender, WaveInEventArgs e)
        {
            if (!_recording || e.BytesRecorded <= 0) return;

            // 1) 流式写盘（第一帧即写入 → 不丢首字）
            try { _micWriter?.Write(e.Buffer, 0, e.BytesRecorded); } catch { }

            // 2) 自适应静音检测（仅快捷口述用于自动结束）
            if (_mode == CaptureMode.Quick)
                UpdateSpeechActivity(e.Buffer, e.BytesRecorded);

            // 3) 仅把样本入队；真正的 Vosk 推理放到独立 ASR 后台线程。
            //    绝不在音频回调线程上做重活，也绝不在持锁时调用 AcceptWaveform。
            short[] samples = BytesToShorts(e.Buffer, e.BytesRecorded);
            EnqueueBounded(_micQueue, samples);
            _asrSignal.Set();
        }

        private void OnLoopbackData(object sender, WaveInEventArgs e)
        {
            if (!_recording || e.BytesRecorded <= 0) return;
            try { _loopbackBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded); } catch { }

            byte[] tmp = new byte[8192];
            int read;
            var shorts = new List<short>(4096);
            try
            {
            // 防御性上限：单帧回调不应产出超过 ~40s 的音频；若重采样器异常回吐海量数据，到此截断，避免单帧爆内存。
            while (shorts.Count < AsrQueueHardCap * 4 &&
                   (read = _loopbackResampler?.Read(tmp, 0, tmp.Length) ?? 0) > 0)
            {
                for (int i = 0; i + 1 < read; i += 2)
                    shorts.Add(BitConverter.ToInt16(tmp, i));
            }
            }
            catch { }

            if (shorts.Count == 0) return;

            // 流式写盘（系统音轨）
            try
            {
                if (_sysWriter != null)
                {
                    byte[] sysBytes = new byte[shorts.Count * 2];
                    for (int i = 0; i < shorts.Count; i++)
                    {
                        short m = shorts[i];
                        sysBytes[i * 2] = (byte)(m & 0xFF);
                        sysBytes[i * 2 + 1] = (byte)((m >> 8) & 0xFF);
                    }
                    _sysWriter.Write(sysBytes, 0, sysBytes.Length);
                }
            }
            catch { }

            EnqueueBounded(_sysQueue, shorts.ToArray());
            _asrSignal.Set();
        }

        // 由独立 ASR 后台线程（AsrPumpLoop）周期性调用：
        // 从双轨队列取出已积累的 short 样本，混音为 16k/16bit/mono 后喂 Vosk。
        // 关键：AcceptWaveform（CPU 密集）绝不运行在音频回调线程上，也绝不持 _lock。
        private void PumpAsrOnce()
        {
            int n;
            lock (_lock)
            {
                if (_micQueue.Count == 0 && _sysQueue.Count == 0) return;
                // 根因修复：两路按各自可用量独立抽干，绝不用“较小值”对齐——
                // 否则某一路（如系统音）长时间静音时，另一路（麦克风）队列会以实时速率无限膨胀 → OOM。
                n = Math.Min(Math.Max(_micQueue.Count, _sysQueue.Count), 96000);
            }
            if (n <= 0) return;

            short[] mic = new short[n];
            short[] sys = new short[n];
            lock (_lock)
            {
                for (int i = 0; i < n; i++)
                {
                    mic[i] = _micQueue.Count > 0 ? _micQueue.Dequeue() : (short)0;
                    sys[i] = _sysQueue.Count > 0 ? _sysQueue.Dequeue() : (short)0;
                }
            }

            byte[] bytes = new byte[n * 2];
            for (int i = 0; i < n; i++)
            {
                int sum = mic[i] + sys[i];
                if (sum > 32767) sum = 32767;
                else if (sum < -32768) sum = -32768;
                short m = (short)sum;
                bytes[i * 2] = (byte)(m & 0xFF);
                bytes[i * 2 + 1] = (byte)((m >> 8) & 0xFF);
            }

            FeedAsr(bytes);
        }

        // 引擎分发：FunASR 模式喂分段缓冲；Vosk 模式走流式识别；引擎未就绪时统一进有界 pending。
        private void FeedAsr(byte[] bytes)
        {
            if (_engineKind == AsrEngineKind.FunAsr)
            {
                if (_funasrReady)
                {
                    ReplayPending(FeedFunAsr);
                    FeedFunAsr(bytes);
                }
                else
                {
                    PendingAsr(bytes);
                }
            }
            else
            {
                if (_voskReady && _voskRecognizer != null)
                {
                    ReplayPending(TryFeedVosk);
                    TryFeedVosk(bytes);
                }
                else
                {
                    PendingAsr(bytes);
                }
            }
        }

        // 引擎就绪后回灌 pending 缓冲（pre-roll 防首字丢失，两个引擎共用同一机制）。
        private void ReplayPending(Action<byte[]> feed)
        {
            List<byte[]> replay = null;
            lock (_lock)
            {
                if (_asrPending.Count > 0)
                {
                    replay = new List<byte[]>(_asrPending.Count);
                    while (_asrPending.Count > 0)
                    {
                        var p = _asrPending.Dequeue();
                        _asrPendingBytes -= p.Length;
                        replay.Add(p);
                    }
                }
            }
            if (replay != null)
                foreach (var p in replay) feed(p);
        }

        private void PendingAsr(byte[] bytes)
        {
            lock (_lock)
            {
                // ASR 未就绪：缓存（有界），录音与写盘不受影响
                _asrPendingBytes += bytes.Length;
                while (_asrPendingBytes > AsrPendingCapBytes && _asrPending.Count > 0)
                    _asrPendingBytes -= _asrPending.Dequeue().Length;
                _asrPending.Enqueue(bytes);
            }
        }

        // ---------- FunASR：分段喂入（16k/16bit/mono 混音 PCM，与泵输出一致）----------

        private void FeedFunAsr(byte[] bytes)
        {
            byte[] toRecognize = null;
            lock (_segLock)
            {
                _segChunks.Enqueue(bytes);
                _segBufferedBytes += bytes.Length;

                // 尾部静音统计：说到停顿处提前切段，断句自然、精度最好
                double rms = FunAsrSegmenter.Rms(bytes);
                if (rms < FunAsrSegmenter.QuietRmsThreshold)
                    _segQuietTailBytes += bytes.Length;
                else
                    _segQuietTailBytes = 0;

                // 硬容量上限：worker 未就绪/变慢时防无限囤积（丢最旧段，与队列防 OOM 同一原则）
                int capBytes = FunAsrSegCapSeconds * 32000;
                while (_segBufferedBytes > capBytes && _segChunks.Count > 1)
                {
                    _segBufferedBytes -= _segChunks.Dequeue().Length;
                }

                if (FunAsrSegmenter.ShouldFlush(_segBufferedBytes, _segQuietTailBytes, 16000))
                {
                    toRecognize = TakeSegmentLocked();
                }
            }
            if (toRecognize != null)
            {
                DispatchFunAsrSegment(toRecognize, _turns, _sessionGen);
            }
        }

        /// <summary>取走全部缓冲拼成一段（调用方必须持 _segLock）。</summary>
        private byte[] TakeSegmentLocked()
        {
            if (_segBufferedBytes <= 0) return null;
            var pcm = new byte[_segBufferedBytes];
            int offset = 0;
            foreach (var chunk in _segChunks)
            {
                Buffer.BlockCopy(chunk, 0, pcm, offset, chunk.Length);
                offset += chunk.Length;
            }
            _segChunks.Clear();
            _segBufferedBytes = 0;
            _segQuietTailBytes = 0;
            return pcm;
        }

        private string WriteSegWav(byte[] pcm)
        {
            try
            {
                if (_funasrSegDir == null)
                {
                    _funasrSegDir = Path.Combine(Path.GetTempPath(), "TimeTask", "funasr-capture");
                    Directory.CreateDirectory(_funasrSegDir);
                }
                string path = Path.Combine(_funasrSegDir,
                    $"seg_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Interlocked.Increment(ref _segSeq)}.wav");
                File.WriteAllBytes(path, FunAsrEngine.BuildWav16kMono(pcm));
                return path;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("写入 FunASR 分段临时文件失败。", ex);
                return null;
            }
        }

        /// <summary>把一段 PCM 落成临时 WAV 并交后台识别（不阻塞泵线程）。</summary>
        private void DispatchFunAsrSegment(byte[] pcm, List<TranscriptTurn> sessionTurns, int gen)
        {
            string wavPath = WriteSegWav(pcm);
            if (wavPath == null) return;

            Interlocked.Increment(ref _funasrInFlight);
            Task.Run(async () =>
            {
                try
                {
                    var r = await _funasr.RecognizeWavAsync(wavPath).ConfigureAwait(false);
                    AppendFunAsrTurn(sessionTurns, gen, r);
                }
                catch (Exception ex)
                {
                    VoiceRuntimeLog.Error("FunASR 分段识别异常。", ex);
                }
                finally
                {
                    try { File.Delete(wavPath); } catch { }
                    Interlocked.Decrement(ref _funasrInFlight);
                }
            });
        }

        /// <summary>
        /// 把分段识别结果写进转写：会话进行中走完整 AddTurn（草稿/状态发布）；
        /// 停止后（含收尾冲洗段）直接追加到捕获的会话列表引用，绝不污染新会话。
        /// </summary>
        private void AppendFunAsrTurn(List<TranscriptTurn> sessionTurns, int gen, FunAsrResult r)
        {
            if (r == null || !r.Ok || string.IsNullOrWhiteSpace(r.Text)) return;
            string text = CleanText(r.Text);
            if (text.Length < 2) return;

            if (gen == _sessionGen && ReferenceEquals(sessionTurns, _turns))
            {
                AddTurn(text, (float)r.Confidence);
                return;
            }

            lock (_lock)
            {
                sessionTurns.Add(new TranscriptTurn { Time = DateTime.Now, Text = text, SpeakerId = "me" });
            }
        }

        /// <summary>停止时的 FunASR 收尾：冲洗剩余分段 + 有界等在途识别完成。</summary>
        private async Task FinalizeFunAsrSessionAsync(SessionSnapshot snap)
        {
            try
            {
                var remaining = snap.FunasrSegments;
                snap.FunasrSegments = null;
                if (remaining != null && remaining.Count > 0)
                {
                    int total = 0;
                    foreach (var c in remaining) total += c.Length;
                    if (total >= 16000) // ≥0.5s 才值得识别
                    {
                        var pcm = new byte[total];
                        int offset = 0;
                        foreach (var chunk in remaining)
                        {
                            Buffer.BlockCopy(chunk, 0, pcm, offset, chunk.Length);
                            offset += chunk.Length;
                        }
                        string wavPath = WriteSegWav(pcm);
                        if (wavPath != null)
                        {
                            Interlocked.Increment(ref _funasrInFlight);
                            try
                            {
                                var r = await _funasr.RecognizeWavAsync(wavPath).ConfigureAwait(false);
                                AppendFunAsrTurn(snap.Turns, snap.Gen, r);
                            }
                            finally
                            {
                                try { File.Delete(wavPath); } catch { }
                                Interlocked.Decrement(ref _funasrInFlight);
                            }
                        }
                    }
                }

                // 有界等在途分段识别完成（结果由闭包直接写入 snap.Turns）
                var deadline = DateTime.UtcNow.AddSeconds(FunAsrStopWaitSeconds);
                while (DateTime.UtcNow < deadline && Volatile.Read(ref _funasrInFlight) > 0)
                {
                    await Task.Delay(200).ConfigureAwait(false);
                }
                if (Volatile.Read(ref _funasrInFlight) > 0)
                {
                    VoiceRuntimeLog.Info("FunASR 在途识别未在限时内完成：放弃等待（已完成的转写不受影响，录音完整保存）。");
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR 会话收尾失败（不影响录音与已得转写）。", ex);
            }
        }

        private void AsrPumpLoop(object state)
        {
            var pump = (AsrPump)state;
            // 泵线程自愈：单次 PumpAsrOnce 异常在循环内吞掉，绝不因此让整个泵线程静默死亡
            // （否则队列将从此无人抽干、无限膨胀直至 OOM）。
            while (!pump.Stop)
            {
                try { _asrSignal.WaitOne(20); } catch { }
                if (pump.Stop) break;
                try { PumpAsrOnce(); }
                catch (Exception ex)
                {
                    VoiceRuntimeLog.Error("ASR 泵循环单步异常（已忽略，继续抽干队列）。", ex);
                }
            }
            // 注意：FinalResult 不在此处执行。识别器收尾由 StopInternal 在泵退出后、
            // 经 _voskIoLock 单线程完成，避免与新会话的泵并发访问 Vosk。
        }

        // 由 Start 在持有 _lock 时调用：为本次会话创建独立泵（旧泵已随会话快照 detach）。
        private void StartPump()
        {
            var pump = new AsrPump();
            pump.Thread = new Thread(AsrPumpLoop)
            {
                IsBackground = true,
                Name = "ConversationCapture-AsrPump"
            };
            _pump = pump;
            pump.Thread.Start(pump);
        }

        // 由收尾线程调用：等指定泵退出（最多 2s）。Vosk 若卡在非托管代码，Thread.Interrupt 无效，
        // 至多等 2 秒即放弃——停止流程不被拖住。
        private static void StopPump(AsrPump pump)
        {
            if (pump == null) return;
            pump.Stop = true;
            if (pump.Thread != null && pump.Thread.IsAlive)
            {
                try { pump.Thread.Join(TimeSpan.FromSeconds(2)); } catch { }
            }
        }

        private void TryStopRecording(IWaveIn device, int timeoutMs)
        {
            if (device == null) return;
            try
            {
                var t = Task.Run(() =>
                {
                    try { device.StopRecording(); } catch { }
                });
                t.Wait(timeoutMs);
            }
            catch { }
        }

        private void TryFeedVosk(byte[] bytes)
        {
            try
            {
                // 所有 Vosk 原生调用经 _voskIoLock 串行化（纵深防御：极端时序下也不会并发触碰识别器）。
                lock (_voskIoLock)
                {
                    if (_voskRecognizer != null && _voskRecognizer.AcceptWaveform(bytes, bytes.Length))
                        AddTurnFromJson(_voskRecognizer.Result());
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Vosk AcceptWaveform 失败。", ex);
            }
        }

        private static short[] BytesToShorts(byte[] buffer, int bytesRecorded)
        {
            int count = bytesRecorded / 2;
            var arr = new short[count];
            for (int i = 0; i < count; i++)
                arr[i] = BitConverter.ToInt16(buffer, i * 2);
            return arr;
        }

        // ---------- 自适应静音检测（快捷口述自动结束）----------

        private void UpdateSpeechActivity(byte[] buffer, int bytesRecorded)
        {
            int count = bytesRecorded / 2;
            if (count <= 0) return;
            long sumSq = 0;
            for (int i = 0; i < count; i++)
            {
                short s = BitConverter.ToInt16(buffer, i * 2);
                sumSq += (long)s * s;
            }
            double rms = Math.Sqrt((double)sumSq / count);

            // 噪声基底：仅在“安静帧”（明显低于人声）时更新，避免把开局大声说话当成底噪
            if (rms < 1200)
            {
                if (_noiseFloor == double.MaxValue) _noiseFloor = rms;
                else _noiseFloor = Math.Min(_noiseFloor, rms);
            }

            // 语音判定：明显高于噪声基底，且不低于绝对下限（避免底噪误触发）
            // 在底噪尚未建立时退化为 400 的绝对阈值，保证一开录就大声说话也能被识别
            double threshold = _noiseFloor == double.MaxValue ? 400.0 : Math.Max(_noiseFloor * 3.0, 400.0);
            if (rms > threshold)
            {
                _hadSpeech = true;
                _lastActivity = DateTime.Now;
            }
        }

        // ---------- 文本处理 ----------

        private void AddTurnFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                string text = (string)root["text"] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text)) return;
                AddTurn(CleanText(text), 0.6f);
            }
            catch { }
        }

        private void AddTurn(string text, float conf)
        {
            text = CleanText(text);
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return;

            lock (_lock)
            {
                _turns.Add(new TranscriptTurn { Time = DateTime.Now, Text = text, SpeakerId = "me" });
                _lastActivity = DateTime.Now;
            }

            if (_mode == CaptureMode.Quick)
            {
                var draft = BuildDraftIfTask(text, conf);
                if (draft != null)
                {
                    try { _draftManager.AddDraft(draft); } catch { }
                }
            }

            VoiceListenerStatusCenter.Publish(VoiceListenerState.Recognizing, text.Length > 40 ? text.Substring(0, 40) + "…" : text);
        }

        private static string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = Regex.Replace(text, @"<\|[^|>]+\|>", ""); // 清理 SenseVoice 标签
            text = Regex.Replace(text, @"\s+", " ").Trim();
            text = Regex.Replace(text, @"^(编辑行|编辑|嗯|那个|就是|请|麻烦)\s*", "", RegexOptions.IgnoreCase);
            text = text.Trim(',', '。', '.', '，');
            return text;
        }

        private TaskDraft BuildDraftIfTask(string text, float conf)
        {
            try
            {
                var analysis = TaskTextQualityHelper.AnalyzeVoiceTaskCandidate(text);
                if (!analysis.IsMeaningfulTask) return null;
                if (!_intentRecognizer.IsPotentialTask(text)) return null;

                string cleaned = _intentRecognizer.ExtractTaskDescription(text);
                if (string.IsNullOrWhiteSpace(cleaned)) return null;
                if (!TaskTextQualityHelper.IsMeaningfulTaskText(cleaned)) return null;

                DateTime? reminder = null;
                if (_reminderParser.TryParse(text, DateTime.Now, out DateTime rt))
                    reminder = rt;

                var (imp, urg) = _intentRecognizer.EstimatePriority(cleaned);
                string quad = _intentRecognizer.EstimateQuadrant(imp, urg);

                return new TaskDraft
                {
                    RawText = text,
                    CleanedText = cleaned,
                    ReminderTime = reminder,
                    ReminderHintText = reminder.HasValue ? reminder.Value.ToString("yyyy-MM-dd HH:mm") : null,
                    EstimatedQuadrant = quad,
                    Importance = imp,
                    Urgency = urg,
                    Confidence = Math.Max(0.6f, conf),
                    Source = "voice"
                };
            }
            catch { return null; }
        }

        // ---------- 结束与抽取 ----------

        private ConversationCaptureResult BuildResult(List<TranscriptTurn> turns, bool voskAvailable, string folder, DateTime startTime, CaptureMode mode, bool llmRefined)
        {
            var type = mode == CaptureMode.Meeting ? ConversationType.Meeting
                     : mode == CaptureMode.Conversation ? ConversationType.Dialog
                     : ConversationType.Monologue;

            var actions = ExtractActions(turns);

            string summary;
            if (turns.Count == 0)
                summary = voskAvailable ? "（未识别到有效内容）" : "（本次无实时转写：ASR 不可用，录音已保存到磁盘）";
            else
            {
                var sb = new StringBuilder();
                foreach (var t in turns.Take(40))
                    sb.Append(t.Text).Append(" ");
                string joined = sb.ToString().Trim();
                summary = joined.Length > 600 ? joined.Substring(0, 600) + "…" : joined;
            }

            return new ConversationCaptureResult
            {
                Type = type,
                StartTime = startTime,
                EndTime = DateTime.Now,
                Summary = summary,
                Transcript = turns,
                Actions = actions,
                AudioFolderPath = folder,
                AsrAvailable = voskAvailable,
                LlmRefined = llmRefined
            };
        }

        private List<TaskDraft> ExtractActions(List<TranscriptTurn> turns)
        {
            var actions = new List<TaskDraft>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var turn in turns)
            {
                string text = turn.Text;
                if (string.IsNullOrWhiteSpace(text)) continue;
                var draft = BuildDraftIfTask(text, 0.7f);
                if (draft == null) continue;
                if (seen.Contains(draft.CleanedText)) continue;
                seen.Add(draft.CleanedText);
                actions.Add(draft);
                if (actions.Count >= 20) break;
            }
            return actions;
        }

        /// <summary>
        /// 无 ASR 时的离线兜底：把任意文本（用户手动录入/粘贴的转写）按句切分后
        /// 复用同一套行动项抽取逻辑。使“录音 → 行动项”闭环在完全没有本地模型时仍可用。
        /// </summary>
        public List<TaskDraft> ExtractActionsFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<TaskDraft>();
            var turns = SplitIntoLines(text)
                .Select(l => new TranscriptTurn { Time = DateTime.Now, Text = l })
                .ToList();
            return ExtractActions(turns);
        }

        private static List<string> SplitIntoLines(string text)
        {
            var result = new List<string>();
            foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                // 再按句末标点切分，便于逐句抽取行动项
                foreach (var seg in Regex.Split(line, @"(?<=[。！？!?；;])"))
                {
                    var s = seg.Trim();
                    if (s.Length > 0) result.Add(s);
                }
            }
            return result;
        }

        // ---------- 上下文感知的 LLM 转写精修（模拟“人类听众脑补”）----------

        /// <summary>设置本次录制的上下文锚点（会议主题/术语），用于 LLM 精修时锚定领域与人名。</summary>
        public void SetContext(string topic, string glossary)
        {
            _contextTopic = (topic ?? string.Empty).Trim();
            _contextGlossary = (glossary ?? string.Empty).Trim();
        }

        private LlmService GetLlmService()
        {
            if (_llmService != null) return _llmService;
            try { _llmService = new LlmService(); }
            catch (Exception ex) { VoiceRuntimeLog.Error("LlmService 初始化失败，上下文精修不可用。", ex); _llmService = null; }
            return _llmService;
        }

        // ---------- 会议实时状态机（有状态增量更新）----------

        private void RefineTick(object sender, ElapsedEventArgs e)
        {
            // 录制中：仅当“有新增转写且当前未在精修”时，才把【增量 delta + 当前状态】发给 LLM。
            // 无论会议 1 小时还是 3 小时，单次调用规模恒定，彻底解决长会议的成本/上下文爆炸问题。
            if (!_recording || _refining) return;
            int baseCount;
            int gen;
            List<TranscriptTurn> delta;
            lock (_lock)
            {
                gen = _sessionGen;
                baseCount = _lastStateTurnCount;
                delta = _turns.Skip(baseCount).ToList();
            }
            if (delta.Count == 0) return;
            _refining = true;
            _ = UpdateMeetingStateAsync(delta, baseCount, gen);
        }

        private async Task UpdateMeetingStateAsync(List<TranscriptTurn> delta, int baseCount, int gen)
        {
            try
            {
                var current = _meetingState ?? new MeetingState();
                var updated = await TryUpdateStateAsync(current, delta);
                if (updated == null) return;
                // 会话已切换（用户已停止并开始新一轮）：丢弃过期结果，绝不污染新会话状态。
                if (gen != _sessionGen) return;
                _meetingState = updated;
                lock (_lock) { _lastStateTurnCount = baseCount + delta.Count; }
                DetectAndRaiseNewItems(updated);          // 触发气泡提示（仅本机）
                try { MeetingStateChanged?.Invoke(updated); } catch { } // 实时刷新面板
                VoiceListenerStatusCenter.Publish(VoiceListenerState.Recognizing, "会议助手分析中…");
            }
            catch { }
            finally { _refining = false; }
        }

        /// <summary>
        /// 把【当前会议状态 + 新增转写 delta】交给 LLM，返回刷新后的状态（JSON）。
        /// 失败 / 无 LLM Key 时返回 null，调用方保留旧状态。规模恒定，长会议安全。
        /// </summary>
        private async Task<MeetingState> TryUpdateStateAsync(MeetingState current, List<TranscriptTurn> delta)
        {
            try
            {
                var llm = GetLlmService();
                if (llm == null) return null;

                var deltaText = string.Join("\n", delta.Select(t => t.Text));
                if (string.IsNullOrWhiteSpace(deltaText)) return null;

                string json = await llm.GetCompletionAsync(
                    BuildStateUpdatePrompt(current, deltaText),
                    _llmLifecycleCts?.Token ?? System.Threading.CancellationToken.None);
                if (string.IsNullOrWhiteSpace(json) || json.Contains("LLM dummy response") || json.Contains("request cancelled")) return null;

                return ParseMeetingState(json, current);
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("会议状态增量更新失败，保留旧状态。", ex);
                return null;
            }
        }

        private string BuildStateUpdatePrompt(MeetingState current, string deltaTranscript)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是一个中文会议/交流的实时助手。我会给你【当前会议状态（JSON）】和【最近新增的转写片段】。");
            sb.AppendLine("请结合【会议类型】和【主题/术语】（如果有），把新增片段里有价值的信息并入状态，输出【更新后的完整状态 JSON】。规则：");
            sb.AppendLine("1) concepts：本场出现的新概念/术语，每条含 term（术语）与 explanation（一句话解释，便于参会者理解）。最多保留 20 条，过时的可合并或丢弃。");
            sb.AppendLine("2) questions：被提出、需要讨论或尚未解决的问题；若某问题在新增片段中被解答/达成共识，则把对应项 resolved 置为 true（保留该条以体现来龙去脉）。最多保留 15 条。");
            sb.AppendLine("3) decisions：已明确达成的共识/决策（简短一句）。最多保留 15 条。");
            sb.AppendLine("4) actions：从发言中浮现的、待办或已分配的行动（简短一句，不含隐私时尽量具体）。最多保留 20 条。");
            sb.AppendLine("5) summary：对目前会议的紧凑摘要（3-6 句），作为下一轮你理解上下文的锚，但不要重复罗列上面结构化的条目。");
            sb.AppendLine("只输出一个 JSON 对象，键严格为 concepts/questions/decisions/actions/summary，不要解释、不要 markdown 代码块包裹（若输出代码块也行，我会剥离）。");
            sb.AppendLine();
            sb.AppendLine($"【会议类型】{(_mode == CaptureMode.Meeting ? "会议" : _mode == CaptureMode.Quick ? "快捷口述" : "交流/讨论")}");
            if (!string.IsNullOrWhiteSpace(_contextTopic))
                sb.AppendLine($"【主题】{_contextTopic}");
            if (!string.IsNullOrWhiteSpace(_contextGlossary))
                sb.AppendLine($"【术语/专有名词】{_contextGlossary}");
            sb.AppendLine();
            sb.AppendLine("【当前会议状态 JSON】");
            sb.AppendLine(SerializeState(current));
            sb.AppendLine();
            sb.AppendLine("【最近新增转写片段】");
            sb.AppendLine(deltaTranscript);
            sb.AppendLine();
            sb.AppendLine("【更新后的完整状态 JSON】");
            return sb.ToString();
        }

        private static string SerializeState(MeetingState s)
        {
            if (s == null) return "{}";
            try
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    concepts = s.Concepts ?? new List<MeetingConcept>(),
                    questions = s.Questions ?? new List<MeetingQuestion>(),
                    decisions = s.Decisions ?? new List<string>(),
                    actions = s.Actions ?? new List<string>(),
                    summary = s.Summary ?? string.Empty
                });
            }
            catch { return "{}"; }
        }

        private static MeetingState ParseMeetingState(string json, MeetingState fallback)
        {
            try
            {
                json = StripCodeFences(json).Trim();
                int s = json.IndexOf('{');
                int e = json.LastIndexOf('}');
                if (s >= 0 && e > s) json = json.Substring(s, e - s + 1);
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                var st = new MeetingState();
                if (root["concepts"] is Newtonsoft.Json.Linq.JArray ca)
                    foreach (var it in ca)
                        st.Concepts.Add(new MeetingConcept
                        {
                            Term = (string)it["term"] ?? (string)it["Term"] ?? string.Empty,
                            Explanation = (string)it["explanation"] ?? (string)it["Explanation"] ?? string.Empty
                        });
                if (root["questions"] is Newtonsoft.Json.Linq.JArray qa)
                    foreach (var it in qa)
                        st.Questions.Add(new MeetingQuestion
                        {
                            Question = (string)it["question"] ?? (string)it["Question"] ?? string.Empty,
                            Resolved = (bool?)it["resolved"] ?? (bool?)it["Resolved"] ?? false
                        });
                if (root["decisions"] is Newtonsoft.Json.Linq.JArray da)
                    foreach (var it in da) { var v = (string)it; if (!string.IsNullOrWhiteSpace(v)) st.Decisions.Add(v); }
                if (root["actions"] is Newtonsoft.Json.Linq.JArray aa)
                    foreach (var it in aa) { var v = (string)it; if (!string.IsNullOrWhiteSpace(v)) st.Actions.Add(v); }
                st.Summary = (string)root["summary"] ?? fallback?.Summary ?? string.Empty;
                // 兜底：若解析出的结构为空（模型敷衍），保留旧状态，避免清零
                if (st.Concepts.Count == 0 && st.Questions.Count == 0 && st.Decisions.Count == 0 && st.Actions.Count == 0 && string.IsNullOrWhiteSpace(st.Summary))
                    return fallback ?? st;
                return st;
            }
            catch
            {
                return fallback;
            }
        }

        private void DetectAndRaiseNewItems(MeetingState state)
        {
            try
            {
                if (_prevConcepts.Count < state.Concepts.Count)
                    for (int i = _prevConcepts.Count; i < state.Concepts.Count; i++)
                        try { MeetingPrompt?.Invoke($"💡 新概念：{state.Concepts[i].Term} — {state.Concepts[i].Explanation}"); } catch { }
                if (_prevQuestions.Count < state.Questions.Count)
                    for (int i = _prevQuestions.Count; i < state.Questions.Count; i++)
                        try { MeetingPrompt?.Invoke($"❓ 待讨论：{state.Questions[i].Question}"); } catch { }
                if (_prevDecisions.Count < state.Decisions.Count)
                    for (int i = _prevDecisions.Count; i < state.Decisions.Count; i++)
                        try { MeetingPrompt?.Invoke($"✅ 决策：{state.Decisions[i]}"); } catch { }
                if (_prevActions.Count < state.Actions.Count)
                    for (int i = _prevActions.Count; i < state.Actions.Count; i++)
                        try { MeetingPrompt?.Invoke($"📌 行动项：{state.Actions[i]}"); } catch { }
                _prevConcepts = new List<MeetingConcept>(state.Concepts);
                _prevQuestions = new List<MeetingQuestion>(state.Questions);
                _prevDecisions = new List<string>(state.Decisions);
                _prevActions = new List<string>(state.Actions);
            }
            catch { }
        }

        private static string StripCodeFences(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            s = s.Trim();
            if (s.StartsWith("```"))
            {
                int nl = s.IndexOf('\n');
                if (nl >= 0) s = s.Substring(nl + 1);
                if (s.EndsWith("```")) s = s.Substring(0, s.Length - 3);
            }
            return s.Trim();
        }

        private void OnWatchdogTick(object sender, ElapsedEventArgs e)
        {
            if (!_recording) return;
            var now = DateTime.Now;

            if (_mode == CaptureMode.Quick)
            {
                // 讲过话后静默超时 → 结束；或一直没讲也别无限录，给一个最大等待
                bool silentTooLong = _hadSpeech && (now - _lastActivity) > _quickSilenceTimeout;
                bool neverSpokeTooLong = !_hadSpeech && (now - _startTime) > _quickMaxWait;
                if (silentTooLong || neverSpokeTooLong)
                    Stop();
            }
            else if ((now - _startTime) > _maxMeetingDuration)
            {
                Stop(); // 会议超时自动结束并弹收件箱
            }
        }

        // ---------- ASR 初始化（后台，与录音解耦）----------

        /// <summary>
        /// 异步初始化 ASR：启动即调用一次（构造函数里），之后幂等。
        /// 关键修复：旧实现用 bootstrap.Wait(20秒) 硬等 —— 大模型首跑 20 秒内几乎不可能下完，
        /// 且超时后即便后台下载完成也再没人把 _voskReady 置 true，导致收件箱永远为空。
        /// 这里改为：发起模型引导（下载/复用缓存），并用 continuation 在“真正就绪时”才置 _voskReady，
        /// 随后触发 ASR 泵线程回灌 pending 缓冲 —— 录音中途模型就绪也能补上实时转写。
        /// </summary>
        private void InitRecognizerBackground()
        {
            // FunASR 引擎（每会话检查；worker 常驻时瞬间完成）：
            // 未就绪期间音频进 pending 缓冲，就绪后回灌；超时按配置回落 Vosk。
            if (_engineKind == AsrEngineKind.FunAsr)
            {
                KickFunAsrReadiness();
            }

            bool firstInit = false;
            lock (_lock)
            {
                if (!_initStarted)
                {
                    _initStarted = true;
                    firstInit = true;
                }
            }

            if (!firstInit)
            {
                // 初始化已启动过：模型已常驻时，为新会话创建轻量识别器。
                // 关键修复：识别器在 Stop 时被置空（交后台收尾并释放），旧实现下第二次录音
                // 永远不会重建识别器——这里保证每一轮新会话都有自己的识别器。
                TryCreateSessionRecognizer();
                return;
            }

            try
            {
                _modelManager = new SpeechModelManager();
                _asrStatus = 1; // 加载中
                RaiseStatus(); // 若已有订阅方，立即反映“加载中”
                _bootstrapTask = _modelManager.EnsureReadyAsync();

                _bootstrapTask.ContinueWith(t =>
                {
                    try
                    {
                        var res = (t.Status == TaskStatus.RanToCompletion) ? t.Result : null;
                        if (res != null && res.IsReady
                            && !string.IsNullOrWhiteSpace(res.ModelDirectory)
                            && Directory.Exists(res.ModelDirectory))
                        {
                            Vosk.Vosk.SetLogLevel(-1);
                            if (_voskModel == null)
                                _voskModel = new Model(res.ModelDirectory);

                            TryCreateSessionRecognizer(); // 内部保证顺序：先识别器后 ready
                            Console.WriteLine("[ConversationCapture] Vosk ready (async).");
                        }
                        else
                        {
                            _asrStatus = 3; // 不可用
                            VoiceRuntimeLog.Info("Vosk 不可用：本次记录无实时转写，但音频已落盘。");
                            RaiseStatus();
                        }
                    }
                    catch (Exception ex)
                    {
                        _asrStatus = 3;
                        VoiceRuntimeLog.Error("Vosk 初始化失败：录音将继续，但本次无实时转写。", ex);
                        RaiseStatus();
                    }
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _asrStatus = 3;
                VoiceRuntimeLog.Error("Vosk 初始化启动失败。", ex);
            }
        }

        /// <summary>启动/检查 FunASR worker 就绪：成功即切换为高精度转写；失败按配置回落 Vosk。</summary>
        private void KickFunAsrReadiness()
        {
            if (_funasrReady) return;

            _asrStatus = 1; // 启动中（首次准备运行环境较慢，状态栏有提示）
            RaiseStatus();
            VoiceListenerStatusCenter.Publish(VoiceListenerState.Loading, "正在启动高精度识别引擎");

            Task.Run(async () =>
            {
                try
                {
                    bool ok = await _funasr.EnsureReadyAsync(TimeSpan.FromSeconds(FunAsrStartTimeoutSeconds))
                        .ConfigureAwait(false);
                    if (ok)
                    {
                        _funasrReady = true;
                        _asrStatus = 2;
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, "高精度识别就绪");
                        RaiseStatus();
                        try { _asrSignal.Set(); } catch { } // 唤醒泵回灌 pending
                        return;
                    }

                    // 未就绪：auto 模式回落 Vosk（模型已就绪则立即开转写，否则等模型下载完成时自动接入）
                    if (_engineAllowVoskFallback)
                    {
                        // 文案区分「仍在准备（下载继续，别担心）」与「确实不可用」
                        if (_funasr.IsPreparing)
                        {
                            VoiceRuntimeLog.Info("FunASR 本轮未就绪（模型仍在准备）：已回落 Vosk，准备完成后下场录音自动升级。");
                            VoiceListenerStatusCenter.Publish(VoiceListenerState.Loading,
                                "高精度模型首次准备中（约230MB，仅需一次），本轮用快速识别");
                        }
                        else
                        {
                            VoiceRuntimeLog.Info("FunASR 本轮不可用：已回落 Vosk。");
                            VoiceListenerStatusCenter.Publish(VoiceListenerState.Loading,
                                "高精度引擎暂不可用，本轮用快速识别");
                        }
                        _engineKind = AsrEngineKind.Vosk;
                        TryCreateSessionRecognizer();
                        RaiseStatus();
                    }
                    else
                    {
                        _asrStatus = 3;
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "高精度识别不可用（录音仍保存）");
                        VoiceRuntimeLog.Info("FunASR 不可用（ConversationCaptureAsrEngine=funasr 不回落）：本轮无实时转写，音频已落盘。");
                        RaiseStatus();
                    }
                }
                catch (Exception ex)
                {
                    _asrStatus = 3;
                    VoiceRuntimeLog.Error("FunASR 就绪检查失败。", ex);
                    RaiseStatus();
                }
            });
        }

        /// <summary>
        /// 创建本会话的轻量 Vosk 识别器（Model 常驻复用、Recognizer 每会话一个）。
        /// 模型未就绪或本会话已有识别器时为空操作；失败仅影响实时转写，录音与落盘不受影响。
        /// </summary>
        private void TryCreateSessionRecognizer()
        {
            bool created = false;
            try
            {
                lock (_lock)
                {
                    if (_voskModel == null) return;       // 模型仍未就绪（首次下载中），等 bootstrap 完成时再建
                    if (_voskRecognizer != null) return;  // 本会话已有识别器
                    var rec = new VoskRecognizer(_voskModel, 16000f);
                    rec.SetMaxAlternatives(1);
                    rec.SetWords(false);
                    _voskRecognizer = rec;                // 先赋值识别器，再置 ready
                    _voskReady = true;
                    // FunASR 仍是主引擎时，Vosk 只作待命备份：不发布「就绪」误导用户
                    if (_engineKind != AsrEngineKind.FunAsr || _funasrReady)
                    {
                        _asrStatus = 2; // 就绪
                    }
                    created = true;
                }
                if (created)
                {
                    RaiseStatus();                        // 通知托盘刷新 ASR 状态
                    try { _asrSignal.Set(); } catch { }   // 唤醒泵线程回灌 pending 缓冲
                }
            }
            catch (Exception ex)
            {
                _asrStatus = 3;
                VoiceRuntimeLog.Error("Vosk 识别器创建失败：本次无实时转写（录音照常落盘）。", ex);
                RaiseStatus();
            }
        }

        // ---------- 状态 ----------

        private void RaiseStatus()
        {
            try { StatusChanged?.Invoke(); } catch { }
        }

        // ---------- 配置读取 ----------

        private static bool ReadBool(string key, bool fallback)
        {
            try
            {
                var v = ConfigurationManager.AppSettings[key];
                return bool.TryParse(v, out bool p) ? p : fallback;
            }
            catch { return fallback; }
        }

        private static int ReadInt(string key, int fallback)
        {
            try
            {
                var v = ConfigurationManager.AppSettings[key];
                return int.TryParse(v, out int p) ? p : fallback;
            }
            catch { return fallback; }
        }

        public void Dispose()
        {
            try { _watchdog?.Stop(); } catch { }
            try { _watchdog?.Dispose(); } catch { }

            Task stopTask = Task.CompletedTask;
            try
            {
                // 仍在录音则先请求停止；随后**有界等待**收尾完成，保证：
                // WAV 文件头（RIFF 长度）写完、旧识别器不在使用中被 Dispose。
                Stop(true);
                lock (_lock) { stopTask = _stopTask ?? Task.CompletedTask; }
                if (!stopTask.IsCompleted)
                {
                    try { stopTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
                }
            }
            catch { }

            // 取消仍在途的 LLM 精修请求（注意：不放在 Stop() 里——停止后还有最终 NLP 抽取要用 LLM，
            // 只有服务整体拆卸时才应掐断所有模型调用）。
            try { _llmLifecycleCts?.Cancel(); } catch { }
            try { _llmLifecycleCts?.Dispose(); } catch { }

            // Vosk 对象的释放同样经 IO 锁兜底（若还有线程卡在原生调用，宁可跳过释放也不冒险并发）。
            bool tookVoskLock = false;
            try
            {
                tookVoskLock = Monitor.TryEnter(_voskIoLock, 1000);
                if (tookVoskLock)
                {
                    try { _voskRecognizer?.Dispose(); } catch { }
                    try { _voskModel?.Dispose(); } catch { }
                }
            }
            catch { }
            finally { if (tookVoskLock) { try { Monitor.Exit(_voskIoLock); } catch { } } }

            // FunASR 常驻 worker：服务拆卸时温和停机（进程树兜底），并清掉分段临时目录
            try { _funasr?.Shutdown(); } catch { }
            try
            {
                if (_funasrSegDir != null && Directory.Exists(_funasrSegDir))
                {
                    Directory.Delete(_funasrSegDir, recursive: true);
                }
            }
            catch { }
        }
    }
}
