using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>
    /// 数据韧性：原子写入、.bak 备份与损坏自动回退的契约测试。
    /// 这些机制是「断电/崩溃不丢用户数据」的最后一道防线，行为必须锁定。
    /// </summary>
    [TestClass]
    public class DataResilienceTests
    {
        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "TimeTask.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ---------- AtomicFile ----------

        [TestMethod]
        public void AtomicFile_WriteTwice_CreatesBakWithPreviousContent()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "store.json");
                AtomicFile.WriteAllText(path, "v1");
                AtomicFile.WriteAllText(path, "v2");

                Assert.AreEqual("v2", File.ReadAllText(path), "主文件应是最新内容");
                Assert.IsTrue(File.Exists(path + ".bak"), "第二次写入应产生 .bak");
                Assert.AreEqual("v1", File.ReadAllText(path + ".bak"), ".bak 应保留上一代内容");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // ---------- JsonStore ----------

        private static string ValidatingDeserializer(string json)
        {
            // 简化的反序列化器：内容必须包含 "OK"，否则视为损坏
            if (string.IsNullOrWhiteSpace(json) || !json.Contains("OK"))
            {
                throw new FormatException("bad json");
            }
            return json;
        }

        [TestMethod]
        public void JsonStore_Load_CorruptMain_FallsBackToBak()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "state.json");
                File.WriteAllText(path, "{ truncated garbage"); // 可读但解析必败
                File.WriteAllText(path + ".bak", "OK-data");

                string result = JsonStore.Load(path, ValidatingDeserializer);
                Assert.AreEqual("OK-data", result, "主文件解析失败时应回退 .bak");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [TestMethod]
        public void JsonStore_Load_NoFiles_ReturnsNull()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "missing.json");
                Assert.IsNull(JsonStore.Load(path, ValidatingDeserializer), "无主文件也无备份应返回 null");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [TestMethod]
        public void JsonStore_Load_HealthyMain_IgnoresBak()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "state.json");
                File.WriteAllText(path, "OK-main");
                File.WriteAllText(path + ".bak", "OK-bak");

                string result = JsonStore.Load(path, ValidatingDeserializer);
                Assert.AreEqual("OK-main", result, "主文件健康时不得用旧备份覆盖新数据");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // ---------- ReadCsv 备份回退 ----------

        private const string CsvHeader = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate";

        [TestMethod]
        public void ReadCsv_CorruptMainWithCleanBackup_FallsBackToBackup()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "1.csv");
                // 主文件：2 条好任务 + 1 条坏行（字段数不足）
                File.WriteAllLines(path, new[]
                {
                    CsvHeader,
                    "任务A,1,,False,High,High,2026-01-01,2026-01-01",
                    "broken-line",
                    "任务B,2,,False,High,Low,2026-01-01,2026-01-01"
                });
                // 备份：同样 2 条好任务、无坏行
                File.WriteAllLines(path + ".bak", new[]
                {
                    CsvHeader,
                    "任务A,1,,False,High,High,2026-01-01,2026-01-01",
                    "任务B,2,,False,High,Low,2026-01-01,2026-01-01"
                });

                var items = HelperClass.ReadCsv(path);
                Assert.IsNotNull(items);
                Assert.AreEqual(2, items.Count, "坏行主文件应回退到干净备份");
                Assert.AreEqual("任务A", items[0].Task);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [TestMethod]
        public void ReadCsv_BackupWouldLoseRows_KeepsMain()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "2.csv");
                // 主文件：3 条好任务 + 1 条坏行
                File.WriteAllLines(path, new[]
                {
                    CsvHeader,
                    "任务A,1,,False,High,High,2026-01-01,2026-01-01",
                    "broken-line",
                    "任务B,2,,False,High,Low,2026-01-01,2026-01-01",
                    "任务C,3,,False,Low,Low,2026-01-01,2026-01-01"
                });
                // 备份：只有 1 条（更旧）——回退会丢 2 条新任务
                File.WriteAllLines(path + ".bak", new[]
                {
                    CsvHeader,
                    "任务A,1,,False,High,High,2026-01-01,2026-01-01"
                });

                var items = HelperClass.ReadCsv(path);
                Assert.IsNotNull(items);
                Assert.AreEqual(3, items.Count, "备份好行更少时必须保留主文件数据，宁可丢坏行不丢好行");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [TestMethod]
        public void ReadCsv_MissingFile_ReturnsNull()
        {
            string dir = TempDir();
            try
            {
                Assert.IsNull(HelperClass.ReadCsv(Path.Combine(dir, "nope.csv")));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
