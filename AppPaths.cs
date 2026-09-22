using System;
using System.IO;
using System.Reflection;

namespace TimeTask
{
    /// <summary>
    /// 统一的数据目录约定（便携优先，漫游兜底）：
    ///
    /// 1. 便携模式：若 exe 旁已存在 data\ 目录（历史版本的工作方式，zip 解压即用的场景），
    ///    继续使用它——老用户数据原地不动，U 盘便携场景也保持可用。
    /// 2. 漫游模式：否则使用 %AppData%\TimeTask\data（安装到 Program Files 等只读位置时
    ///    也能正常写入；多用户互不干扰；卸载/升级不丢数据）。
    ///
    /// 所有用户数据的读写都应通过本类取路径，不要再各自拼接 Assembly 目录。
    /// </summary>
    public static class AppPaths
    {
        private static readonly string AppDataBaseName = "TimeTask";

        private static readonly Lazy<string> _dataDir = new Lazy<string>(ResolveDataDir, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private static readonly Lazy<string> _recordingsDir = new Lazy<string>(ResolveRecordingsDir, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 用户数据根目录（便携模式 = exe 旁 data\；漫游模式 = %AppData%\TimeTask\data）。
        /// </summary>
        public static string DataDir => _dataDir.Value;

        /// <summary>
        /// 录音文件根目录（与数据目录同策略）：
        /// 便携模式 = exe 旁 Recordings\（沿用历史布局）；漫游模式 = %AppData%\TimeTask\Recordings。
        /// 安装到 Program Files 等只读位置时，录音仍能正常落盘。
        /// </summary>
        public static string RecordingsDir => _recordingsDir.Value;

        /// <summary>
        /// 数据目录下的子目录/文件路径拼接。
        /// </summary>
        public static string GetDataFile(params string[] segments)
        {
            // 常见用法 GetDataFile("1.csv") / GetDataFile("strategy", "x.json")
            if (segments == null || segments.Length == 0)
            {
                return DataDir;
            }
            return Path.Combine(DataDir, Path.Combine(segments));
        }

        private static string ResolveDataDir()
        {
            string legacyDir = TryGetPortableDataDir();
            if (!string.IsNullOrEmpty(legacyDir))
            {
                return legacyDir;
            }

            string roaming = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDataBaseName,
                "data");
            try
            {
                if (!Directory.Exists(roaming))
                {
                    Directory.CreateDirectory(roaming);
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error($"创建漫游数据目录失败：{roaming}", ex);
            }
            return roaming;
        }

        private static string ResolveRecordingsDir()
        {
            // 便携模式：录音继续留在 exe 旁 Recordings\（与历史版本一致，U 盘场景可用）。
            if (!string.IsNullOrEmpty(TryGetPortableDataDir()))
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recordings");
            }

            // 漫游模式：安装目录（Program Files 等）通常只读，录音必须落到用户目录。
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDataBaseName,
                "Recordings");
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error($"创建录音目录失败：{dir}", ex);
            }
            return dir;
        }

        /// <summary>
        /// exe 旁存在 data\ 目录则视为便携模式（沿用历史数据）。
        /// </summary>
        private static string TryGetPortableDataDir()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string candidate = Path.Combine(baseDir, "data");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("探测便携数据目录失败。", ex);
            }
            return null;
        }
    }
}
