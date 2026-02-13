using System;
using System.IO;

namespace TimeTask
{
    internal static class VoiceRuntimeLog
    {
        private static readonly object Sync = new object();
        private static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        public static string LogFilePath
        {
            get
            {
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
