using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// FunASR 常驻识别进程的封装：scripts/funasr_asr.py --server。
    /// 协议：stdin 发 {"wav":路径}，stdout 回 {"ok":true,"text":...}；模型加载完成后先发 {"event":"ready"}。
    ///
    /// 就绪策略（按优先级）：
    /// 1. 已在跑且已 ready 的 worker 直接复用（跨会话常驻，模型只加载一次）；
    /// 2. 直接用本机 python 启动（FunAsrPythonExe 配置 / PATH 上的 python / py 启动器）——
    ///    依赖缺失时脚本会快速退出，自动换下一个候选；
    /// 3. FunAsrRuntimeManager 的预置运行包（data\funasr-runtime-bundle.zip）——
    ///    注意其 allowOnlineInstallFallback 默认关闭，本机已装 python 的场景走第 2 条即可命中。
    ///
    /// 关键教训（2026-09-22 实测「一直提示高精度模型准备中」的根因）：
    /// - 就绪等待超时**绝不杀进程**——首次要从 modelscope 下载约 230MB 模型，杀掉就前功尽弃、
    ///   每场录音重新下载永远到不了头。超时只意味着「本轮会话先回落」，worker 继续后台准备。
    /// - ready 之前 worker 不接识别请求（此时请求会与 ready 行错位）。
    /// </summary>
    public sealed class FunAsrEngine : IDisposable
    {
        private const string ReadyMarker = "\"event\""; // {"event":"ready"}

        private readonly string _scriptPath;
        private readonly string _model;
        private readonly string _device;
        private readonly int _timeoutSeconds;

        private readonly object _startLock = new object();
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private Process _proc;
        private StreamWriter _stdin;
        private StreamReader _stdout;
        private Task _stderrDrain;
        private Task<bool> _readyWatcher;
        private volatile bool _workerReady;

        public FunAsrEngine()
        {
            _scriptPath = ResolveScriptPath(ReadString("FunAsrScriptPath", @"scripts\funasr_asr.py"));
            _model = ReadString("FunAsrModel", "iic/SenseVoiceSmall");
            _device = ReadString("FunAsrDevice", "cpu");
            _timeoutSeconds = ReadInt("FunAsrTimeoutSeconds", 60);
        }

        /// <summary>worker 已就绪可接识别请求（进程活着 ≠ 就绪：ready 事件之前不接请求）。</summary>
        public bool IsRunning
        {
            get
            {
                lock (_startLock)
                {
                    return _workerReady && _proc != null && !_proc.HasExited && _stdin != null && _stdout != null;
                }
            }
        }

        /// <summary>worker 正在启动/下载模型（尚未就绪）。用于给用户准确的状态文案。</summary>
        public bool IsPreparing
        {
            get
            {
                lock (_startLock)
                {
                    var watcher = _readyWatcher;
                    return !_workerReady && watcher != null && !watcher.IsCompleted
                        && _proc != null && !_proc.HasExited;
                }
            }
        }

        /// <summary>
        /// 脚本路径解析。按序探测：配置原值（相对当前目录）→ exe 目录 → exe 目录向上两级
        /// （开发布局 bin\Debug → 仓库根）→ exe 目录下的 scripts\funasr_asr.py（csproj 拷贝产物）。
        /// </summary>
        public static string ResolveScriptPath(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) return null;
            try
            {
                if (File.Exists(configured)) return Path.GetFullPath(configured);

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string byBase = Path.Combine(baseDir, configured);
                if (File.Exists(byBase)) return Path.GetFullPath(byBase);

                string byRepoRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..", configured));
                if (File.Exists(byRepoRoot)) return Path.GetFullPath(byRepoRoot);

                string byScripts = Path.Combine(baseDir, "scripts", "funasr_asr.py");
                if (File.Exists(byScripts)) return Path.GetFullPath(byScripts);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 确保 worker 可用。startupTimeout 内未就绪即返回 false（本轮回落），
        /// 但 worker 继续在后台准备（首次模型下载不中断），之后的会话直接复用。
        /// </summary>
        public async Task<bool> EnsureReadyAsync(TimeSpan startupTimeout)
        {
            if (IsRunning) return true;
            if (string.IsNullOrWhiteSpace(_scriptPath))
            {
                VoiceRuntimeLog.Info("FunASR 脚本缺失：高精度引擎不可用（检查 scripts/funasr_asr.py）。");
                return false;
            }

            Task<bool> watcher;
            lock (_startLock)
            {
                if (IsRunningLocked()) return true;
                if (_readyWatcher == null || _readyWatcher.IsCompleted)
                {
                    _readyWatcher = StartWorkerAndWatchReadyAsync();
                }
                watcher = _readyWatcher;
            }

            var done = await Task.WhenAny(watcher, Task.Delay(startupTimeout)).ConfigureAwait(false);
            if (done != watcher)
            {
                VoiceRuntimeLog.Info(
                    $"FunASR worker {startupTimeout.TotalSeconds:F0}s 内未就绪（首次需下载约 230MB 模型）：本轮回落，worker 继续后台准备。");
                return false;
            }
            return watcher.Status == TaskStatus.RanToCompletion && watcher.Result;
        }

        /// <summary>识别一个 WAV 文件（调用方负责生成与删除临时文件）。</summary>
        public async Task<FunAsrResult> RecognizeWavAsync(string wavPath)
        {
            if (!IsRunning)
            {
                return FunAsrResult.Fail("worker-not-ready");
            }

            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                StreamWriter stdin;
                StreamReader stdout;
                lock (_startLock)
                {
                    if (!IsRunningLocked()) return FunAsrResult.Fail("worker-not-ready");
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
            Task<bool> watcher;
            lock (_startLock)
            {
                watcher = _readyWatcher;
                StopLocked();
            }
            // 启动观察者自身最多等 3 秒（它可能正卡在模型下载的长等待里）
            try
            {
                if (watcher != null && !watcher.IsCompleted)
                {
                    Task.WhenAny(watcher, Task.Delay(TimeSpan.FromSeconds(3))).Wait();
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Shutdown();
        }

        // ---------- worker 启动与就绪观察 ----------

        /// <summary>
        /// 依次尝试各 python 候选启动 worker 并等待 ready（首次含模型下载，最长 15 分钟）。
        /// 任一候选就绪即成功；候选进程快速退出（依赖缺失）则自动换下一个。
        /// </summary>
        private async Task<bool> StartWorkerAndWatchReadyAsync()
        {
            try
            {
                foreach (var python in await ResolvePythonCandidatesAsync().ConfigureAwait(false))
                {
                    Process proc;
                    StreamWriter stdin;
                    StreamReader stdout;
                    lock (_startLock)
                    {
                        // 上一候选的残留先清干净
                        StopLocked();
                        if (!TryStartWorkerLocked(python, out proc, out stdin, out stdout))
                        {
                            VoiceRuntimeLog.Info($"FunASR worker 进程启动失败：python={python}");
                            continue;
                        }
                        // 提交给实例字段（RecognizeWavAsync 在 ready 前不会消费它们）
                        _proc = proc;
                        _stdin = stdin;
                        _stdout = stdout;
                    }

                    string outcome = await WaitReadyAsync(proc, stdout, TimeSpan.FromMinutes(15)).ConfigureAwait(false);
                    if (outcome == "ready")
                    {
                        lock (_startLock)
                        {
                            if (!ReferenceEquals(_proc, proc))
                            {
                                // 等待期间被并发重启过：这个进程作废
                                KillProcessTree(proc, stdin);
                                continue;
                            }
                            _workerReady = true;
                        }
                        VoiceRuntimeLog.Info($"FunASR worker 就绪：python={python}, model={_model}, device={_device}");
                        return true;
                    }

                    VoiceRuntimeLog.Info($"FunASR worker 未就绪（python={python}, reason={outcome}）：尝试下一个候选。");
                    lock (_startLock)
                    {
                        if (ReferenceEquals(_proc, proc))
                        {
                            StopLocked(); // 清掉本候选，进入下一个
                        }
                        else
                        {
                            KillProcessTree(proc, stdin);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(_scriptPath))
                {
                    VoiceRuntimeLog.Info("FunASR 脚本缺失：所有 python 候选跳过。");
                }
                return false;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR worker 启动流程异常。", ex);
                return false;
            }
        }

        /// <summary>python 候选：运行环境管理器已就绪的（预置包）优先，其次本机配置的 python / PATH 上的 python / py 启动器。</summary>
        private static async Task<System.Collections.Generic.List<string>> ResolvePythonCandidatesAsync()
        {
            var list = new System.Collections.Generic.List<string>();
            try
            {
                var runtime = FunAsrRuntimeManager.EnsureReadyAsync();
                var done = await Task.WhenAny(runtime, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
                if (done == runtime && runtime.Status == TaskStatus.RanToCompletion
                    && runtime.Result != null && runtime.Result.IsReady
                    && !string.IsNullOrWhiteSpace(runtime.Result.PythonExe))
                {
                    list.Add(runtime.Result.PythonExe); // 已验证的预置包优先
                    VoiceRuntimeLog.Info($"FunASR python 候选：预置运行包 {runtime.Result.PythonExe}");
                }
            }
            catch { }

            AddCandidate(list, ReadString("FunAsrPythonExe", "python"));
            AddCandidate(list, "python");
            AddCandidate(list, "py");
            return list;
        }

        private static void AddCandidate(System.Collections.Generic.List<string> list, string exe)
        {
            if (!string.IsNullOrWhiteSpace(exe)
                && !list.Contains(exe, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(exe);
            }
        }

        private bool TryStartWorkerLocked(string pythonExe, out Process proc, out StreamWriter stdin, out StreamReader stdout)
        {
            proc = null;
            stdin = null;
            stdout = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
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

                proc = new Process { StartInfo = psi };
                proc.Start();
                stdin = proc.StandardInput;
                stdout = proc.StandardOutput;
                var captured = proc;
                _stderrDrain = Task.Run(() =>
                {
                    try
                    {
                        string line;
                        while ((line = captured.StandardError.ReadLine()) != null)
                        {
                            if (line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                VoiceRuntimeLog.Info("[funasr-worker] " + line);
                            }
                        }
                    }
                    catch { }
                });
                return true;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Info($"FunASR worker 启动异常：python={pythonExe}, {ex.Message}");
                try { proc?.Dispose(); } catch { }
                return false;
            }
        }

        /// <summary>
        /// 等 ready 事件。单挂起读取（StreamReader 不支持并发异步读）：一行读完成再读下一行；
        /// 进程退出（依赖缺失时脚本数秒内退掉）→ 立即返回让调用方换候选；最长 maxWait。
        /// </summary>
        private static async Task<string> WaitReadyAsync(Process proc, StreamReader stdout, TimeSpan maxWait)
        {
            var deadline = DateTime.UtcNow + maxWait;
            Task<string> pending = stdout.ReadLineAsync();
            while (DateTime.UtcNow < deadline)
            {
                if (proc.HasExited)
                {
                    return "process-exited:" + proc.ExitCode;
                }

                var step = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
                if (step == pending)
                {
                    if (pending.IsFaulted)
                    {
                        return "stream-error";
                    }
                    string line = pending.Status == TaskStatus.RanToCompletion ? pending.Result : null;
                    if (line == null)
                    {
                        return "stream-closed";
                    }
                    if (line.IndexOf(ReadyMarker, StringComparison.Ordinal) >= 0)
                    {
                        return "ready"; // 注意：不再发起新的读取，把流干净地交给识别请求
                    }
                    // 其他输出（库日志等）：继续等
                    pending = stdout.ReadLineAsync();
                }
                // 3 秒无输出：回到循环头检查进程状态
            }
            return "timeout";
        }

        // ---------- 停机 ----------

        private bool IsRunningLocked()
        {
            return _workerReady && _proc != null && !_proc.HasExited && _stdin != null && _stdout != null;
        }

        private void StopWorker()
        {
            lock (_startLock) { StopLocked(); }
        }

        private void StopLocked()
        {
            _workerReady = false;
            var proc = _proc;
            var stdin = _stdin;
            _proc = null;
            _stdin = null;
            _stdout = null;
            _readyWatcher = null;

            if (proc == null) return;
            KillProcessTree(proc, stdin);
        }

        private static void KillProcessTree(Process proc, StreamWriter stdin)
        {
            try
            {
                if (stdin != null)
                {
                    // 优雅停机：让 python 正常退出、释放模型内存
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
