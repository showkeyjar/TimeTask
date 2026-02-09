using System;
using System.IO;
using System.Speech.Recognition;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using NAudio.Wave;
using NAudio.CoreAudioApi;

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

        private SpeechRecognitionEngine _recognizer;
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

                // 初始化语音识别
                try
                {
                    _recognizer = new SpeechRecognitionEngine();
                    _recognizer.SetInputToDefaultAudioDevice();
                    _recognizer.LoadGrammar(new DictationGrammar());

                    // 优化识别器设置
                    TryUpdateRecognizerSetting("BabbleTimeout", TimeSpan.FromSeconds(3));
                    TryUpdateRecognizerSetting("InitialSilenceTimeout", TimeSpan.FromSeconds(6));

                    _recognizer.SpeechRecognized += OnSpeechRecognized;
                    _recognizer.SpeechHypothesized += OnSpeechHypothesized;
                    _recognizer.AudioStateChanged += OnAudioStateChanged;

                    _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                    Console.WriteLine("[EnhancedAudioCaptureService] Speech recognition started.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnhancedAudioCaptureService] Failed to start speech recognition: {ex.Message}");
                }

                // 启动静音检测定时器
                _silenceTimer = new System.Timers.Timer(1000);
                _silenceTimer.Elapsed += OnSilenceTick;
                _silenceTimer.AutoReset = true;
                _silenceTimer.Start();

                // 启动音频采集
                StartAudioCapture();

                Console.WriteLine("[EnhancedAudioCaptureService] Started successfully.");
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

                StopAudioCapture();

                Console.WriteLine("[EnhancedAudioCaptureService] Stopped.");
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
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"[EnhancedAudioCaptureService] WASAPI fallback failed: {ex2.Message}");
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

            TotalSpeechDetections++;
            LastDetectionTime = DateTime.Now;

            var confidence = e.Result.Confidence;
            var text = e.Result.Text;

            // 清理识别结果
            text = CleanRecognizedText(text);

            if (string.IsNullOrWhiteSpace(text) || text.Length < 3 || confidence < _confidenceThreshold)
                return;

            _lastSpeechTime = DateTime.UtcNow;

            // 意图识别
            if (_intentRecognizer.IsPotentialTask(text))
            {
                TotalPotentialTasks++;
                ProcessPotentialTask(text, confidence);
            }
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

            return text;
        }

        private void ProcessPotentialTask(string text, float confidence)
        {
            try
            {
                // 提取任务描述
                string cleanedText = _intentRecognizer.ExtractTaskDescription(text);
                if (string.IsNullOrWhiteSpace(cleanedText))
                    return;

                // 估计优先级
                var (importance, urgency) = _intentRecognizer.EstimatePriority(text);

                // 估计象限
                string quadrant = EstimateQuadrant(importance, urgency);

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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnhancedAudioCaptureService] Error processing potential task: {ex.Message}");
            }
        }

        private string EstimateQuadrant(string importance, string urgency)
        {
            if (importance == "High" && urgency == "High")
                return "重要且紧急";
            else if (importance == "High" && urgency == "Low")
                return "重要不紧急";
            else if (importance == "Low" && urgency == "High")
                return "不重要紧急";
            else
                return "不重要不紧急";
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
