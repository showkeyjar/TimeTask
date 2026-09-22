using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;

namespace TimeTask
{
    /// <summary>一次录音清理的结果（用于日志与界面提示）。</summary>
    public sealed class RecordingCleanupReport
    {
        public int DeletedCount { get; set; }
        public long FreedBytes { get; set; }
    }

    /// <summary>
    /// 录音保留策略：录音会无限堆积是磁盘隐患（一场 2 小时双轨会议约数百 MB）。
    /// 规则：Recordings 目录下的会话目录（yyyyMMdd_HHmmss\mic.wav/system.wav）与
    /// 旧管线的散落 recording_*.wav，按「最后写入时间」判龄，超过保留天数即整目录删除。
    ///
    /// - 默认保留 7 天（App.config 的 ConversationCaptureRetentionDays，&lt;=0 表示永久保留）。
    /// - 只扫 Recordings 根目录这一层——该目录归应用所有，不会碰到用户其他文件。
    /// - 正在写入的会话永远不满足过期条件（LastWriteTime 实时更新），无并发风险。
    /// - 判龄逻辑（FindExpired）是纯函数，可单元测试；Apply 只做 IO 与容错。
    /// </summary>
    public static class RecordingRetention
    {
        public const string AppSettingKey = "ConversationCaptureRetentionDays";
        public const int DefaultRetentionDays = 7;

        /// <summary>读取配置的保留天数：缺省/非法回落默认 7；&lt;=0 表示永久保留（清理关闭）。</summary>
        public static int GetRetentionDays()
        {
            try
            {
                string raw = ConfigurationManager.AppSettings[AppSettingKey];
                if (int.TryParse(raw, out int days))
                {
                    return days;
                }
            }
            catch
            {
                // 配置不可用（如测试环境无 App.config）时回落默认值
            }
            return DefaultRetentionDays;
        }

        /// <summary>保留策略是否启用（0 = 永久保留，不清理）。</summary>
        public static bool IsEnabled(int retentionDays)
        {
            return retentionDays > 0;
        }

        /// <summary>
        /// 纯函数：返回 Recordings 根目录下已过期的子目录与散落录音文件的完整路径。
        /// 判龄依据是条目的 LastWriteTime（目录取其自身时间戳，足够精确：会话结束即不再更新）。
        /// 根目录不存在时返回空列表（首次运行属正常）。
        /// </summary>
        public static List<string> FindExpired(string recordingsRoot, int retentionDays, DateTime now)
        {
            var expired = new List<string>();
            if (!IsEnabled(retentionDays) || string.IsNullOrEmpty(recordingsRoot) || !Directory.Exists(recordingsRoot))
            {
                return expired;
            }

            DateTime cutoff = now.AddDays(-retentionDays);

            foreach (var dir in Directory.GetDirectories(recordingsRoot))
            {
                try
                {
                    if (Directory.GetLastWriteTime(dir) < cutoff)
                    {
                        expired.Add(dir);
                    }
                }
                catch (IOException) { /* 正在被写/权限问题：跳过本轮，下轮再试 */ }
            }

            foreach (var file in Directory.GetFiles(recordingsRoot))
            {
                try
                {
                    if (IsRecordingFile(file) && File.GetLastWriteTime(file) < cutoff)
                    {
                        expired.Add(file);
                    }
                }
                catch (IOException) { }
            }

            return expired;
        }

        /// <summary>
        /// 执行一轮清理，返回删除条数与释放字节数。逐条容错：单条失败只记日志，
        /// 不影响其余条目（IO 竞态下个别文件被占用是正常情况，下一轮会再清）。
        /// </summary>
        public static RecordingCleanupReport Apply(string recordingsRoot, int retentionDays, DateTime? now = null)
        {
            var report = new RecordingCleanupReport();
            var targets = FindExpired(recordingsRoot, retentionDays, now ?? DateTime.Now);

            foreach (var path in targets)
            {
                try
                {
                    long size = GetSize(path);
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }
                    else if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else
                    {
                        continue; // 两轮之间已被删
                    }
                    report.DeletedCount++;
                    report.FreedBytes += size;
                }
                catch (Exception ex)
                {
                    VoiceRuntimeLog.Error($"清理过期录音失败：{path}", ex);
                }
            }

            if (report.DeletedCount > 0)
            {
                VoiceRuntimeLog.Info(
                    $"已清理 {report.DeletedCount} 个过期录音（保留 {retentionDays} 天策略），释放 {report.FreedBytes / (1024 * 1024.0):F1} MB。");
            }
            return report;
        }

        /// <summary>散落录音文件识别：旧管线产物 recording_*.wav；会话目录内文件随目录整体删除。</summary>
        private static bool IsRecordingFile(string path)
        {
            string name = Path.GetFileName(path);
            return name.StartsWith("recording_", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        }

        private static long GetSize(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    long total = 0;
                    foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        total += new FileInfo(f).Length;
                    }
                    return total;
                }
                if (File.Exists(path))
                {
                    return new FileInfo(path).Length;
                }
            }
            catch { /* 统计失败不影响删除 */ }
            return 0;
        }
    }
}
