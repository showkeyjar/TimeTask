using System;
using System.IO;
using System.Speech.Recognition;
using System.Timers;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace TimeTask
{
    /// <summary>
    /// 后台智能人声录音服务：
    /// - 持续监听麦克风。
    /// - 当识别到“有意义的句子”（语音识别置信度阈值）时开始录音。
    /// - 当超过指定静默时长（默认30秒）未检测到人声讲话时停止录音。
    /// - 音频保存为 WAV 文件到 Recordings/ 目录。
    /// </summary>
    public sealed class AudioCaptureService : IDisposable
    {
        private readonly object _lock = new object();
        private readonly TimeSpan _silenceTimeout;
        private readonly float _confidenceThreshold;

        private SpeechRecognitionEngine _recognizer;
        private DateTime _lastSpeechTime = DateTime.MinValue;
        private Timer _silenceTimer;

        private WaveInEvent _waveIn;
        private WasapiCapture _wasapi;
        private WaveFileWriter _writer;
        private string _currentFilePath;

        private bool _enabled;

        // 简单 VAD 与去抖动参数
        private readonly double _energyThresholdDb = -35.0; // 能量阈值（dBFS），越高越“严格”
        private readonly int _minStartMs = 400;              // 连续高能量达到此时长才开始录音
        private readonly int _hangoverMs = 1200;             // 录音进行中，连续低能量达到此时长才停止
        private int _consecAboveMs = 0;
        private int _consecBelowMs = 0;
        private DateTime _lastAudioStateSpeech = DateTime.MinValue;

        public AudioCaptureService(TimeSpan? silenceTimeout = null, float confidenceThreshold = 0.4f)
        {
            _silenceTimeout = silenceTimeout ?? TimeSpan.FromSeconds(30);
            _confidenceThreshold = confidenceThreshold;
        }

        /// <summary>
        /// 启动持续监听。
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_enabled) return;
                _enabled = true;

                // 初始化语音识别（使用系统离线识别器 + 自由听写语法）
                _recognizer = new SpeechRecognitionEngine();
                _recognizer.SetInputToDefaultAudioDevice();
                _recognizer.LoadGrammar(new DictationGrammar());

                // 可选：适当放宽杂音/静音设置，提升连贯监听体验
                TryUpdateRecognizerSetting("BabbleTimeout",  TimeSpan.FromSeconds(2));
                TryUpdateRecognizerSetting("InitialSilenceTimeout", TimeSpan.FromSeconds(5));

                _recognizer.SpeechRecognized += OnSpeechRecognized;
                _recognizer.SpeechHypothesized += OnSpeechHypothesized;
                _recognizer.AudioStateChanged += OnAudioStateChanged;

                _recognizer.RecognizeAsync(RecognizeMode.Multiple);

                _silenceTimer = new Timer(1000);
                _silenceTimer.Elapsed += OnSilenceTick;
                _silenceTimer.AutoReset = true;
                _silenceTimer.Start();
            }
        }

        /// <summary>
        /// 停止监听并清理资源。
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _enabled = false;
                try { _silenceTimer?.Stop(); } catch { }
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
                StopRecordingInternal();
                _silenceTimer?.Dispose();
                _silenceTimer = null;
            }
        }

        private void OnSpeechHypothesized(object sender, SpeechHypothesizedEventArgs e)
        {
            // 任何假设事件都视为存在讲话，刷新说话时间（避免短暂停顿导致过早停止）
            _lastSpeechTime = DateTime.UtcNow;
        }

        private void OnAudioStateChanged(object sender, AudioStateChangedEventArgs e)
        {
            if (e.AudioState == AudioState.Speech)
            {
                // 仅记录最近的语音状态时间，用于与能量判定共同决定是否开录
                _lastAudioStateSpeech = DateTime.UtcNow;
            }
        }

        private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            if (e == null || e.Result == null) return;
            var confidence = e.Result.Confidence;
            var text = e.Result.Text;

            // 认为是“有意义的句子”的简单规则：长度>=3，且置信度达到阈值
            if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 3 && confidence >= _confidenceThreshold)
            {
                _lastSpeechTime = DateTime.UtcNow;
                // 确保录音已启动
                EnsureRecording();
            }
        }

        private void OnSilenceTick(object sender, ElapsedEventArgs e)
        {
            if (!_enabled) return;

            var now = DateTime.UtcNow;
            // 超过静默阈值则停止录音
            if (_writer != null && now - _lastSpeechTime > _silenceTimeout)
            {
                lock (_lock)
                {
                    if (_writer != null && now - _lastSpeechTime > _silenceTimeout)
                    {
                        StopRecordingInternal();
                    }
                }
            }
        }

        private void EnsureRecording()
        {
            lock (_lock)
            {
                if (_writer != null) return;

                Directory.CreateDirectory(GetRecordingDir());
                _currentFilePath = Path.Combine(GetRecordingDir(),
                    $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

                // 首选 MME (WaveIn)，采样率 44100Hz 单声道 16bit，兼容性更好
                try
                {
                    _waveIn = new WaveInEvent
                    {
                        WaveFormat = new WaveFormat(44100, 16, 1),
                        BufferMilliseconds = 200
                    };
                    _waveIn.DataAvailable += OnWaveData;
                    _waveIn.RecordingStopped += OnRecordingStopped;

                    _writer = new WaveFileWriter(_currentFilePath, _waveIn.WaveFormat);
                    _waveIn.StartRecording();
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WaveIn start failed, fallback to WASAPI: {ex.Message}");
                    try
                    {
                        if (_waveIn != null)
                        {
                            try { _waveIn.DataAvailable -= OnWaveData; } catch { }
                            try { _waveIn.RecordingStopped -= OnRecordingStopped; } catch { }
                            _waveIn.Dispose();
                        }
                    }
                    catch { }
                    _waveIn = null;
                }

                // 回退：WASAPI 共享模式，使用系统默认录音设备
                try
                {
                    var enumerator = new MMDeviceEnumerator();
                    var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                    _wasapi = new WasapiCapture(device);
                    _wasapi.ShareMode = AudioClientShareMode.Shared;
                    _wasapi.DataAvailable += OnWaveData;
                    _wasapi.RecordingStopped += OnRecordingStopped;

                    _writer = new WaveFileWriter(_currentFilePath, _wasapi.WaveFormat);
                    _wasapi.StartRecording();
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"WASAPI start failed: {ex2}");
                    // 启动失败，清理写入器
                    if (_writer != null) { try { _writer.Dispose(); } catch { } _writer = null; }
                    _currentFilePath = null;
                }
            }
        }

        private void StopRecordingInternal()
        {
            try
            {
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

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            lock (_lock)
            {
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

                if (_writer != null)
                {
                    try { _writer.Flush(); } catch { }
                    try { _writer.Dispose(); } catch { }
                    _writer = null;
                }

                _currentFilePath = null;
            }
        }

        private void OnWaveData(object sender, WaveInEventArgs e)
        {
            try
            {
                ProcessAudioBuffer(e.Buffer, e.BytesRecorded, (_waveIn != null) ? _waveIn.WaveFormat : (_wasapi != null ? _wasapi.WaveFormat : null));
            }
            catch
            {
                StopRecordingInternal();
            }
        }

        private void ProcessAudioBuffer(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            if (format == null || bytesRecorded <= 0) return;

            // 计算帧时长（毫秒）
            int bytesPerMs = (format.AverageBytesPerSecond / 1000);
            int frameMs = Math.Max(1, bytesRecorded / Math.Max(1, bytesPerMs));

            // 计算当前缓冲的 dBFS
            double db = ComputeDbfs(buffer, bytesRecorded, format);

            bool above = db >= _energyThresholdDb;
            if (above)
            {
                _consecAboveMs += frameMs;
                _consecBelowMs = 0;
                _lastSpeechTime = DateTime.UtcNow; // 只要高能量即刷新“最后讲话时间”
            }
            else
            {
                _consecBelowMs += frameMs;
                _consecAboveMs = 0;
            }

            bool recentAudioSpeech = (DateTime.UtcNow - _lastAudioStateSpeech) < TimeSpan.FromSeconds(1.0);

            // 开始录音条件：最近识别到语音状态 且 高能量持续达到阈值 或者 识别结果强置信度文本（由 OnSpeechRecognized 触发 EnsureRecording）
            if (_writer == null)
            {
                if (recentAudioSpeech && _consecAboveMs >= _minStartMs)
                {
                    EnsureRecording();
                }
            }
            else
            {
                // 停止录音条件：持续低能量达到挂起时间（更快停录），或由 30s 静默定时器兜底
                if (_consecBelowMs >= _hangoverMs)
                {
                    StopRecordingInternal();
                }
            }

            // 若正在录音，写入缓冲
            if (_writer != null)
            {
                try
                {
                    _writer.Write(buffer, 0, bytesRecorded);
                }
                catch { StopRecordingInternal(); }
            }
        }

        private static double ComputeDbfs(byte[] buffer, int bytes, WaveFormat format)
        {
            try
            {
                if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
                {
                    int samples = bytes / 4; // 32-bit float
                    if (samples == 0) return double.NegativeInfinity;
                    double sumSq = 0;
                    for (int i = 0; i < bytes; i += 4)
                    {
                        float sample = BitConverter.ToSingle(buffer, i);
                        sumSq += (double)sample * sample;
                    }
                    double rms = Math.Sqrt(sumSq / samples);
                    double dbfs = 20.0 * Math.Log10(rms + 1e-12);
                    return (!double.IsNaN(dbfs) && !double.IsInfinity(dbfs)) ? dbfs : double.NegativeInfinity;
                }
                else if (format.BitsPerSample == 16)
                {
                    int samples = bytes / 2;
                    if (samples == 0) return double.NegativeInfinity;
                    double sumSq = 0;
                    for (int i = 0; i < bytes; i += 2)
                    {
                        short s = BitConverter.ToInt16(buffer, i);
                        double norm = s / 32768.0; // -1..1
                        sumSq += norm * norm;
                    }
                    double rms = Math.Sqrt(sumSq / samples);
                    double dbfs = 20.0 * Math.Log10(rms + 1e-12);
                    return (!double.IsNaN(dbfs) && !double.IsInfinity(dbfs)) ? dbfs : double.NegativeInfinity;
                }
                else
                {
                    // 其它位深简单回退：按字节归一
                    double sum = 0;
                    for (int i = 0; i < bytes; i++) sum += Math.Abs(buffer[i] - 128);
                    double avg = sum / bytes / 128.0; // 0..1
                    double dbfs = 20.0 * Math.Log10(avg + 1e-12);
                    return (!double.IsNaN(dbfs) && !double.IsInfinity(dbfs)) ? dbfs : double.NegativeInfinity;
                }
            }
            catch { return double.NegativeInfinity; }
        }

        private static string GetRecordingDir()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "Recordings");
        }

        private void TryUpdateRecognizerSetting(string name, TimeSpan value)
        {
            try { _recognizer.UpdateRecognizerSetting(name, (int)value.TotalMilliseconds); }
            catch { /* 某些设置可能不被实现，忽略 */ }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
