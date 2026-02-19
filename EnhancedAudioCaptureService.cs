using System;
using System.IO;
using System.Linq;
using System.Configuration;
using System.Speech.Recognition;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using Newtonsoft.Json.Linq;
using Vosk;

namespace TimeTask
{
    /// <summary>
    /// 增强型智能语音监听服务
    /// - 降低资源消耗 (16kHz 采样率)
    /// - 只转文字，不保存音频
    /// - 本地意图识别 + 任务草稿生成
    /// - 静默运行，不打扰用户
    /// </summary>
    public sealed class EnhancedAudioCaptureService : IDisposable
    {
        private readonly object _lock = new object();
        private readonly TimeSpan _silenceTimeout;
        private readonly float _confidenceThreshold;
        private readonly SpeechModelManager _speechModelManager;
        private readonly Task<SpeechModelBootstrapResult> _modelBootstrapTask;
        private readonly bool _autoAddVoiceTasks;
        private readonly float _autoAddMinConfidence;
        private readonly bool _useLlmForVoiceQuadrant;
        private readonly bool _requireDraftConfirmation;
        private readonly bool _conversationExtractEnabled;
        private readonly TimeSpan _conversationWindow;
        private readonly int _conversationMinTurns;
        private DateTime _lastConversationExtract = DateTime.MinValue;
        private readonly List<(DateTime ts, string text)> _conversationBuffer = new List<(DateTime, string)>();
        private readonly SemaphoreSlim _llmSemaphore = new SemaphoreSlim(1, 1);
        private LlmService _llmService;
        private readonly SpeakerVerificationService _speakerService;
        private readonly bool _speakerVerifyEnabled;
        private readonly bool _speakerEnrollMode;
        private readonly double _speakerThreshold;
        private readonly double _speakerMinSeconds;
        private bool _lastSpeakerVerified;
        private DateTime _lastSpeakerVerifyTime = DateTime.MinValue;
        private readonly List<byte> _speechBuffer = new List<byte>(16000 * 2 * 5);

        private SpeechRecognitionEngine _recognizer;
        private Model _voskModel;
        private VoskRecognizer _voskRecognizer;
        private bool _voskEnabled;
        private bool _voskFormatWarningLogged;
        private readonly bool _useStrictVoskGrammar;
        private readonly bool _systemSpeechUseHints;
        private readonly HashSet<string> _hintPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _asrProvider;
        private readonly bool _funAsrEnabled;
        private readonly bool _funAsrOnlyMode;
        private string _funAsrPythonExe;
        private readonly string _funAsrScriptPath;
        private readonly string _funAsrModel;
        private readonly string _funAsrDevice;
        private readonly int _funAsrTimeoutSeconds;
        private readonly int _funAsrWorkerStartupTimeoutSeconds;
        private readonly bool _funAsrUsePersistentWorker;
        private readonly double _funAsrMinSegmentSeconds;
        private Task<FunAsrRuntimeBootstrapResult> _funAsrBootstrapTask;
        private readonly List<byte> _asrSegmentBuffer = new List<byte>(16000 * 2 * 12);
        private readonly SemaphoreSlim _funAsrRecognitionSemaphore = new SemaphoreSlim(1, 1);
        private Process _funAsrWorkerProcess;
        private StreamWriter _funAsrWorkerInput;
        private StreamReader _funAsrWorkerOutput;
        private string _funAsrWorkerLastErr = string.Empty;
        private readonly object _funAsrWorkerErrLock = new object();
        private DateTime _funAsrWorkerStdoutLogUtc = DateTime.MinValue;
        private bool _funAsrScriptMissingLogged;
        private bool _funAsrPythonMissingLogged;
        private bool _funAsrRepairInProgress;
        private int _funAsrAutoRepairAttempts;
        private int _funAsrFailureCount;
        private bool _classicFallbackEnabled;
        private bool _funAsrRuntimeReady;
        private bool _funAsrBootstrapMonitorStarted;
        private CancellationTokenSource _funAsrRetryCts;
        private int _recognizingStateEpoch;
        private DateTime _lastRecognizingTouchUtc = DateTime.MinValue;
        private string _lastRecognizedText;
        private DateTime _lastRecognizedTextTime = DateTime.MinValue;
        private DateTime _lastSpeechTime = DateTime.MinValue;
        private System.Timers.Timer _silenceTimer;

        private WaveInEvent _waveIn;
        private WasapiCapture _wasapi;

        private bool _enabled;

        // 意图识别和草稿管理
        private IntentRecognizer _intentRecognizer;
        private TaskDraftManager _draftManager;
        private VoiceReminderTimeParser _reminderTimeParser;

        // VAD 参数 - 更保守的配置
        private double _energyThresholdDb = -30.0; // 能量阈值，-30dBFS 表示要有一定音量
        private int _minStartMs = 260;              // 连续高能量达到 260ms 即可开始，减少首句漏检
        private int _hangoverMs = 900;              // 连续低能量达到 900ms 即停止，提升响应速度
        private int _consecAboveMs = 0;
        private int _consecBelowMs = 0;
        private DateTime _lastAudioStateSpeech = DateTime.MinValue;

        // 是否正在录音（用于写入文件）
        private bool _isRecording = false;

        // 统计
        public int TotalSpeechDetections { get; private set; }
        public int TotalPotentialTasks { get; private set; }
        public DateTime? LastDetectionTime { get; private set; }

        public EnhancedAudioCaptureService(TimeSpan? silenceTimeout = null, float confidenceThreshold = 0.5f)
        {
            _silenceTimeout = silenceTimeout ?? TimeSpan.FromSeconds(30);
            _confidenceThreshold = confidenceThreshold;

            _intentRecognizer = new IntentRecognizer();
            _draftManager = new TaskDraftManager();
            _reminderTimeParser = new VoiceReminderTimeParser();
            _speechModelManager = new SpeechModelManager();
            _modelBootstrapTask = _speechModelManager.EnsureReadyAsync();
            _autoAddVoiceTasks = ReadBoolSetting("VoiceAutoAddToQuadrant", false);
            _autoAddMinConfidence = ReadFloatSetting("VoiceAutoAddMinConfidence", 0.65f);
            _useLlmForVoiceQuadrant = ReadBoolSetting("VoiceUseLlmQuadrant", false);
            _requireDraftConfirmation = ReadBoolSetting("VoiceRequireConfirmation", true);
            _conversationExtractEnabled = ReadBoolSetting("VoiceConversationExtractEnabled", true);
            _conversationWindow = TimeSpan.FromSeconds(ReadIntSetting("VoiceConversationWindowSeconds", 45));
            _conversationMinTurns = ReadIntSetting("VoiceConversationMinTurns", 3);
            _speakerService = new SpeakerVerificationService();
            _speakerVerifyEnabled = ReadBoolSetting("VoiceSpeakerVerifyEnabled", false);
            _speakerEnrollMode = ReadBoolSetting("VoiceSpeakerEnrollMode", false);
            _speakerThreshold = ReadDoubleSetting("VoiceSpeakerThreshold", 0.72);
            _speakerMinSeconds = ReadDoubleSetting("VoiceSpeakerMinSeconds", 2.0);
            _useStrictVoskGrammar = ReadBoolSetting("VoiceUseStrictVoskGrammar", false);
            _systemSpeechUseHints = ReadBoolSetting("VoiceSystemSpeechUseHints", false);
            _asrProvider = ReadStringSetting("VoiceAsrProvider", "hybrid");
            _funAsrEnabled = _asrProvider.IndexOf("funasr", StringComparison.OrdinalIgnoreCase) >= 0;
            _funAsrOnlyMode = string.Equals(_asrProvider, "funasr", StringComparison.OrdinalIgnoreCase);
            _funAsrPythonExe = ReadStringSetting("FunAsrPythonExe", "python");
            _funAsrScriptPath = ReadStringSetting("FunAsrScriptPath", @"scripts\funasr_asr.py");
            _funAsrModel = ReadStringSetting("FunAsrModel", "iic/SenseVoiceSmall");
            _funAsrDevice = ReadStringSetting("FunAsrDevice", "cpu");
            _funAsrTimeoutSeconds = ReadIntSetting("FunAsrTimeoutSeconds", 45);
            _funAsrWorkerStartupTimeoutSeconds = ReadIntSetting("FunAsrWorkerStartupTimeoutSeconds", 600);
            _funAsrUsePersistentWorker = ReadBoolSetting("FunAsrUsePersistentWorker", true);
            _funAsrMinSegmentSeconds = ReadDoubleSetting("FunAsrMinSegmentSeconds", 0.5);
            _energyThresholdDb = ReadDoubleSetting("VoiceEnergyThresholdDb", -30.0);
            _minStartMs = ReadIntSetting("VoiceVadMinStartMs", 260);
            _hangoverMs = ReadIntSetting("VoiceVadHangoverMs", 900);
            if (_funAsrEnabled)
            {
                VoiceRuntimeLog.Info($"EnhancedAudioCaptureService ctor: requesting FunASR bootstrap task. provider={_asrProvider}");
                _funAsrBootstrapTask = FunAsrRuntimeManager.EnsureReadyAsync();
            }
            else
            {
                VoiceRuntimeLog.Info($"EnhancedAudioCaptureService ctor: FunASR disabled by provider={_asrProvider}");
            }
        }

        /// <summary>
        /// 启动持续监听
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_enabled) return;
                _enabled = true;
                VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音监听不可用（初始化中）");

                Console.WriteLine("[EnhancedAudioCaptureService] Starting...");
                VoiceRuntimeLog.Info("EnhancedAudioCaptureService starting.");

                bool voskReady = false;
                if (!_funAsrOnlyMode)
                {
                    // 初始化语音识别
                    try
                    {
                        TryWaitModelBootstrap();
                        voskReady = TryInitializeVoskRecognizer();

                        if (!voskReady)
                        {
                            InitializeSystemSpeechRecognizer();
                        }

                        if (!voskReady && _recognizer == null && !_funAsrEnabled)
                        {
                            throw new InvalidOperationException("Vosk 和 System.Speech 均未初始化成功。");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EnhancedAudioCaptureService] Failed to start speech recognition: {ex.Message}");
                        VoiceRuntimeLog.Error("Failed to start speech recognition.", ex);
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音引擎初始化失败");
                        throw new InvalidOperationException("语音识别初始化失败，请检查系统语音识别组件和麦克风权限。", ex);
                    }
                }

                if (_funAsrEnabled)
                {
                    StartFunAsrBootstrapMonitor();
                    TryWaitFunAsrBootstrap(TimeSpan.FromSeconds(2));
                    var resolvedScript = ResolveFunAsrScriptPath();
                    if (string.IsNullOrWhiteSpace(resolvedScript))
                    {
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "FunASR 脚本缺失，语音监听不可用");
                        throw new InvalidOperationException($"FunASR 脚本不存在：{_funAsrScriptPath}");
                    }
                    VoiceRuntimeLog.Info($"FunASR subprocess enabled. provider={_asrProvider}, python={_funAsrPythonExe}, script={resolvedScript}, model={_funAsrModel}, device={_funAsrDevice}, persistentWorker={_funAsrUsePersistentWorker}, timeoutSec={_funAsrTimeoutSeconds}");
                }

                // 启动静音检测定时器
                _silenceTimer = new System.Timers.Timer(1000);
                _silenceTimer.Elapsed += OnSilenceTick;
                _silenceTimer.AutoReset = true;
                _silenceTimer.Start();

                // 启动音频采集
                StartAudioCapture();

                if (!voskReady && !_funAsrOnlyMode)
                {
                    HookLateVoskInitialization();
                }

                Console.WriteLine("[EnhancedAudioCaptureService] Started successfully.");
                VoiceRuntimeLog.Info("EnhancedAudioCaptureService started successfully.");

                bool canListen = IsRecognitionPipelineAvailable();
                if (canListen)
                {
                    VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, "语音监听可用");
                }
                else
                {
                    VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "FunASR 尚未就绪，语音监听不可用");
                }
            }
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _enabled = false;

                Console.WriteLine("[EnhancedAudioCaptureService] Stopping...");

                try { _silenceTimer?.Stop(); } catch { }
                _silenceTimer?.Dispose();
                _silenceTimer = null;

                if (_recognizer != null)
                {
                    try { _recognizer.RecognizeAsyncCancel(); } catch { }
                    try { _recognizer.RecognizeAsyncStop(); } catch { }
                    _recognizer.SpeechRecognized -= OnSpeechRecognized;
                    _recognizer.SpeechHypothesized -= OnSpeechHypothesized;
                    _recognizer.AudioStateChanged -= OnAudioStateChanged;
                    _recognizer.Dispose();
                    _recognizer = null;
                }

                if (_voskRecognizer != null)
                {
                    try { _voskRecognizer.Dispose(); } catch { }
                    _voskRecognizer = null;
                }
                if (_voskModel != null)
                {
                    try { _voskModel.Dispose(); } catch { }
                    _voskModel = null;
                }
                _voskEnabled = false;

                StopAudioCapture();
                CancelFunAsrRetry();
                StopFunAsrWorker();

                Console.WriteLine("[EnhancedAudioCaptureService] Stopped.");
                VoiceRuntimeLog.Info("EnhancedAudioCaptureService stopped.");
                ResetRecognizingStateTimer();
                VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音监听已停止");
            }
        }

        private void StartAudioCapture()
        {
            try
            {
                // 使用 16kHz 采样率 (语音足够用，降低资源消耗)
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 16, 1), // 16kHz, 16bit, Mono
                    BufferMilliseconds = 200
                };
                _waveIn.DataAvailable += OnWaveData;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                Console.WriteLine("[EnhancedAudioCaptureService] Audio capture started (16kHz).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnhancedAudioCaptureService] WaveIn start failed: {ex.Message}");
                VoiceRuntimeLog.Error("WaveIn start failed.", ex);
                TryFallbackToWasapi();
            }
        }

        private void TryFallbackToWasapi()
        {
            try
            {
                if (_waveIn != null)
                {
                    try { _waveIn.DataAvailable -= OnWaveData; } catch { }
                    try { _waveIn.RecordingStopped -= OnRecordingStopped; } catch { }
                    _waveIn.Dispose();
                }
                _waveIn = null;

                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                _wasapi = new WasapiCapture(device);
                _wasapi.ShareMode = AudioClientShareMode.Shared;
                // 尽量统一到 16k/16bit/mono，避免 Vosk 因格式不匹配而被跳过。
                _wasapi.WaveFormat = new WaveFormat(16000, 16, 1);
                _wasapi.DataAvailable += OnWaveData;
                _wasapi.RecordingStopped += OnRecordingStopped;
                _wasapi.StartRecording();
                Console.WriteLine("[EnhancedAudioCaptureService] Fallback to WASAPI successful.");
                VoiceRuntimeLog.Info("WASAPI fallback capture started.");
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"[EnhancedAudioCaptureService] WASAPI fallback failed: {ex2.Message}");
                VoiceRuntimeLog.Error("WASAPI fallback failed.", ex2);
            }
        }

        private void StopAudioCapture()
        {
            try
            {
                _isRecording = false;
                if (_waveIn != null)
                {
                    try { _waveIn.StopRecording(); } catch { }
                }
                if (_wasapi != null)
                {
                    try { _wasapi.StopRecording(); } catch { }
                }
            }
            catch { }
        }

        private void OnSpeechHypothesized(object sender, SpeechHypothesizedEventArgs e)
        {
            if (!_enabled) return;
            _lastSpeechTime = DateTime.UtcNow;
        }

        private void OnAudioStateChanged(object sender, AudioStateChangedEventArgs e)
        {
            if (e.AudioState == AudioState.Speech)
            {
                _lastAudioStateSpeech = DateTime.UtcNow;
            }
        }

        private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            if (!_enabled || e == null || e.Result == null) return;

            var bestCandidate = SelectBestCandidate(e);
            if (bestCandidate == null)
            {
                VoiceRuntimeLog.Info($"System.Speech ignored: text={e.Result.Text}, conf={e.Result.Confidence:F2}");
                return;
            }

            HandleRecognizedText(bestCandidate.Text, bestCandidate.Confidence, "system-speech");
        }

        private string CleanRecognizedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 清理 SenseVoice 常见标签，如 <|zh|><|NEUTRAL|><|Speech|>
            text = Regex.Replace(text, @"<\|[^|>]+\|>", "");

            // 移除多余的空白
            text = Regex.Replace(text, @"\s+", " ");
            text = text.Trim();

            // 移除常见的识别错误
            text = Regex.Replace(text, @"^[,，。.]\s*", "");
            text = text.Trim(',', '。', '.', '，');

            // 合并中文字符之间的空格（例如 "我 明天 的 会议" -> "我明天的会议"）
            text = Regex.Replace(text, @"(?<=[\u4e00-\u9fa5])\s+(?=[\u4e00-\u9fa5])", "");

            // 常见口头/识别噪声前缀清理
            text = Regex.Replace(text, @"^(编辑行|编辑|嗯|那个|就是|请|麻烦)\s*", "", RegexOptions.IgnoreCase);

            // 若只剩问号/占位符，视为无效文本
            if (Regex.IsMatch(text, @"^[\?\uFF1F\.\,\!\s]+$"))
            {
                return string.Empty;
            }

            return text;
        }

        private TaskDraft ProcessPotentialTask(string text, float confidence)
        {
            try
            {
                // 提取任务描述
                string cleanedText = _intentRecognizer.ExtractTaskDescription(text);
                if (string.IsNullOrWhiteSpace(cleanedText))
                    return null;

                DateTime? reminderTime = null;
                if (_reminderTimeParser != null && _reminderTimeParser.TryParse(text, DateTime.Now, out DateTime parsedTime))
                {
                    reminderTime = parsedTime;
                }

                // 估计优先级
                var (importance, urgency) = _intentRecognizer.EstimatePriority(cleanedText);

                // 估计象限
                string quadrant = _intentRecognizer.EstimateQuadrant(importance, urgency);

                // 创建草稿
                var draft = new TaskDraft
                {
                    RawText = text,
                    CleanedText = cleanedText,
                    ReminderTime = reminderTime,
                    ReminderHintText = reminderTime.HasValue ? reminderTime.Value.ToString("yyyy-MM-dd HH:mm") : null,
                    Importance = importance,
                    Urgency = urgency,
                    EstimatedQuadrant = quadrant,
                    Source = "voice"
                };

                _draftManager.AddDraft(draft);

                Console.WriteLine($"[EnhancedAudioCaptureService] Task draft created: \"{cleanedText}\" (Quadrant: {quadrant}, Conf: {confidence:P0}, Reminder: {reminderTime?.ToString("yyyy-MM-dd HH:mm") ?? "none"})");
                VoiceRuntimeLog.Info($"Task draft created from voice. text={cleanedText}, quadrant={quadrant}, conf={confidence:F2}, reminder={reminderTime?.ToString("o") ?? "none"}");
                return draft;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnhancedAudioCaptureService] Error processing potential task: {ex.Message}");
                VoiceRuntimeLog.Error("ProcessPotentialTask failed.", ex);
                return null;
            }
        }

        private void OnSilenceTick(object sender, ElapsedEventArgs e)
        {
            if (!_enabled) return;

            var now = DateTime.UtcNow;

            // 超过静默阈值则停止录音
            if (_isRecording && now - _lastSpeechTime > _silenceTimeout)
            {
                lock (_lock)
                {
                    if (_isRecording && now - _lastSpeechTime > _silenceTimeout)
                    {
                        StopAudioCapture();
                        Console.WriteLine("[EnhancedAudioCaptureService] Silence timeout, stopped recording.");
                        EvaluateCompletedSegment(_waveIn?.WaveFormat ?? _wasapi?.WaveFormat);
                    }
                }
            }
        }

        private void OnWaveData(object sender, WaveInEventArgs e)
        {
            if (!_enabled) return;

            var format = _waveIn?.WaveFormat ?? (_wasapi?.WaveFormat);
            if (format == null || e.BytesRecorded <= 0) return;

            // 在 funasr-only 且运行时未就绪时，不进入分段录音流程，避免“界面不可用但后台在录音”的矛盾状态。
            if (!IsRecognitionPipelineAvailable())
            {
                lock (_lock)
                {
                    _isRecording = false;
                    _consecAboveMs = 0;
                    _consecBelowMs = 0;
                }

                lock (_speechBuffer) { _speechBuffer.Clear(); }
                lock (_asrSegmentBuffer) { _asrSegmentBuffer.Clear(); }
                return;
            }

            if (!string.Equals(_asrProvider, "funasr", StringComparison.OrdinalIgnoreCase) || _classicFallbackEnabled)
            {
                TryProcessVoskAudio(e.Buffer, e.BytesRecorded, format);
            }
            CaptureSpeechBufferIfNeeded(e.Buffer, e.BytesRecorded);

            // 计算帧时长（毫秒）
            int bytesPerMs = format.AverageBytesPerSecond / 1000;
            int frameMs = Math.Max(1, e.BytesRecorded / Math.Max(1, bytesPerMs));

            // 计算 dBFS
            double db = ComputeDbfs(e.Buffer, e.BytesRecorded, format);

            bool above = db >= _energyThresholdDb;

            lock (_lock)
            {
                if (above)
                {
                    _consecAboveMs += frameMs;
                    _consecBelowMs = 0;
                    _lastSpeechTime = DateTime.UtcNow;
                }
                else
                {
                    _consecBelowMs += frameMs;
                    _consecAboveMs = 0;
                }
            }

            bool recentAudioSpeech = (DateTime.UtcNow - _lastAudioStateSpeech) < TimeSpan.FromSeconds(1.0);
            bool vadOnlyStart = string.Equals(_asrProvider, "funasr", StringComparison.OrdinalIgnoreCase);

            lock (_lock)
            {
                // 开始录音条件
                if (!_isRecording)
                {
                    if ((recentAudioSpeech || vadOnlyStart) && _consecAboveMs >= _minStartMs)
                    {
                        _isRecording = true;
                        TouchRecognizingState();
                        Console.WriteLine("[EnhancedAudioCaptureService] Started recording (VAD triggered).");
                    }
                }
                else
                {
                    if (above || _consecBelowMs < _hangoverMs)
                    {
                        TouchRecognizingState();
                    }

                    // 停止录音条件
                    if (_consecBelowMs >= _hangoverMs)
                    {
                        _isRecording = false;
                        Console.WriteLine("[EnhancedAudioCaptureService] Stopped recording (silence detected).");
                        EvaluateCompletedSegment(format);
                    }
                }

                // 注意：这里不再写入文件，只做 VAD 检测
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            lock (_lock)
            {
                _isRecording = false;

                try
                {
                    if (_waveIn != null)
                    {
                        _waveIn.DataAvailable -= OnWaveData;
                        _waveIn.RecordingStopped -= OnRecordingStopped;
                    }
                    if (_wasapi != null)
                    {
                        _wasapi.DataAvailable -= OnWaveData;
                        _wasapi.RecordingStopped -= OnRecordingStopped;
                    }
                }
                catch { }

                _waveIn?.Dispose();
                _waveIn = null;
                _wasapi?.Dispose();
                _wasapi = null;
            }
        }

        private static double ComputeDbfs(byte[] buffer, int bytes, WaveFormat format)
        {
            try
            {
                if (format.BitsPerSample == 16)
                {
                    int samples = bytes / 2;
                    if (samples == 0) return double.NegativeInfinity;
                    double sumSq = 0;
                    for (int i = 0; i < bytes; i += 2)
                    {
                        short s = BitConverter.ToInt16(buffer, i);
                        double norm = s / 32768.0;
                        sumSq += norm * norm;
                    }
                    double rms = Math.Sqrt(sumSq / samples);
                    double dbfs = 20.0 * Math.Log10(rms + 1e-12);
                    return (!double.IsNaN(dbfs) && !double.IsInfinity(dbfs)) ? dbfs : double.NegativeInfinity;
                }
            }
            catch { }
            return double.NegativeInfinity;
        }

        private void TryUpdateRecognizerSetting(string name, TimeSpan value)
        {
            try { _recognizer?.UpdateRecognizerSetting(name, (int)value.TotalMilliseconds); }
            catch { }
        }

        private void TryWaitModelBootstrap()
        {
            try
            {
                if (_modelBootstrapTask == null) return;

                if (_modelBootstrapTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    var result = _modelBootstrapTask.Result;
                    Console.WriteLine($"[EnhancedAudioCaptureService] Model bootstrap ready={result.IsReady}, source={result.Source}, msg={result.Message}");
                    VoiceRuntimeLog.Info($"Model bootstrap result: ready={result.IsReady}, source={result.Source}, msg={result.Message}");
                }
                else
                {
                    Console.WriteLine("[EnhancedAudioCaptureService] Model bootstrap running in background.");
                    VoiceRuntimeLog.Info("Model bootstrap is still running in background.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnhancedAudioCaptureService] Model bootstrap check failed: {ex.Message}");
                VoiceRuntimeLog.Error("Model bootstrap check failed.", ex);
            }
        }

        private void TryLoadHintsGrammar()
        {
            try
            {
                string hintsPath = _speechModelManager.GetHintsFilePath();
                if (!File.Exists(hintsPath))
                    return;

                var phrases = File.ReadAllLines(hintsPath, Encoding.UTF8)
                    .Select(l => l?.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(300)
                    .ToList();

                if (!phrases.Any())
                    return;

                _hintPhrases.Clear();
                foreach (var phrase in phrases)
                {
                    _hintPhrases.Add(phrase);
                }

                var choices = new Choices(phrases.ToArray());
                var grammar = new Grammar(new GrammarBuilder(choices))
                {
                    Name = "task-hints"
                };
                _recognizer.LoadGrammar(grammar);
                Console.WriteLine($"[EnhancedAudioCaptureService] Loaded hints grammar, phrases={phrases.Count}");
                VoiceRuntimeLog.Info($"Hints grammar loaded, count={phrases.Count}, file={hintsPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnhancedAudioCaptureService] Failed to load hints grammar: {ex.Message}");
                VoiceRuntimeLog.Error("Failed to load hints grammar.", ex);
            }
        }

        private void TryLoadVoskGrammar()
        {
            try
            {
                string hintsPath = _speechModelManager.GetHintsFilePath();
                if (!File.Exists(hintsPath))
                    return;

                var phrases = File.ReadAllLines(hintsPath, Encoding.UTF8)
                    .Select(l => l?.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(300)
                    .ToList();

                if (!phrases.Any())
                    return;

                _hintPhrases.Clear();
                foreach (var phrase in phrases)
                {
                    _hintPhrases.Add(phrase);
                }

                if (!_useStrictVoskGrammar)
                {
                    VoiceRuntimeLog.Info("Vosk grammar hints loaded for ranking only (strict grammar disabled).");
                    return;
                }

                if (!phrases.Contains("[unk]", StringComparer.OrdinalIgnoreCase))
                {
                    phrases.Add("[unk]");
                }

                string grammarJson = Newtonsoft.Json.JsonConvert.SerializeObject(phrases);
                if (_voskRecognizer != null)
                {
                    var method = _voskRecognizer.GetType().GetMethod("SetGrammar", new[] { typeof(string) });
                    if (method != null)
                    {
                        method.Invoke(_voskRecognizer, new object[] { grammarJson });
                        VoiceRuntimeLog.Info($"Vosk grammar loaded, phrases={phrases.Count}");
                    }
                    else
                    {
                        VoiceRuntimeLog.Info("Vosk grammar not supported by current Vosk library. Skip SetGrammar.");
                    }
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Failed to load Vosk grammar.", ex);
            }
        }

        private bool TryInitializeVoskRecognizer()
        {
            try
            {
                if (_modelBootstrapTask == null || !_modelBootstrapTask.IsCompleted)
                {
                    VoiceRuntimeLog.Info("Vosk init skipped: model bootstrap not completed yet.");
                    return false;
                }

                var result = _modelBootstrapTask.Result;
                if (!result.IsReady || string.IsNullOrWhiteSpace(result.ModelDirectory) || !Directory.Exists(result.ModelDirectory))
                {
                    VoiceRuntimeLog.Info($"Vosk init skipped: model not ready. msg={result.Message}");
                    return false;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string nativeDll = Path.Combine(baseDir, "libvosk.dll");
                VoiceRuntimeLog.Info($"Vosk native dll exists: {File.Exists(nativeDll)} path={nativeDll}");

                Vosk.Vosk.SetLogLevel(-1);
                _voskModel = new Model(result.ModelDirectory);
                _voskRecognizer = new VoskRecognizer(_voskModel, 16000.0f);
                _voskRecognizer.SetMaxAlternatives(3);
                _voskRecognizer.SetWords(true);
                TryLoadVoskGrammar();
                _voskEnabled = true;

                VoiceRuntimeLog.Info($"Vosk recognizer initialized. modelDir={result.ModelDirectory}");
                return true;
            }
            catch (Exception ex)
            {
                _voskEnabled = false;
                VoiceRuntimeLog.Error($"Vosk recognizer initialization failed. ProcessBitness={(Environment.Is64BitProcess ? "x64" : "x86")}", ex);
                return false;
            }
        }

        private void HookLateVoskInitialization()
        {
            if (_modelBootstrapTask == null || _modelBootstrapTask.IsCompleted)
                return;

            _modelBootstrapTask.ContinueWith(t =>
            {
                if (t.IsFaulted || !_enabled)
                    return;

                lock (_lock)
                {
                    if (!_enabled || _voskEnabled)
                        return;

                    bool ready = TryInitializeVoskRecognizer();
                    if (ready)
                    {
                        VoiceRuntimeLog.Info("Vosk recognizer initialized after delayed model bootstrap.");
                    }
                }
            }, TaskScheduler.Default);
        }

        private void InitializeSystemSpeechRecognizer()
        {
            _recognizer = CreateRecognizer();
            _recognizer.SetInputToDefaultAudioDevice();
            _recognizer.LoadGrammar(new DictationGrammar());
            if (_systemSpeechUseHints)
            {
                TryLoadHintsGrammar();
            }

            TryUpdateRecognizerSetting("BabbleTimeout", TimeSpan.FromSeconds(3));
            TryUpdateRecognizerSetting("InitialSilenceTimeout", TimeSpan.FromSeconds(6));

            _recognizer.SpeechRecognized += OnSpeechRecognized;
            _recognizer.SpeechHypothesized += OnSpeechHypothesized;
            _recognizer.AudioStateChanged += OnAudioStateChanged;
            _recognizer.RecognizeAsync(RecognizeMode.Multiple);

            Console.WriteLine("[EnhancedAudioCaptureService] System.Speech recognition started.");
            VoiceRuntimeLog.Info("System.Speech recognizer started.");
        }

        private void TryProcessVoskAudio(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            if (!_voskEnabled || _voskRecognizer == null)
                return;

            if (format.SampleRate != 16000 || format.BitsPerSample != 16 || format.Channels != 1)
            {
                if (!_voskFormatWarningLogged)
                {
                    _voskFormatWarningLogged = true;
                    VoiceRuntimeLog.Info($"Vosk audio format not supported directly: {format.SampleRate}Hz/{format.BitsPerSample}bit/{format.Channels}ch");
                }
                return;
            }

            bool isFinal = _voskRecognizer.AcceptWaveform(buffer, bytesRecorded);
            if (!isFinal)
                return;

            string finalJson = _voskRecognizer.Result();
            var parsed = ParseVoskFinalResult(finalJson);
            if (string.IsNullOrWhiteSpace(parsed.text))
                return;

            HandleRecognizedText(parsed.text, parsed.confidence, "vosk");
        }

        private (string text, float confidence) ParseVoskFinalResult(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                    return (string.Empty, 0f);

                var root = JObject.Parse(json);
                var words = root["result"] as JArray;
                float wordAvgConf = 0.6f;
                if (words != null && words.Count > 0)
                {
                    var confs = words
                        .OfType<JObject>()
                        .Select(w => (float?)w["conf"])
                        .Where(c => c.HasValue)
                        .Select(c => c.Value)
                        .ToList();

                    if (confs.Count > 0)
                    {
                        wordAvgConf = confs.Average();
                    }
                }

                var alternatives = root["alternatives"] as JArray;
                if (alternatives != null && alternatives.Count > 0)
                {
                    var bestAlt = alternatives
                        .OfType<JObject>()
                        .Select(x => new
                        {
                            Text = (string)x["text"],
                            Confidence = (float?)x["confidence"] ?? wordAvgConf
                        })
                        .Select(x => new
                        {
                            x.Text,
                            x.Confidence,
                            Score = ScoreAlt(x.Text, x.Confidence)
                        })
                        .OrderByDescending(x => x.Score)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Text));

                    if (bestAlt != null)
                        return (bestAlt.Text, ClampConfidence(bestAlt.Confidence));
                }

                string text = (string)root["text"] ?? string.Empty;
                if (words == null || words.Count == 0)
                    return (text, wordAvgConf);

                return (text, ClampConfidence(wordAvgConf));
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Failed to parse Vosk result json.", ex);
                return (string.Empty, 0f);
            }
        }

        private double ScoreAlt(string text, float confidence)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string normalized = CleanRecognizedText(text);
            if (string.IsNullOrWhiteSpace(normalized))
                return 0;

            double score = ClampConfidence(confidence);
            score += (_intentRecognizer?.ScoreTaskLikelihood(normalized) ?? 0) * 0.25;
            score += ScoreHintMatch(normalized);

            if (Regex.IsMatch(normalized, @"^(嗯+|啊+|额+|哦+|那个|就是)$", RegexOptions.IgnoreCase))
            {
                score -= 0.25;
            }
            if (normalized.Length <= 2)
            {
                score -= 0.15;
            }
            return score;
        }

        private double ScoreHintMatch(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _hintPhrases.Count == 0)
                return 0;

            string normalized = text.Trim();
            if (_hintPhrases.Contains(normalized))
                return 0.20;

            bool containsHint = _hintPhrases.Any(p => p.Length >= 2 && normalized.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
            if (containsHint)
                return 0.10;

            return 0;
        }

        private static float ClampConfidence(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;

            if (value > 1.0f)
            {
                // 兜底：异常大值统一压到 1.0
                return 1.0f;
            }
            if (value < 0f)
                return 0f;
            return value;
        }

        private void HandleRecognizedText(string rawText, float confidence, string source)
        {
            string text = CleanRecognizedText(rawText);
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                return;

            if (IsDuplicateRecognition(text))
                return;

            if (_speakerVerifyEnabled && !IsSpeakerVerified())
            {
                VoiceRuntimeLog.Info("Speaker verification failed or not confirmed. Ignoring recognition.");
                return;
            }

            TotalSpeechDetections++;
            LastDetectionTime = DateTime.Now;
            _lastSpeechTime = DateTime.UtcNow;
            MarkRecognizingState();

            VoiceRuntimeLog.Info($"Recognized({source}) conf={confidence:F2} text={text}");
            VoiceListenerStatusCenter.PublishRecognition(new VoiceRecognitionRecord
            {
                Text = text,
                Confidence = ClampConfidence(confidence),
                Source = source,
                CapturedAtUtc = DateTime.UtcNow,
                AudioPcm16Mono = SnapshotSpeechPcm()
            });

            bool isSystemFallback = _classicFallbackEnabled && string.Equals(source, "system-speech", StringComparison.OrdinalIgnoreCase);
            double taskLikelihood = _intentRecognizer.ScoreTaskLikelihood(text);
            bool reminderLike = _intentRecognizer.IsReminderLike(text);
            float minConfidence = isSystemFallback
                ? Math.Max(0.05f, _confidenceThreshold * 0.12f)
                : _confidenceThreshold * 0.45f;

            if (reminderLike)
            {
                minConfidence = Math.Max(0.15f, minConfidence * 0.6f);
            }

            if (confidence < minConfidence && taskLikelihood < 0.45 && !reminderLike)
                return;

            if (_intentRecognizer.IsPotentialTask(text) || reminderLike)
            {
                TotalPotentialTasks++;
                var draft = ProcessPotentialTask(text, confidence);
                if (draft != null)
                {
                    if (_useLlmForVoiceQuadrant)
                    {
                        EnqueueLlmQuadrant(draft, confidence);
                    }
                    else if (!_requireDraftConfirmation)
                    {
                        TryAutoAddTaskToQuadrant(draft, confidence);
                    }
                }
            }
            else
            {
                VoiceRuntimeLog.Info($"Skip draft: not task-like. text={text}");
            }

            BufferConversation(text);
            TryExtractConversationTasksAsync();
        }

        private byte[] SnapshotSpeechPcm()
        {
            lock (_speechBuffer)
            {
                if (_speechBuffer.Count == 0)
                    return null;

                return _speechBuffer.ToArray();
            }
        }

        private bool IsDuplicateRecognition(string text)
        {
            if (string.IsNullOrWhiteSpace(_lastRecognizedText))
            {
                _lastRecognizedText = text;
                _lastRecognizedTextTime = DateTime.UtcNow;
                return false;
            }

            bool duplicated = string.Equals(_lastRecognizedText, text, StringComparison.OrdinalIgnoreCase)
                && (DateTime.UtcNow - _lastRecognizedTextTime) < TimeSpan.FromSeconds(2);

            _lastRecognizedText = text;
            _lastRecognizedTextTime = DateTime.UtcNow;
            return duplicated;
        }

        private static SpeechRecognitionEngine CreateRecognizer()
        {
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            if (installed == null || installed.Count == 0)
            {
                throw new InvalidOperationException("未检测到可用的系统语音识别引擎。");
            }

            RecognizerInfo selected =
                installed.FirstOrDefault(r => r.Culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)) ??
                installed.FirstOrDefault(r => r.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) ??
                installed.FirstOrDefault(r => r.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase)) ??
                installed[0];

            VoiceRuntimeLog.Info($"Using recognizer: {selected.Name}, culture={selected.Culture.Name}");
            return new SpeechRecognitionEngine(selected.Culture);
        }

        private void TryAutoAddTaskToQuadrant(TaskDraft draft, float confidence)
        {
            if (!_autoAddVoiceTasks)
                return;

            if (draft == null || string.IsNullOrWhiteSpace(draft.CleanedText))
                return;

            if (confidence < _autoAddMinConfidence)
            {
                VoiceRuntimeLog.Info($"AutoAdd skipped: confidence {confidence:F2} < min {_autoAddMinConfidence:F2}");
                return;
            }

            try
            {
                int quadrantIndex = GetQuadrantIndex(draft);
                if (quadrantIndex < 0 || quadrantIndex > 3)
                {
                    VoiceRuntimeLog.Info($"AutoAdd skipped: invalid quadrant for draft {draft.EstimatedQuadrant}");
                    return;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dataDir = Path.Combine(baseDir, "data");
                Directory.CreateDirectory(dataDir);

                string csvNumber = (quadrantIndex + 1).ToString();
                string csvPath = Path.Combine(dataDir, $"{csvNumber}.csv");

                var items = HelperClass.ReadCsv(csvPath) ?? new List<ItemGrid>();
                var newItem = new ItemGrid
                {
                    Task = draft.CleanedText,
                    Importance = draft.Importance ?? "Unknown",
                    Urgency = draft.Urgency ?? "Unknown",
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    LastModifiedDate = DateTime.Now,
                    ReminderTime = draft.ReminderTime,
                    IsActiveInQuadrant = true,
                    InactiveWarningCount = 0,
                    Result = string.Empty
                };

                items.Insert(0, newItem);
                for (int i = 0; i < items.Count; i++)
                {
                    items[i].Score = items.Count - i;
                }

                HelperClass.WriteCsv(items, csvPath);
                VoiceRuntimeLog.Info($"AutoAdd success: {draft.CleanedText} -> quadrant {csvNumber}");

                var app = Application.Current;
                if (app != null)
                {
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var mainWindow = app.Windows.OfType<MainWindow>().FirstOrDefault();
                            if (mainWindow != null)
                            {
                                mainWindow.loadDataGridView();
                            }
                        }
                        catch (Exception ex)
                        {
                            VoiceRuntimeLog.Error("UI refresh after auto-add failed.", ex);
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("AutoAdd to quadrant failed.", ex);
            }
        }

        private int GetQuadrantIndex(TaskDraft draft)
        {
            if (draft == null)
                return -1;

            string q = draft.EstimatedQuadrant?.Trim();
            if (string.Equals(q, "重要且紧急", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(q, "重要不紧急", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(q, "不重要紧急", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(q, "不重要不紧急", StringComparison.OrdinalIgnoreCase)) return 3;

            if (draft.Importance == "High" && draft.Urgency == "High") return 0;
            if (draft.Importance == "High" && draft.Urgency == "Low") return 1;
            if (draft.Importance == "Low" && draft.Urgency == "High") return 2;
            if (draft.Importance == "Low" && draft.Urgency == "Low") return 3;

            return -1;
        }

        private static bool ReadBoolSetting(string key, bool fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return bool.TryParse(value, out bool parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static float ReadFloatSetting(string key, float fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return float.TryParse(value, out float parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static double ReadDoubleSetting(string key, double fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return double.TryParse(value, out double parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static string ReadStringSetting(string key, string fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            }
            catch
            {
                return fallback;
            }
        }

        private void CaptureSpeechBufferIfNeeded(byte[] buffer, int bytesRecorded)
        {
            if (!_isRecording || buffer == null || bytesRecorded <= 0)
                return;

            lock (_speechBuffer)
            {
                int maxBytes = (int)(16000 * 2 * 20); // ~20s
                int copy = Math.Min(bytesRecorded, maxBytes - _speechBuffer.Count);
                if (copy > 0)
                {
                    for (int i = 0; i < copy; i++) _speechBuffer.Add(buffer[i]);
                }
            }

            if (_funAsrEnabled)
            {
                lock (_asrSegmentBuffer)
                {
                    int maxBytes = (int)(16000 * 2 * 20); // ~20s
                    int copy = Math.Min(bytesRecorded, maxBytes - _asrSegmentBuffer.Count);
                    if (copy > 0)
                    {
                        for (int i = 0; i < copy; i++) _asrSegmentBuffer.Add(buffer[i]);
                    }
                }
            }
        }

        private void EvaluateCompletedSegment(WaveFormat format)
        {
            EvaluateSpeakerSegment(format);
            if (!_speakerVerifyEnabled)
            {
                lock (_speechBuffer)
                {
                    _speechBuffer.Clear();
                }
            }
            if (_funAsrEnabled)
            {
                TryRecognizeSegmentWithFunAsr(format);
            }
        }

        private void EvaluateSpeakerSegment(WaveFormat format)
        {
            if (!_speakerVerifyEnabled)
                return;

            if (format == null || format.SampleRate != 16000 || format.BitsPerSample != 16 || format.Channels != 1)
                return;

            byte[] pcm;
            lock (_speechBuffer)
            {
                pcm = _speechBuffer.ToArray();
                _speechBuffer.Clear();
            }

            if (pcm == null || pcm.Length == 0) return;

            double seconds = pcm.Length / (double)(format.SampleRate * 2);
            if (seconds < _speakerMinSeconds)
            {
                VoiceRuntimeLog.Info($"Speaker segment too short: {seconds:F2}s");
                return;
            }

            if (_speakerEnrollMode)
            {
                _speakerService.Enroll(pcm, format.SampleRate);
                _lastSpeakerVerified = true;
                _lastSpeakerVerifyTime = DateTime.UtcNow;
                VoiceRuntimeLog.Info($"Speaker enrolled/updated. seconds={seconds:F2}");
                return;
            }

            double score = _speakerService.Verify(pcm, format.SampleRate);
            _lastSpeakerVerified = score >= _speakerThreshold;
            _lastSpeakerVerifyTime = DateTime.UtcNow;
            VoiceRuntimeLog.Info($"Speaker verification score={score:F3} pass={_lastSpeakerVerified}");
        }

        private void TryRecognizeSegmentWithFunAsr(WaveFormat format)
        {
            if (!_funAsrEnabled)
                return;

            if (_classicFallbackEnabled)
                return;

            if (_funAsrRepairInProgress)
            {
                VoiceListenerStatusCenter.Publish(VoiceListenerState.Installing, "语音环境修复中，稍后自动恢复");
                return;
            }

            if (format == null || format.SampleRate != 16000 || format.BitsPerSample != 16 || format.Channels != 1)
                return;

            byte[] pcm;
            lock (_asrSegmentBuffer)
            {
                pcm = _asrSegmentBuffer.ToArray();
                _asrSegmentBuffer.Clear();
            }

            if (pcm == null || pcm.Length == 0)
                return;

            double seconds = pcm.Length / (double)(format.SampleRate * 2);
            if (seconds < _funAsrMinSegmentSeconds)
            {
                VoiceRuntimeLog.Info($"FunASR segment too short: {seconds:F2}s");
                return;
            }

            _ = Task.Run(() => RecognizeWithFunAsrAsync(pcm, format.SampleRate, false));
        }

        private async Task RecognizeWithFunAsrAsync(byte[] pcm, int sampleRate, bool isRetryAfterRepair)
        {
            if (!await EnsureFunAsrRuntimeReadyAsync().ConfigureAwait(false))
            {
                HandleFunAsrFailure("语音环境未就绪");
                return;
            }

            string scriptPath = ResolveFunAsrScriptPath();
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                if (!_funAsrScriptMissingLogged)
                {
                    _funAsrScriptMissingLogged = true;
                    VoiceRuntimeLog.Info($"FunASR script not found. config={_funAsrScriptPath}");
                }
                VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "FunASR 脚本缺失");
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "TimeTask", "funasr");
            Directory.CreateDirectory(tempDir);
            string wavPath = Path.Combine(tempDir, $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.wav");

            await _funAsrRecognitionSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                WritePcm16MonoWav(wavPath, pcm, sampleRate);
                if (_funAsrUsePersistentWorker)
                {
                    bool workerReady = await EnsureFunAsrWorkerReadyAsync(scriptPath).ConfigureAwait(false);
                    if (!workerReady)
                    {
                        HandleFunAsrFailure("FunASR 工作进程启动失败");
                        return;
                    }

                    bool wavExists = File.Exists(wavPath);
                    VoiceRuntimeLog.Info($"FunASR worker request: wav={wavPath}, exists={wavExists}, bytes={pcm.Length}");
                    string req = JObject.FromObject(new { wav = wavPath }).ToString(Newtonsoft.Json.Formatting.None);
                    await _funAsrWorkerInput.WriteLineAsync(req).ConfigureAwait(false);
                    await _funAsrWorkerInput.FlushAsync().ConfigureAwait(false);

                    string response = await ReadJsonLineWithTimeoutAsync(
                        _funAsrWorkerOutput,
                        TimeSpan.FromSeconds(Math.Max(5, _funAsrTimeoutSeconds)),
                        "inference").ConfigureAwait(false);
                    if (response == null)
                    {
                        VoiceRuntimeLog.Info($"FunASR worker timeout. timeoutSec={_funAsrTimeoutSeconds}");
                        StopFunAsrWorker();
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音识别超时");
                        HandleFunAsrFailure("FunASR 识别超时");
                        return;
                    }

                    _funAsrFailureCount = 0;
                    _funAsrAutoRepairAttempts = 0;
                    ParseAndHandleFunAsrOutput(response, GetFunAsrWorkerLastErr());
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = _funAsrPythonExe,
                    Arguments = $"\"{scriptPath}\" --wav \"{wavPath}\" --model \"{_funAsrModel}\" --device \"{_funAsrDevice}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";

                using (var process = new Process { StartInfo = psi })
                {
                    process.Start();
                    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                    bool exited = await Task.Run(() => process.WaitForExit(Math.Max(5, _funAsrTimeoutSeconds) * 1000)).ConfigureAwait(false);
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        VoiceRuntimeLog.Info($"FunASR subprocess timeout. timeoutSec={_funAsrTimeoutSeconds}");
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音识别超时");
                        return;
                    }

                    string stdout = await stdoutTask.ConfigureAwait(false);
                    string stderr = await stderrTask.ConfigureAwait(false);

                    if (process.ExitCode != 0)
                    {
                        string failureDetails = BuildFunAsrFailureDetails(process.ExitCode, stdout, stderr);
                        bool missingModule = failureDetails.IndexOf("No module named", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!_funAsrPythonMissingLogged && missingModule)
                        {
                            _funAsrPythonMissingLogged = true;
                            VoiceRuntimeLog.Info("FunASR python dependency missing detected from subprocess output.");
                        }
                        VoiceRuntimeLog.Info(failureDetails);

                        if (missingModule && !isRetryAfterRepair && _funAsrAutoRepairAttempts < 1)
                        {
                            bool repaired = await TryRepairFunAsrRuntimeAsync().ConfigureAwait(false);
                            if (repaired)
                            {
                                _ = Task.Run(() => RecognizeWithFunAsrAsync(pcm, sampleRate, true));
                                return;
                            }
                            TryEnableClassicRecognizerFallback("funasr-missing-module-after-repair");
                        }

                        HandleFunAsrFailure("FunASR 子进程异常");
                        return;
                    }

                    _funAsrFailureCount = 0;
                    _funAsrAutoRepairAttempts = 0;
                    ParseAndHandleFunAsrOutput(stdout, stderr);
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR subprocess recognition failed.", ex);
                HandleFunAsrFailure("FunASR 识别异常");
            }
            finally
            {
                _funAsrRecognitionSemaphore.Release();
                try { File.Delete(wavPath); } catch { }
            }
        }

        private async Task<bool> TryRepairFunAsrRuntimeAsync()
        {
            try
            {
                _funAsrAutoRepairAttempts++;
                _funAsrRepairInProgress = true;
                VoiceListenerStatusCenter.Publish(VoiceListenerState.Installing, "正在修复语音运行环境");
                VoiceRuntimeLog.Info("Trying to repair FunASR runtime automatically.");

                _funAsrBootstrapTask = FunAsrRuntimeManager.ForceRebootstrapAsync();
                var repairedTask = _funAsrBootstrapTask;
                var result = await repairedTask.ConfigureAwait(false);
                if (result != null && result.IsReady && !string.IsNullOrWhiteSpace(result.PythonExe))
                {
                    _funAsrPythonExe = result.PythonExe;
                    VoiceRuntimeLog.Info($"FunASR runtime repaired. python={_funAsrPythonExe}");
                    return true;
                }

                VoiceRuntimeLog.Info($"FunASR runtime repair failed. msg={result?.Message}");
                return false;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR runtime repair attempt failed.", ex);
                return false;
            }
            finally
            {
                _funAsrRepairInProgress = false;
            }
        }

        private async Task<bool> EnsureFunAsrWorkerReadyAsync(string scriptPath)
        {
            try
            {
                if (_funAsrWorkerProcess != null && !_funAsrWorkerProcess.HasExited && _funAsrWorkerInput != null && _funAsrWorkerOutput != null)
                {
                    return true;
                }

                StopFunAsrWorker();
                string workerArgs = $"\"{scriptPath}\" --server --model \"{_funAsrModel}\" --device \"{_funAsrDevice}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = _funAsrPythonExe,
                    Arguments = workerArgs,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";

                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                process.Exited += (s, e) =>
                {
                    VoiceRuntimeLog.Info("FunASR worker exited.");
                };

                process.Start();
                _funAsrWorkerProcess = process;
                _funAsrWorkerInput = process.StandardInput;
                _funAsrWorkerOutput = process.StandardOutput;
                StartFunAsrWorkerStderrPump(process);

                int startupTimeout = Math.Max(_funAsrTimeoutSeconds, _funAsrWorkerStartupTimeoutSeconds);
                string readyLine = await ReadJsonLineWithTimeoutAsync(
                    _funAsrWorkerOutput,
                    TimeSpan.FromSeconds(startupTimeout),
                    "startup").ConfigureAwait(false);
                if (readyLine == null)
                {
                    VoiceRuntimeLog.Info($"FunASR worker startup timeout. timeoutSec={startupTimeout}");
                    StopFunAsrWorker();
                    return false;
                }

                var root = JObject.Parse(readyLine);
                bool ok = (bool?)root["ok"] ?? false;
                string evt = (string)root["event"] ?? string.Empty;
                if (!ok || !string.Equals(evt, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    VoiceRuntimeLog.Info($"FunASR worker not ready. payload={readyLine}");
                    StopFunAsrWorker();
                    return false;
                }

                VoiceRuntimeLog.Info($"FunASR worker started. timeoutSec={_funAsrTimeoutSeconds}");
                return true;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR worker startup failed.", ex);
                StopFunAsrWorker();
                return false;
            }
        }

        private void StartFunAsrWorkerStderrPump(Process process)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    while (process != null && !process.HasExited)
                    {
                        string line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                        if (line == null)
                        {
                            break;
                        }

                        string trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed))
                            continue;

                        lock (_funAsrWorkerErrLock)
                        {
                            _funAsrWorkerLastErr = trimmed;
                        }

                        string lower = trimmed.ToLowerInvariant();
                        if (lower.Contains("error") || lower.Contains("traceback") || lower.Contains("failed"))
                        {
                            VoiceRuntimeLog.Info($"FunASR worker stderr: {trimmed}");
                        }
                    }
                }
                catch
                {
                }
            });
        }

        private async Task<string> ReadLineWithTimeoutAsync(StreamReader reader, TimeSpan timeout)
        {
            if (reader == null)
                return null;

            Task<string> readTask = reader.ReadLineAsync();
            Task delayTask = Task.Delay(timeout);
            Task completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
            if (!ReferenceEquals(completed, readTask))
            {
                return null;
            }

            return await readTask.ConfigureAwait(false);
        }

        private async Task<string> ReadJsonLineWithTimeoutAsync(StreamReader reader, TimeSpan timeout, string phase)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                string line = await ReadLineWithTimeoutAsync(reader, remaining).ConfigureAwait(false);
                if (line == null)
                    return null;

                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (trimmed.StartsWith("{"))
                    return trimmed;

                if ((DateTime.UtcNow - _funAsrWorkerStdoutLogUtc) >= TimeSpan.FromSeconds(3))
                {
                    _funAsrWorkerStdoutLogUtc = DateTime.UtcNow;
                    if (trimmed.Length > 180)
                    {
                        trimmed = trimmed.Substring(0, 180) + "...";
                    }
                    VoiceRuntimeLog.Info($"FunASR worker {phase} stdout(non-json): {trimmed}");
                }
            }
            return null;
        }

        private string GetFunAsrWorkerLastErr()
        {
            lock (_funAsrWorkerErrLock)
            {
                return _funAsrWorkerLastErr;
            }
        }

        private void StopFunAsrWorker()
        {
            try
            {
                if (_funAsrWorkerInput != null)
                {
                    try
                    {
                        _funAsrWorkerInput.WriteLine("{\"cmd\":\"shutdown\"}");
                        _funAsrWorkerInput.Flush();
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                if (_funAsrWorkerProcess != null && !_funAsrWorkerProcess.HasExited)
                {
                    try { _funAsrWorkerProcess.Kill(); } catch { }
                }
            }
            catch { }

            try { _funAsrWorkerInput?.Dispose(); } catch { }
            try { _funAsrWorkerOutput?.Dispose(); } catch { }
            try { _funAsrWorkerProcess?.Dispose(); } catch { }

            _funAsrWorkerInput = null;
            _funAsrWorkerOutput = null;
            _funAsrWorkerProcess = null;
        }

        private void TryWaitFunAsrBootstrap(TimeSpan timeout)
        {
            try
            {
                if (_funAsrBootstrapTask == null)
                    return;

                if (_funAsrBootstrapTask.Wait(timeout))
                {
                    var result = _funAsrBootstrapTask.Result;
                    _funAsrRuntimeReady = result?.IsReady ?? false;
                    if (result != null && !string.IsNullOrWhiteSpace(result.PythonExe))
                    {
                        _funAsrPythonExe = result.PythonExe;
                    }
                    VoiceRuntimeLog.Info($"FunASR bootstrap ready={result?.IsReady}, python={result?.PythonExe}, msg={result?.Message}");
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR bootstrap wait failed.", ex);
            }
        }

        private async Task<bool> EnsureFunAsrRuntimeReadyAsync()
        {
            if (_funAsrBootstrapTask == null)
                return true;

            try
            {
                var result = await _funAsrBootstrapTask.ConfigureAwait(false);
                if (result != null && !string.IsNullOrWhiteSpace(result.PythonExe))
                {
                    _funAsrPythonExe = result.PythonExe;
                }

                _funAsrRuntimeReady = result?.IsReady ?? false;

                if (!_funAsrRuntimeReady && !_classicFallbackEnabled)
                {
                    if (_funAsrOnlyMode)
                    {
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, $"FunASR 未就绪：{result?.Message}");
                    }
                    else
                    {
                        string reason = $"funasr-bootstrap-not-ready:{result?.Message}";
                        bool fallbackReady = TryEnableClassicRecognizerFallback(reason);
                        if (fallbackReady)
                        {
                            VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, "FunASR 不可用，已自动回退本地引擎");
                        }
                    }
                }
                return _funAsrRuntimeReady;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("EnsureFunAsrRuntimeReadyAsync failed.", ex);
                return false;
            }
        }

        private void MarkRecognizingState()
        {
            VoiceListenerStatusCenter.Publish(VoiceListenerState.Recognizing, "语音识别中");

            int epoch;
            lock (_lock)
            {
                _recognizingStateEpoch++;
                epoch = _recognizingStateEpoch;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1200).ConfigureAwait(false);

                    bool stillLatest;
                    lock (_lock)
                    {
                        stillLatest = epoch == _recognizingStateEpoch;
                    }

                    if (_enabled && stillLatest)
                    {
                        if (_funAsrOnlyMode && !_funAsrRuntimeReady && !_classicFallbackEnabled)
                        {
                            VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "FunASR 尚未就绪，语音监听不可用");
                        }
                        else
                        {
                            VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, "语音监听可用");
                        }
                    }
                }
                catch
                {
                }
            });
        }

        private void TouchRecognizingState()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRecognizingTouchUtc) < TimeSpan.FromMilliseconds(350))
                return;

            _lastRecognizingTouchUtc = now;
            MarkRecognizingState();
        }

        private bool IsRecognitionPipelineAvailable()
        {
            if (!_funAsrOnlyMode)
                return true;

            return _funAsrRuntimeReady || _classicFallbackEnabled;
        }

        private void ResetRecognizingStateTimer()
        {
            lock (_lock)
            {
                _recognizingStateEpoch++;
            }
        }

        private bool IsFunAsrBootstrapFailed()
        {
            try
            {
                if (_funAsrBootstrapTask == null || !_funAsrBootstrapTask.IsCompleted)
                    return false;

                var result = _funAsrBootstrapTask.Result;
                return result == null || !result.IsReady;
            }
            catch
            {
                return true;
            }
        }

        private string BuildFunAsrFailureDetails(int exitCode, string stdout, string stderr)
        {
            string cleanStdErr = (stderr ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(cleanStdErr))
            {
                return $"FunASR subprocess failed. code={exitCode}, err={cleanStdErr}";
            }

            string errorFromStdout = ExtractFunAsrErrorFromStdout(stdout);
            if (!string.IsNullOrWhiteSpace(errorFromStdout))
            {
                return $"FunASR subprocess failed. code={exitCode}, out_error={errorFromStdout}";
            }

            string compactStdout = string.IsNullOrWhiteSpace(stdout) ? string.Empty : stdout.Trim();
            if (compactStdout.Length > 240)
            {
                compactStdout = compactStdout.Substring(0, 240) + "...";
            }
            return $"FunASR subprocess failed. code={exitCode}, out={compactStdout}";
        }

        private string ExtractFunAsrErrorFromStdout(string stdout)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(stdout))
                    return string.Empty;

                string jsonLine = stdout
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault(l => l.TrimStart().StartsWith("{"));
                if (string.IsNullOrWhiteSpace(jsonLine))
                    return string.Empty;

                var root = JObject.Parse(jsonLine);
                return (string)root["error"] ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void HandleFunAsrFailure(string userMessage)
        {
            _funAsrFailureCount++;
            _funAsrRuntimeReady = false;

            if (_classicFallbackEnabled)
            {
                VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, "已回退系统语音引擎，监听可用");
                return;
            }

            if (!_funAsrOnlyMode && _funAsrFailureCount >= 3)
            {
                bool fallbackReady = TryEnableClassicRecognizerFallback("funasr-consecutive-failures");
                if (fallbackReady)
                {
                    VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, "FunASR 不可用，已回退系统语音引擎");
                    return;
                }
            }

            VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, userMessage);
        }

        private bool TryEnableClassicRecognizerFallback(string reason)
        {
            if (_funAsrOnlyMode)
            {
                VoiceRuntimeLog.Info($"Classic recognizer fallback blocked in funasr-only mode. reason={reason}");
                return false;
            }

            lock (_lock)
            {
                if (_classicFallbackEnabled)
                    return true;

                try
                {
                    bool anyReady = false;

                    // 回退优先 Vosk，System.Speech 仅兜底，避免部分系统引擎在中文场景不稳定。
                    if (!_voskEnabled)
                    {
                        TryWaitModelBootstrap();
                        bool voskReady = TryInitializeVoskRecognizer();
                        if (voskReady)
                        {
                            anyReady = true;
                            VoiceRuntimeLog.Info($"Classic fallback using Vosk, reason={reason}");
                        }
                        else
                        {
                            HookLateVoskInitialization();
                        }
                    }

                    if (!anyReady && _recognizer == null)
                    {
                        InitializeSystemSpeechRecognizer();
                    }

                    anyReady = anyReady || _voskEnabled || _recognizer != null;
                    _classicFallbackEnabled = anyReady;
                    VoiceRuntimeLog.Info($"Classic recognizer fallback enabled={_classicFallbackEnabled}, reason={reason}");
                    return _classicFallbackEnabled;
                }
                catch (Exception ex)
                {
                    VoiceRuntimeLog.Error($"Classic recognizer fallback failed. reason={reason}", ex);
                    return false;
                }
            }
        }

        private void ParseAndHandleFunAsrOutput(string stdout, string stderr)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(stdout))
                {
                    VoiceRuntimeLog.Info($"FunASR output empty. stderr={stderr}");
                    HandleFunAsrFailure("语音识别结果为空");
                    return;
                }

                string jsonLine = stdout
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault(l => l.TrimStart().StartsWith("{"));
                if (string.IsNullOrWhiteSpace(jsonLine))
                {
                    VoiceRuntimeLog.Info($"FunASR output parse skipped. stdout={stdout}");
                    HandleFunAsrFailure("语音识别结果解析失败");
                    return;
                }

                var root = JObject.Parse(jsonLine);
                bool ok = (bool?)root["ok"] ?? false;
                if (!ok)
                {
                    string error = (string)root["error"] ?? stderr ?? "unknown";
                    VoiceRuntimeLog.Info($"FunASR returned not-ok. error={error}");

                    // 单次推理失败不应立即把监听状态打成不可用，允许用户继续说下一句。
                    _funAsrFailureCount++;
                    bool severe = error.IndexOf("import funasr failed", StringComparison.OrdinalIgnoreCase) >= 0
                        || error.IndexOf("No module named", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (severe)
                    {
                        HandleFunAsrFailure("FunASR 运行环境异常");
                        return;
                    }

                    if (_funAsrFailureCount >= 3)
                    {
                        HandleFunAsrFailure("FunASR 连续识别失败");
                        return;
                    }

                    VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, "语音监听可用（本次未识别，请重试）");
                    return;
                }

                string text = (string)root["text"] ?? string.Empty;
                float confidence = ClampConfidence((float?)root["confidence"] ?? 0.6f);
                if (string.IsNullOrWhiteSpace(text))
                {
                    HandleFunAsrFailure("语音识别文本为空");
                    return;
                }

                _funAsrFailureCount = 0;
                _funAsrRuntimeReady = true;
                HandleRecognizedText(text, confidence, "funasr");
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR output parse failed.", ex);
                HandleFunAsrFailure("FunASR 结果解析异常");
            }
        }

        private void StartFunAsrBootstrapMonitor()
        {
            if (_funAsrBootstrapTask == null || _funAsrBootstrapMonitorStarted)
                return;

            _funAsrBootstrapMonitorStarted = true;
            VoiceRuntimeLog.Info("FunASR bootstrap monitor started.");
            ObserveFunAsrBootstrapTask(_funAsrBootstrapTask, "initial");
        }

        private void ObserveFunAsrBootstrapTask(Task<FunAsrRuntimeBootstrapResult> task, string source)
        {
            if (task == null)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await task.ConfigureAwait(false);
                    _funAsrRuntimeReady = result?.IsReady ?? false;

                    if (result != null && !string.IsNullOrWhiteSpace(result.PythonExe))
                    {
                        _funAsrPythonExe = result.PythonExe;
                    }

                    VoiceRuntimeLog.Info($"FunASR bootstrap monitor completed. source={source}, ready={result?.IsReady}, msg={result?.Message}");

                    if (!_enabled)
                        return;

                    if (_funAsrRuntimeReady)
                    {
                        CancelFunAsrRetry();
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, "FunASR 就绪，语音监听可用");
                    }
                    else
                    {
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, $"FunASR 未就绪：{result?.Message}");
                        TryScheduleFunAsrRetry(result?.Message);
                    }
                }
                catch (Exception ex)
                {
                    VoiceRuntimeLog.Error("FunASR bootstrap monitor failed.", ex);
                    if (_enabled)
                    {
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "FunASR 初始化失败");
                    }
                }
            });
        }

        private void TryScheduleFunAsrRetry(string message)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(message))
                return;

            int remainingSeconds = ParseRetryAfterSeconds(message);
            if (remainingSeconds <= 0)
                return;

            int retryPollSeconds = Math.Max(15, ReadIntSetting("FunAsrRetryPollSeconds", 60));
            int waitSeconds = Math.Min(remainingSeconds, retryPollSeconds);

            lock (_lock)
            {
                if (_funAsrRetryCts != null)
                    return;

                _funAsrRetryCts = new CancellationTokenSource();
            }

            VoiceRuntimeLog.Info($"FunASR retry scheduled: remainingSeconds={remainingSeconds}, nextCheckSeconds={waitSeconds}, reason={message}");
            VoiceListenerStatusCenter.Publish(
                VoiceListenerState.Unavailable,
                $"FunASR 冷却中（剩余约 {FormatDurationZh(remainingSeconds)}），将在 {waitSeconds} 秒后自动重试");

            var cts = _funAsrRetryCts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Math.Max(1, waitSeconds) * 1000, cts.Token).ConfigureAwait(false);
                    if (!_enabled || cts.IsCancellationRequested)
                        return;

                    VoiceListenerStatusCenter.Publish(VoiceListenerState.Loading, "FunASR 自动重试中");
                    VoiceRuntimeLog.Info("FunASR auto retry trigger: calling ForceRebootstrapAsync.");
                    _funAsrBootstrapTask = FunAsrRuntimeManager.ForceRebootstrapAsync();
                    ObserveFunAsrBootstrapTask(_funAsrBootstrapTask, "auto-retry");
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    VoiceRuntimeLog.Error("FunASR auto retry failed.", ex);
                }
                finally
                {
                    lock (_lock)
                    {
                        if (ReferenceEquals(_funAsrRetryCts, cts))
                        {
                            _funAsrRetryCts.Dispose();
                            _funAsrRetryCts = null;
                        }
                    }
                }
            });
        }

        private static int ParseRetryAfterSeconds(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return 0;

            const string token = "retry-after-sec=";
            int idx = message.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return 0;

            int start = idx + token.Length;
            int end = start;
            while (end < message.Length && char.IsDigit(message[end]))
            {
                end++;
            }

            if (end <= start)
                return 0;

            string number = message.Substring(start, end - start);
            return int.TryParse(number, out int sec) ? sec : 0;
        }

        private static string FormatDurationZh(int totalSeconds)
        {
            if (totalSeconds <= 0)
                return "0秒";

            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            if (minutes <= 0)
                return $"{seconds}秒";
            if (seconds == 0)
                return $"{minutes}分";
            return $"{minutes}分{seconds}秒";
        }

        private void CancelFunAsrRetry()
        {
            lock (_lock)
            {
                if (_funAsrRetryCts == null)
                    return;

                try { _funAsrRetryCts.Cancel(); } catch { }
                try { _funAsrRetryCts.Dispose(); } catch { }
                _funAsrRetryCts = null;
            }
        }

        private string ResolveFunAsrScriptPath()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_funAsrScriptPath))
                    return null;

                if (Path.IsPathRooted(_funAsrScriptPath) && File.Exists(_funAsrScriptPath))
                    return _funAsrScriptPath;

                string baseDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _funAsrScriptPath);
                if (File.Exists(baseDirPath))
                    return baseDirPath;

                string currentDirPath = Path.Combine(Environment.CurrentDirectory, _funAsrScriptPath);
                if (File.Exists(currentDirPath))
                    return currentDirPath;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static void WritePcm16MonoWav(string path, byte[] pcmData, int sampleRate)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                int subChunk2Size = pcmData?.Length ?? 0;
                int byteRate = sampleRate * 2;
                short blockAlign = 2;
                short bitsPerSample = 16;
                int chunkSize = 36 + subChunk2Size;

                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(chunkSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)1);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write(bitsPerSample);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(subChunk2Size);
                if (subChunk2Size > 0)
                {
                    bw.Write(pcmData);
                }
            }
        }

        private bool IsSpeakerVerified()
        {
            if (!_speakerVerifyEnabled)
                return true;

            if (_speakerEnrollMode)
                return true;

            if ((DateTime.UtcNow - _lastSpeakerVerifyTime) > TimeSpan.FromSeconds(5))
                return false;

            return _lastSpeakerVerified;
        }

        private static int ReadIntSetting(string key, int fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return int.TryParse(value, out int parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private void EnqueueLlmQuadrant(TaskDraft draft, float confidence)
        {
            _ = Task.Run(() => EnhanceDraftWithLlmAsync(draft, confidence));
        }

        private async Task EnhanceDraftWithLlmAsync(TaskDraft draft, float confidence)
        {
            if (draft == null || string.IsNullOrWhiteSpace(draft.CleanedText))
                return;

            await _llmSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var llm = GetLlmService();
                VoiceRuntimeLog.Info($"LLM quadrant requested for: {draft.CleanedText}");

                var (importance, urgency) = await llm.GetTaskPriorityAsync(draft.CleanedText).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(importance) || importance == "Unknown")
                    importance = draft.Importance;
                if (string.IsNullOrWhiteSpace(urgency) || urgency == "Unknown")
                    urgency = draft.Urgency;

                draft.Importance = importance;
                draft.Urgency = urgency;
                draft.EstimatedQuadrant = _intentRecognizer.EstimateQuadrant(importance, urgency);

                _draftManager.UpdateDraft(draft);

                VoiceRuntimeLog.Info($"LLM quadrant result: {draft.CleanedText} -> {draft.EstimatedQuadrant} (I={importance}, U={urgency})");
                if (!_requireDraftConfirmation)
                {
                    TryAutoAddTaskToQuadrant(draft, confidence);
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("LLM quadrant failed.", ex);
                if (!_requireDraftConfirmation)
                {
                    TryAutoAddTaskToQuadrant(draft, confidence);
                }
            }
            finally
            {
                _llmSemaphore.Release();
            }
        }

        private void BufferConversation(string text)
        {
            if (!_conversationExtractEnabled)
                return;

            if (string.IsNullOrWhiteSpace(text))
                return;

            var now = DateTime.UtcNow;
            _conversationBuffer.Add((now, text));

            // keep only recent
            _conversationBuffer.RemoveAll(x => now - x.ts > _conversationWindow);
        }

        private void TryExtractConversationTasksAsync()
        {
            if (!_conversationExtractEnabled)
                return;

            var now = DateTime.UtcNow;
            if (now - _lastConversationExtract < TimeSpan.FromSeconds(20))
                return;

            if (_conversationBuffer.Count < _conversationMinTurns)
                return;

            // Heuristic: presence of “你/他说/她说/我们” indicates conversation context
            string combined = string.Join("。", _conversationBuffer.Select(x => x.text));
            if (!Regex.IsMatch(combined, @"(你说|他说|她说|我们|他们|对方|聊|讨论|商量)"))
                return;

            _lastConversationExtract = now;
            _ = Task.Run(() => ExtractTasksFromConversationAsync(combined));
        }

        private async Task ExtractTasksFromConversationAsync(string conversation)
        {
            try
            {
                var llm = GetLlmService();
                var tasks = await llm.ExtractTasksFromConversationAsync(conversation).ConfigureAwait(false);
                if (tasks == null || tasks.Count == 0)
                    return;

                foreach (var task in tasks)
                {
                    if (string.IsNullOrWhiteSpace(task))
                        continue;
                    var draft = ProcessPotentialTask(task, 0.6f);
                    if (draft != null)
                    {
                        if (_useLlmForVoiceQuadrant)
                        {
                            EnqueueLlmQuadrant(draft, 0.6f);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Conversation task extraction failed.", ex);
            }
        }

        private LlmService GetLlmService()
        {
            if (_llmService != null)
                return _llmService;

            lock (_lock)
            {
                if (_llmService == null)
                {
                    _llmService = new LlmService();
                }
            }
            return _llmService;
        }

        private SpeechCandidate SelectBestCandidate(SpeechRecognizedEventArgs e)
        {
            if (e?.Result == null)
                return null;

            var candidates = new System.Collections.Generic.List<SpeechCandidate>
            {
                new SpeechCandidate(e.Result.Text, e.Result.Confidence)
            };

            if (e.Result.Alternates != null)
            {
                foreach (RecognizedPhrase alt in e.Result.Alternates)
                {
                    if (alt?.Text == null) continue;
                    candidates.Add(new SpeechCandidate(alt.Text, alt.Confidence));
                }
            }

            SpeechCandidate best = null;
            double bestScore = double.NegativeInfinity;
            double bestTaskLikelihood = 0;
            bool relaxedForSystemFallback = _classicFallbackEnabled;

            foreach (var candidate in candidates
                .GroupBy(c => c.Text, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Confidence).First()))
            {
                string normalizedText = CleanRecognizedText(candidate.Text);
                if (string.IsNullOrWhiteSpace(normalizedText))
                    continue;

                double taskLikelihood = _intentRecognizer.ScoreTaskLikelihood(normalizedText);
                double hintScore = ScoreHintMatch(normalizedText);
                double combinedScore = relaxedForSystemFallback
                    ? taskLikelihood * 0.62 + candidate.Confidence * 0.23 + hintScore * 0.15
                    : taskLikelihood * 0.4 + candidate.Confidence * 0.45 + hintScore * 0.15;

                if (combinedScore > bestScore)
                {
                    bestScore = combinedScore;
                    best = new SpeechCandidate(normalizedText, candidate.Confidence);
                    bestTaskLikelihood = taskLikelihood;
                }
            }

            if (best == null)
                return null;

            if (relaxedForSystemFallback)
            {
                if (bestScore < 0.18 && bestTaskLikelihood < 0.60)
                    return null;

                if (best.Confidence < Math.Max(0.03f, _confidenceThreshold * 0.08f) && bestTaskLikelihood < 0.65)
                    return null;
            }
            else
            {
                if (bestScore < 0.35 || best.Confidence < _confidenceThreshold * 0.65f)
                    return null;
            }

            VoiceRuntimeLog.Info($"System.Speech selected: text={best.Text}, conf={best.Confidence:F2}, score={bestScore:F2}, taskLike={bestTaskLikelihood:F2}");

            return best;
        }

        private sealed class SpeechCandidate
        {
            public string Text { get; }
            public float Confidence { get; }

            public SpeechCandidate(string text, float confidence)
            {
                Text = text;
                Confidence = confidence;
            }
        }

        /// <summary>
        /// 获取草稿管理器实例
        /// </summary>
        public TaskDraftManager GetDraftManager() => _draftManager;

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public (int totalDetections, int potentialTasks, DateTime? lastDetection) GetStats()
        {
            return (TotalSpeechDetections, TotalPotentialTasks, LastDetectionTime);
        }

        public void Dispose()
        {
            Stop();
            try { _funAsrRecognitionSemaphore?.Dispose(); } catch { }
            try { _llmSemaphore?.Dispose(); } catch { }
            _draftManager?.Dispose();
        }
    }
}
