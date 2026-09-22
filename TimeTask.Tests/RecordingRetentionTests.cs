using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>
    /// 录音保留策略契约：只有超过保留期的条目被清、保留期 0 = 永久不清、
    /// 根目录缺失不抛、清理报告计数正确。录音无限堆积是磁盘隐患，这条链路必须锁死。
    /// </summary>
    [TestClass]
    public class RecordingRetentionTests
    {
        private static string TempRoot()
        {
            string dir = Path.Combine(Path.GetTempPath(), "TimeTask.Tests.Recordings", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string MakeSession(string root, string name, DateTime stamp, int bytes = 1000)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "mic.wav"), new byte[bytes]);
            Directory.SetLastWriteTime(dir, stamp);
            return dir;
        }

        private static string MakeFile(string root, string name, DateTime stamp, int bytes = 500)
        {
            string path = Path.Combine(root, name);
            File.WriteAllBytes(path, new byte[bytes]);
            File.SetLastWriteTime(path, stamp);
            return path;
        }

        [TestMethod]
        public void FindExpired_OnlyEntriesOlderThanRetention()
        {
            string root = TempRoot();
            try
            {
                var now = DateTime.Now;
                string oldDir = MakeSession(root, "20260901_120000", now.AddDays(-10));
                string freshDir = MakeSession(root, "20260920_130000", now.AddDays(-1));
                string oldWav = MakeFile(root, "recording_20260901_090000.wav", now.AddDays(-10));
                string freshWav = MakeFile(root, "recording_20260920_090000.wav", now.AddDays(-1));
                // 非录音文件（哪怕很旧）绝不能碰
                MakeFile(root, "notes.txt", now.AddDays(-30));

                var expired = RecordingRetention.FindExpired(root, 7, now);

                CollectionAssert.AreEquivalent(new[] { oldDir, oldWav }, expired,
                    "只清超过保留期的会话目录与散落录音；新条目与非录音文件必须保留");
                Assert.IsTrue(Directory.Exists(freshDir));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [TestMethod]
        public void FindExpired_RetentionZero_DisablesCleanup()
        {
            string root = TempRoot();
            try
            {
                var now = DateTime.Now;
                MakeSession(root, "20200101_000000", now.AddDays(-2000));

                Assert.AreEqual(0, RecordingRetention.FindExpired(root, 0, now).Count,
                    "保留期 0 = 永久保留，哪怕录音已存放多年也不清");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [TestMethod]
        public void FindExpired_MissingRoot_ReturnsEmpty()
        {
            var missing = Path.Combine(Path.GetTempPath(), "TimeTask.Tests.Recordings", Guid.NewGuid().ToString("N"));
            Assert.AreEqual(0, RecordingRetention.FindExpired(missing, 7, DateTime.Now).Count, "首次运行目录不存在属正常");
        }

        [TestMethod]
        public void Apply_DeletesExpired_KeepsFresh_AndReports()
        {
            string root = TempRoot();
            try
            {
                var now = DateTime.Now;
                string oldDir = MakeSession(root, "20260901_120000", now.AddDays(-9), bytes: 2048);
                string freshDir = MakeSession(root, "20260922_120000", now.AddHours(-2), bytes: 512);
                string oldWav = MakeFile(root, "recording_20260901_090000.wav", now.AddDays(-9), bytes: 256);

                var report = RecordingRetention.Apply(root, 7, now);

                Assert.AreEqual(2, report.DeletedCount, "一个过期目录 + 一个过期散落文件");
                Assert.IsTrue(report.FreedBytes >= 2048 + 256, "释放字节数应不小于已知文件大小之和");
                Assert.IsFalse(Directory.Exists(oldDir), "过期目录应被整体删除");
                Assert.IsFalse(File.Exists(oldWav));
                Assert.IsTrue(Directory.Exists(freshDir), "保留期内的录音绝不能被删");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [TestMethod]
        public void RetentionHelpers_ConfigAndGate()
        {
            Assert.IsFalse(RecordingRetention.IsEnabled(0), "0 = 永久保留（清理关闭）");
            Assert.IsFalse(RecordingRetention.IsEnabled(-1));
            Assert.IsTrue(RecordingRetention.IsEnabled(7));
            // 测试工程配置里没有该键：必须回落默认 7 天
            Assert.AreEqual(RecordingRetention.DefaultRetentionDays, RecordingRetention.GetRetentionDays());
        }
    }
}
