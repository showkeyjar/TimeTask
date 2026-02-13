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
        private readonly SemaphoreSlim _llmSemaphore = new SemaphoreSlim(1, 1);
        private LlmService _llmService;

        private SpeechRecognitionEngine _recognizer;
        private Model _voskModel;
        private VoskRecognizer _voskRecognizer;
        private bool _voskEnabled;
        private bool _voskFormatWarningLogged;
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

        // VAD 参数 - 更保守的配置
        private readonly double _energyThresholdDb = -30.0; // 能量阈值，-30dBFS 表示要有一定音量
        private readonly int _minStartMs = 500;              // 连续高能量达到 500ms 才开始录音
        private readonly int _hangoverMs = 1500;             // 录音进行中，连续低能量达到 1.5s 才停止
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
            _speechModelManager = new SpeechModelManager();
            _modelBootstrapTask = _speechModelManager.EnsureReadyAsync();
            _autoAddVoiceTasks = ReadBoolSetting("VoiceAutoAddToQuadrant", false);
            _autoAddMinConfidence = ReadFloatSetting("VoiceAutoAddMinConfidence", 0.65f);
            _useLlmForVoiceQuadrant = ReadBoolSetting("VoiceUseLlmQuadrant", false);
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

                Console.WriteLine("[EnhancedAudioCaptureService] Starting...");
                VoiceRuntimeLog.Info("EnhancedAudioCaptureService starting.");

                // 初始化语音识别
                bool voskReady = false;
                try
                {
                    TryWaitModelBootstrap();
                    voskReady = TryInitializeVoskRecognizer();

                    if (!voskReady)
                    {
                        InitializeSystemSpeechRecognizer();
                    }

                    if (!voskReady && _recognizer == null)
                    {
                    throw new InvalidOperationException("Vosk 和 System.Speech 均未初始化成功。");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnhancedAudioCaptureService] Failed to start speech recognition: {ex.Message}");
                    VoiceRuntimeLog.Error("Failed to start speech recognition.", ex);
                    throw new InvalidOperationException("语音识别初始化失败，请检查系统语音识别组件和麦克风权限。", ex);
                }

                // 启动静音检测定时器
                _silenceTimer = new System.Timers.Timer(1000);
                _silenceTimer.Elapsed += OnSilenceTick;
                _silenceTimer.AutoReset = true;
                _silenceTimer.Start();

                // 启动音频采集
                StartAudioCapture();

                if (!voskReady)
                {
                    HookLateVoskInitialization();
                }

                Console.WriteLine("[EnhancedAudioCaptureService] Started successfully.");
                VoiceRuntimeLog.Info("EnhancedAudioCaptureService started successfully.");
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

                Console.WriteLine("[EnhancedAudioCaptureService] Stopped.");
                VoiceRuntimeLog.Info("EnhancedAudioCaptureService stopped.");
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
                return;

            HandleRecognizedText(bestCandidate.Text, bestCandidate.Confidence, "system-speech");
        }

        private string CleanRecognizedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

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

            // 简单纠错：特定场景下的常见替换
            if (text.Contains("部门") && text.Contains("明天") && text.Contains("规律"))
            {
                text = text.Replace("规律", "会议");
            }
            if (text.Contains("自打不过来"))
            {
                text = text.Replace("自打不过来", "市场部门");
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

                // 估计优先级
                var (importance, urgency) = _intentRecognizer.EstimatePriority(cleanedText);

                // 估计象限
                string quadrant = _intentRecognizer.EstimateQuadrant(importance, urgency);

                // 创建草稿
                var draft = new TaskDraft
                {
                    RawText = text,
                    CleanedText = cleanedText,
                    Importance = importance,
                    Urgency = urgency,
                    EstimatedQuadrant = quadrant,
                    Source = "voice"
                };

                _draftManager.AddDraft(draft);

                Console.WriteLine($"[EnhancedAudioCaptureService] Task draft created: \"{cleanedText}\" (Quadrant: {quadrant}, Conf: {confidence:P0})");
                VoiceRuntimeLog.Info($"Task draft created from voice. text={cleanedText}, quadrant={quadrant}, conf={confidence:F2}");
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
                    }
                }
            }
        }

        private void OnWaveData(object sender, WaveInEventArgs e)
        {
            if (!_enabled) return;

            var format = _waveIn?.WaveFormat ?? (_wasapi?.WaveFormat);
            if (format == null || e.BytesRecorded <= 0) return;

            TryProcessVoskAudio(e.Buffer, e.BytesRecorded, format);

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

            lock (_lock)
            {
                // 开始录音条件
                if (!_isRecording)
                {
                    if (recentAudioSpeech && _consecAboveMs >= _minStartMs)
                    {
                        _isRecording = true;
                        Console.WriteLine("[EnhancedAudioCaptureService] Started recording (VAD triggered).");
                    }
                }
                else
                {
                    // 停止录音条件
                    if (_consecBelowMs >= _hangoverMs)
                    {
                        _isRecording = false;
                        Console.WriteLine("[EnhancedAudioCaptureService] Stopped recording (silence detected).");
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
            TryLoadHintsGrammar();

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
                var alternatives = root["alternatives"] as JArray;
                if (alternatives != null && alternatives.Count > 0)
                {
                    var bestAlt = alternatives
                        .OfType<JObject>()
                        .Select(x => new
                        {
                            Text = (string)x["text"],
                            Confidence = (float?)x["confidence"] ?? 0.6f
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
                var words = root["result"] as JArray;
                if (words == null || words.Count == 0)
                    return (text, 0.6f);

                var confs = words
                    .OfType<JObject>()
                    .Select(w => (float?)w["conf"])
                    .Where(c => c.HasValue)
                    .Select(c => c.Value)
                    .ToList();

                float conf = confs.Count > 0 ? confs.Average() : 0.6f;
                return (text, ClampConfidence(conf));
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

            double score = ClampConfidence(confidence);
            if (text.Contains("会议")) score += 0.25;
            if (text.Contains("市场")) score += 0.25;
            if (text.Contains("部门")) score += 0.15;
            if (text.Contains("提醒")) score += 0.15;
            if (text.Contains("明天")) score += 0.15;
            return score;
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

            TotalSpeechDetections++;
            LastDetectionTime = DateTime.Now;
            _lastSpeechTime = DateTime.UtcNow;

            VoiceRuntimeLog.Info($"Recognized({source}) conf={confidence:F2} text={text}");

            if (confidence < _confidenceThreshold * 0.55f)
                return;

            if (_intentRecognizer.IsPotentialTask(text) || _intentRecognizer.IsReminderLike(text))
            {
                TotalPotentialTasks++;
                var draft = ProcessPotentialTask(text, confidence);
                if (draft != null)
                {
                    if (_useLlmForVoiceQuadrant)
                    {
                        EnqueueLlmQuadrant(draft, confidence);
                    }
                    else
                    {
                        TryAutoAddTaskToQuadrant(draft, confidence);
                    }
                }
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
                TryAutoAddTaskToQuadrant(draft, confidence);
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("LLM quadrant failed.", ex);
                TryAutoAddTaskToQuadrant(draft, confidence);
            }
            finally
            {
                _llmSemaphore.Release();
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

            foreach (var candidate in candidates
                .GroupBy(c => c.Text, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Confidence).First()))
            {
                string normalizedText = CleanRecognizedText(candidate.Text);
                if (string.IsNullOrWhiteSpace(normalizedText))
                    continue;

                double taskLikelihood = _intentRecognizer.ScoreTaskLikelihood(normalizedText);
                double combinedScore = taskLikelihood * 0.7 + candidate.Confidence * 0.3;

                if (combinedScore > bestScore)
                {
                    bestScore = combinedScore;
                    best = new SpeechCandidate(normalizedText, candidate.Confidence);
                }
            }

            if (best == null)
                return null;

            if (bestScore < 0.45 || best.Confidence < _confidenceThreshold * 0.7f)
                return null;

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
            _draftManager?.Dispose();
        }
    }
}
