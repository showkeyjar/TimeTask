using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TimeTask
{
    /// <summary>录音采集可用的识别引擎。</summary>
    public enum AsrEngineKind
    {
        Vosk = 0,
        FunAsr = 1
    }

    /// <summary>引擎选择结果：auto 语义 = 优先 FunASR、启动失败回落 Vosk。</summary>
    public sealed class AsrEngineChoice
    {
        public AsrEngineKind Kind { get; set; }
        public bool AllowVoskFallback { get; set; }

        /// <summary>
        /// 解析 ConversationCaptureAsrEngine 配置：vosk=只用 Vosk；funasr=只用 FunASR（不回落）；
        /// auto/缺省/非法=优先 FunASR，启动超时自动回落 Vosk（默认）。
        /// </summary>
        public static AsrEngineChoice Resolve(string raw)
        {
            if (string.Equals(raw?.Trim(), "vosk", StringComparison.OrdinalIgnoreCase))
            {
                return new AsrEngineChoice { Kind = AsrEngineKind.Vosk, AllowVoskFallback = false };
            }
            if (string.Equals(raw?.Trim(), "funasr", StringComparison.OrdinalIgnoreCase))
            {
                return new AsrEngineChoice { Kind = AsrEngineKind.FunAsr, AllowVoskFallback = false };
            }
            return new AsrEngineChoice { Kind = AsrEngineKind.FunAsr, AllowVoskFallback = true };
        }
    }

    /// <summary>一次 FunASR 识别的结果。</summary>
    public sealed class FunAsrResult
    {
        public bool Ok { get; set; }
        public string Text { get; set; }
        public double Confidence { get; set; }
        public string Error { get; set; }

        public static FunAsrResult Success(string text, double conf)
        {
            return new FunAsrResult { Ok = true, Text = text ?? string.Empty, Confidence = conf };
        }

        public static FunAsrResult Fail(string error)
        {
            return new FunAsrResult { Ok = false, Error = error ?? string.Empty };
        }

        /// <summary>解析 funasr_asr.py 的 JSON 行（{"ok":true,"text":...,"confidence":...}）；坏行返回 Fail。</summary>
        public static FunAsrResult FromJsonLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return Fail("empty-response");
            }
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(line);
                bool ok = root.Value<bool?>("ok") ?? false;
                if (!ok)
                {
                    return Fail(root.Value<string>("error") ?? "unknown-error");
                }
                string text = root.Value<string>("text") ?? string.Empty;
                double conf = root.Value<double?>("confidence") ?? 0.5;
                return Success(text, conf);
            }
            catch (Exception ex)
            {
                return Fail("bad-json: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// FunASR 分段决策（纯函数）：固定上限强制切段 + 尾部静音提前切段。
    /// 语音按段送识别：段太短请求频繁、段太长延迟高且中间出错损失大；
    /// 「说到停顿处就切」最符合 ASR 的断句习惯，精度最好。
    /// </summary>
    public static class FunAsrSegmenter
    {
        public const int DefaultMaxSegmentSeconds = 12; // 硬上限：再长也切
        public const int DefaultMinSegmentSeconds = 3;  // 静音提前切的最小段长
        public const int DefaultQuietSeconds = 1;       // 连续静音达到该时长即视为说完一句

        /// <summary>bufferedBytes/quietTailBytes 均为 16k/16bit/mono PCM 字节数。</summary>
        public static bool ShouldFlush(int bufferedBytes, int quietTailBytes, int sampleRate,
            int maxSegmentSeconds = DefaultMaxSegmentSeconds,
            int minSegmentSeconds = DefaultMinSegmentSeconds,
            int quietSeconds = DefaultQuietSeconds)
        {
            if (bufferedBytes <= 0) return false;
            int bytesPerSecond = sampleRate * 2;
            if (bufferedBytes >= maxSegmentSeconds * bytesPerSecond) return true;
            return bufferedBytes >= minSegmentSeconds * bytesPerSecond
                && quietTailBytes >= quietSeconds * bytesPerSecond;
        }

        /// <summary>整段 PCM（16bit LE）的 RMS 能量：用于判定「这段是不是静音」。</summary>
        public static double Rms(byte[] pcm)
        {
            if (pcm == null || pcm.Length < 2) return 0;
            int count = pcm.Length / 2;
            long sumSq = 0;
            for (int i = 0; i < count; i++)
            {
                short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                sumSq += (long)s * s;
            }
            return Math.Sqrt((double)sumSq / count);
        }

        /// <summary>静音阈值：低于该 RMS 视为静音（与快捷口述的绝对下限一致）。</summary>
        public const double QuietRmsThreshold = 400.0;
    }

    /// <summary>
    /// FunASR 常驻识别进程的精简封装：scripts/funasr_asr.py --server。
    /// 协议：stdin 发 {"wav":路径}，stdout 回 {"ok":true,"text":...}；模型加载完成后发 {"event":"ready"}。
    /// 进程跨会话常驻（模型只加载一次，之后的录音秒级可用），服务 Dispose 时才停机。
    /// 运行环境（python + 依赖）复用 FunAsrRuntimeManager 的引导与缓存。
    ///
    /// 失败策略：任何读写超时即重启 worker（流上留下悬空读取会错位，重启是最可靠的自愈）。
    /// </summary>
    public sealed class FunAsrEngine : IDisposable
    {
        private readonly string _scriptPath;
        private readonly string _model;
        private readonly string _device;
        private readonly int _timeoutSeconds;

        private readonly object _startLock = new object();
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private Process _proc;
        private StreamWriter _stdin;
        private StreamReader _stdout;
        private string _pythonExe;
        private Task _stderrDrain;

        public FunAsrEngine()
        {
            _scriptPath = ResolveScriptPath(ReadString("FunAsrScriptPath", @"scripts\funasr_asr.py"));
            _model = ReadString("FunAsrModel", "iic/SenseVoiceSmall");
            _device = ReadString("FunAsrDevice", "cpu");
            _timeoutSeconds = ReadInt("FunAsrTimeoutSeconds", 60);
        }

        public bool IsRunning
        {
            get
            {
                lock (_startLock) { return IsRunningLocked(); }
            }
        }

        /// <summary>脚本路径解析（相对路径基于 exe 目录）；找不到返回 null。</summary>
        public static string ResolveScriptPath(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) return null;
            try
            {
                if (File.Exists(configured)) return Path.GetFullPath(configured);
                string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 确保 worker 可用：运行环境就绪（可能触发首次 pip 安装，较慢）→ 启动进程 → 等 ready。
        /// 任一步在 startupTimeout 内未完成即返回 false（引导任务继续后台跑，下次录音再用）。
        /// </summary>
        public async Task<bool> EnsureReadyAsync(TimeSpan startupTimeout)
        {
            if (string.IsNullOrWhiteSpace(_scriptPath))
            {
                VoiceRuntimeLog.Info("FunASR 脚本缺失：高精度引擎不可用（检查 scripts/funasr_asr.py）。");
                return false;
            }

            try
            {
                lock (_startLock)
                {
                    if (IsRunningLocked()) return true;
                }

                // 1) 运行环境（python + funasr/torch）：EnsureReadyAsync 有缓存，多路调用共享同一次引导
                var runtime = FunAsrRuntimeManager.EnsureReadyAsync();
                var winner = await Task.WhenAny(runtime, Task.Delay(startupTimeout)).ConfigureAwait(false);
                if (winner != runtime)
                {
                    VoiceRuntimeLog.Info($"FunASR 运行环境 {startupTimeout.TotalSeconds:F0}s 内未就绪（首次安装较慢），本轮先回落 Vosk。");
                    return false;
                }
                var rt = runtime.Status == TaskStatus.RanToCompletion ? runtime.Result : null;
                if (rt == null || !rt.IsReady || string.IsNullOrWhiteSpace(rt.PythonExe))
                {
                    VoiceRuntimeLog.Info($"FunASR 运行环境不可用：{rt?.Message}");
                    return false;
                }
                _pythonExe = rt.PythonExe;

                // 2) 启动常驻 worker（--server：stdin/stdout JSON 行协议）
                lock (_startLock)
                {
                    if (IsRunningLocked()) return true;
                    StopLocked();

                    var psi = new ProcessStartInfo
                    {
                        FileName = _pythonExe,
                        Arguments = $"\"{_scriptPath}\" --server --model \"{_model}\" --device \"{_device}\"",
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

                    _proc = new Process { StartInfo = psi };
                    _proc.Start();
                    _stdin = _proc.StandardInput;
                    _stdout = _proc.StandardOutput;
                    _stdin.AutoFlush = false;
                    // stderr 必须持续排空，否则 pip/推理的告警会撑爆管道缓冲区、卡死 worker
                    _stderrDrain = Task.Run(() =>
                    {
                        try
                        {
                            string line;
                            while ((line = _proc.StandardError.ReadLine()) != null)
                            {
                                if (line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    VoiceRuntimeLog.Info("[funasr-worker] " + line);
                                }
                            }
                        }
                        catch { }
                    });
                }

                // 3) 等模型加载完成的 ready 事件（首次会从 modelscope 拉模型，可能较久，同样有界）
                string readyLine = await ReadLineWithTimeoutAsync(_stdout, startupTimeout).ConfigureAwait(false);
                if (readyLine == null || readyLine.IndexOf("\"event\"", StringComparison.Ordinal) < 0)
                {
                    VoiceRuntimeLog.Info($"FunASR worker 启动超时/异常：{(readyLine ?? "no-ready-line")}");
                    StopWorker();
                    return false;
                }
                VoiceRuntimeLog.Info($"FunASR worker 就绪：model={_model}, device={_device}");
                return true;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR worker 启动失败。", ex);
                StopWorker();
                return false;
            }
        }

        /// <summary>识别一个 WAV 文件（调用方负责生成与删除临时文件）。</summary>
        public async Task<FunAsrResult> RecognizeWavAsync(string wavPath)
        {
            if (!IsRunning)
            {
                return FunAsrResult.Fail("worker-not-running");
            }

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                StreamWriter stdin;
                StreamReader stdout;
                lock (_startLock)
                {
                    if (!IsRunningLocked()) return FunAsrResult.Fail("worker-not-running");
                    stdin = _stdin;
                    stdout = _stdout;
                }

                string req = Newtonsoft.Json.Linq.JObject.FromObject(new { wav = wavPath })
                    .ToString(Newtonsoft.Json.Formatting.None);
                await stdin.WriteLineAsync(req).ConfigureAwait(false);
                await stdin.FlushAsync().ConfigureAwait(false);

                var resp = await ReadLineWithTimeoutAsync(stdout, TimeSpan.FromSeconds(Math.Max(5, _timeoutSeconds)))
                    .ConfigureAwait(false);
                if (resp == null)
                {
                    // 读超时：流上留下悬空 ReadLine 会造成后续应答错位，重启 worker 自愈
                    VoiceRuntimeLog.Info("FunASR 识别超时：重启 worker。");
                    StopWorker();
                    return FunAsrResult.Fail("recognize-timeout");
                }
                return FunAsrResult.FromJsonLine(resp);
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR 识别请求失败。", ex);
                StopWorker();
                return FunAsrResult.Fail("request-failed: " + ex.Message);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        /// <summary>构造 16kHz/16bit/mono 的 WAV 字节（RIFF 头 + PCM 数据）。</summary>
        public static byte[] BuildWav16kMono(byte[] pcm)
        {
            if (pcm == null) pcm = new byte[0];
            int dataLen = pcm.Length;
            var wav = new byte[44 + dataLen];
            Encoding.ASCII.GetBytes("RIFF", 0, 4, wav, 0);
            WriteLE32(wav, 4, 36 + dataLen);
            Encoding.ASCII.GetBytes("WAVE", 0, 4, wav, 8);
            Encoding.ASCII.GetBytes("fmt ", 0, 4, wav, 12);
            WriteLE32(wav, 16, 16);          // fmt chunk size
            wav[20] = 1; wav[21] = 0;        // PCM
            wav[22] = 1; wav[23] = 0;        // mono
            WriteLE32(wav, 24, 16000);       // sample rate
            WriteLE32(wav, 28, 32000);       // byte rate = 16000*1*2
            wav[32] = 2; wav[33] = 0;        // block align
            wav[34] = 16; wav[35] = 0;       // bits per sample
            Encoding.ASCII.GetBytes("data", 0, 4, wav, 36);
            WriteLE32(wav, 40, dataLen);
            Buffer.BlockCopy(pcm, 0, wav, 44, dataLen);
            return wav;
        }

        /// <summary>温和停机（shutdown 指令 + 有界等待），超时杀进程树（torch 可能派生子进程）。</summary>
        public void Shutdown()
        {
            lock (_startLock)
            {
                StopLocked();
            }
        }

        public void Dispose()
        {
            Shutdown();
        }

        // ---------- 内部 ----------

        private bool IsRunningLocked()
        {
            return _proc != null && !_proc.HasExited && _stdin != null && _stdout != null;
        }

        private void StopWorker()
        {
            lock (_startLock) { StopLocked(); }
        }

        private void StopLocked()
        {
            var proc = _proc;
            var stdin = _stdin;
            _proc = null;
            _stdin = null;
            _stdout = null;

            if (proc == null) return;

            try
            {
                if (stdin != null)
                {
                    // 优雅停机：让 python 正常退出、释放模型显存/内存
                    var req = Newtonsoft.Json.Linq.JObject.FromObject(new { cmd = "shutdown" })
                        .ToString(Newtonsoft.Json.Formatting.None);
                    stdin.WriteLine(req);
                    stdin.Flush();
                }
            }
            catch { }

            try
            {
                if (!proc.WaitForExit(3000))
                {
                    ProcessUtils.KillTree(proc, "funasr-worker-shutdown");
                }
            }
            catch { }
            try { proc.Dispose(); } catch { }
            try { stdin?.Dispose(); } catch { }
        }

        /// <summary>带超时的行读取：超时返回 null（不取消底层读取——由调用方重启 worker 清理）。</summary>
        private static async Task<string> ReadLineWithTimeoutAsync(StreamReader reader, TimeSpan timeout)
        {
            var readTask = reader.ReadLineAsync();
            var done = await Task.WhenAny(readTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (done != readTask) return null;
            if (readTask.IsFaulted) return null;
            return readTask.Status == TaskStatus.RanToCompletion ? readTask.Result : null;
        }

        private static void WriteLE32(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static string ReadString(string key, string fallback)
        {
            try
            {
                var v = ConfigurationManager.AppSettings[key];
                return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
            }
            catch { return fallback; }
        }

        private static int ReadInt(string key, int fallback)
        {
            try
            {
                var v = ConfigurationManager.AppSettings[key];
                return int.TryParse(v, out int n) ? n : fallback;
            }
            catch { return fallback; }
        }
    }
}
