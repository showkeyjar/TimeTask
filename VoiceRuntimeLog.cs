using System;
using System.IO;

namespace TimeTask
{
    internal static class VoiceRuntimeLog
    {
        private static readonly object Sync = new object();
        private static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // 测试重定向：单测跑在开发机上，绝不能把测试噪声写进用户真实日志
        // （2026-09-22 发现 %AppData%\TimeTask\logs\voice-runtime.log 混入大量单测条目）
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
                        File.AppendAllText(LogFilePath, line + Environment.NewLine);
                    }
                    catch
                    {
                        File.AppendAllText(FallbackLogFilePath, line + Environment.NewLine);
                    }
                }
                Console.WriteLine($"[VoiceRuntimeLog] {line}");
            }
            catch
            {
                // ignore
            }
        }
    }
}
