using System;
using System.IO;
using System.Text;

namespace TimeTask
{
    /// <summary>
    /// 运行日志：全应用唯一的文本日志（%AppData%\TimeTask\logs\voice-runtime.log）。
    /// - 测试可重定向（DetourTo），避免单测污染用户真实日志；
    /// - 超过 MaxLogBytes 自动滚动（当前内容转 .old，保留一份历史）；
    /// - TeeConsoleToLog：把 Console.Out / Console.Error 并入日志——
    ///   全仓 315 处 Console.WriteLine 在 WPF（无控制台）下原本全部丢失，
    ///   排查语音/LLM 问题时这些诊断恰恰最关键。
    /// </summary>
    internal static class VoiceRuntimeLog
    {
        private static readonly object Sync = new object();
        private static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // 原始控制台流：必须在 Tee 安装之前捕获（本类 cctor 在首次使用时运行，
        // App 启动第一行日志早于 Tee 安装 → 捕获的一定是真正的控制台）
        private static readonly TextWriter OriginalOut = Console.Out;
        private static readonly TextWriter OriginalError = Console.Error;
        private static bool _teeInstalled;

        /// <summary>日志体积上限（字节）：超过则滚动。internal 供测试注入小值。</summary>
        internal static long MaxLogBytes = 5L * 1024 * 1024;

        // 测试重定向：单测跑在开发机上，绝不能把测试噪声写进用户真实日志
        private static string _detouredPath;

        /// <summary>重定向日志输出（测试专用）。传 null 恢复默认位置。</summary>
        public static void DetourTo(string path)
        {
            lock (Sync)
            {
                _detouredPath = path;
            }
        }

        public static string LogFilePath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_detouredPath))
                {
                    string detouredDir = Path.GetDirectoryName(_detouredPath);
                    if (!string.IsNullOrEmpty(detouredDir))
                    {
                        Directory.CreateDirectory(detouredDir);
                    }
                    return _detouredPath;
                }
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, "TimeTask", "logs");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "voice-runtime.log");
            }
        }

        public static string FallbackLogFilePath
        {
            get
            {
                string dir = Path.Combine(BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "voice-runtime.log");
            }
        }

        /// <summary>
        /// 把控制台输出并入日志（幂等，重复调用安全）。App 启动早期调用一次：
        /// 之后所有 Console.WriteLine/WriteLine(stderr) 自动作为日志行落盘，
        /// 原始控制台（若带控制台启动调试）保持原样输出。
        /// </summary>
        public static void TeeConsoleToLog()
        {
            lock (Sync)
            {
                if (_teeInstalled)
                {
                    return;
                }
                _teeInstalled = true;
            }
            Console.SetOut(new ConsoleTeeWriter(OriginalOut, isError: false));
            Console.SetError(new ConsoleTeeWriter(OriginalError, isError: true));
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message, Exception ex = null)
        {
            string full = ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}";
            Write("WARN", full);
        }

        public static void Error(string message, Exception ex = null)
        {
            string full = ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}";
            Write("ERROR", full);
        }

        private static void Write(string level, string message)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                lock (Sync)
                {
                    try
                    {
                        RotateIfNeeded(LogFilePath);
                        File.AppendAllText(LogFilePath, line + Environment.NewLine);
                    }
                    catch
                    {
                        File.AppendAllText(FallbackLogFilePath, line + Environment.NewLine);
                    }
                }
                // 控制台回显走原始流（Tee 安装后不再回显，避免与 Tee 双写）
                if (!_teeInstalled)
                {
                    try { OriginalOut.WriteLine($"[VoiceRuntimeLog] {line}"); } catch { }
                }
            }
            catch
            {
                // 日志失败绝不能影响业务
            }
        }

        /// <summary>体积滚动：超过上限时当前内容转 .old（删旧留新一份），当前文件从零开始。</summary>
        private static void RotateIfNeeded(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length <= MaxLogBytes)
                {
                    return;
                }
                string backup = path + ".old";
                try { File.Delete(backup); } catch { }
                File.Move(path, backup);
            }
            catch
            {
                // 滚动失败不阻断写日志
            }
        }
    }

    /// <summary>
    /// 控制台 Tee：逐字符/整行透传给原始流（带控制台调试时原样可见），
    /// 凑齐一行后写入 VoiceRuntimeLog（stderr 的内容按 WARN 记）。
    /// internal 供契约测试直接构造。
    /// </summary>
    internal sealed class ConsoleTeeWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly bool _error;
        private readonly StringBuilder _pending = new StringBuilder();

        public ConsoleTeeWriter(TextWriter inner, bool isError)
        {
            _inner = inner ?? TextWriter.Null;
            _error = isError;
        }

        public override Encoding Encoding
        {
            get { return _inner.Encoding; }
        }

        public override void Write(char value)
        {
            _inner.Write(value);
            if (value == '\r')
            {
                return;
            }
            if (value == '\n')
            {
                FlushLine();
                return;
            }
            _pending.Append(value);
        }

        public override void Write(string value)
        {
            _inner.Write(value);
            if (value != null)
            {
                _pending.Append(value);
            }
        }

        public override void WriteLine(string value)
        {
            _inner.WriteLine(value);
            if (value != null)
            {
                _pending.Append(value);
            }
            FlushLine();
        }

        public override void Flush()
        {
            _inner.Flush();
            base.Flush();
        }

        private void FlushLine()
        {
            string line = _pending.ToString().TrimEnd('\r', '\n');
            _pending.Clear();
            if (line.Length == 0)
            {
                return;
            }
            if (_error)
            {
                VoiceRuntimeLog.Warn("[stderr] " + line);
            }
            else
            {
                VoiceRuntimeLog.Info(line);
            }
        }
    }
}
