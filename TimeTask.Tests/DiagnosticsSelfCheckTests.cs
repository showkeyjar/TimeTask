using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>
    /// DiagnosticsSelfCheck（--diagnostics 无 UI 自检模式）的契约测试。
    /// 全部针对可注入路径的纯检查逻辑；进程探测（python）跳过。
    /// </summary>
    [TestClass]
    public class DiagnosticsSelfCheckTests
    {
        private string _dataDir;
        private string _recordingsDir;

        [TestInitialize]
        public void TestInitialize()
        {
            string root = Path.Combine(Path.GetTempPath(), "TimeTaskTests", "diag", Guid.NewGuid().ToString("N"));
            _dataDir = Path.Combine(root, "data");
            _recordingsDir = Path.Combine(root, "Recordings");
        }

        [TestCleanup]
        public void TestCleanup()
        {
            try
            {
                if (Directory.Exists(Path.GetDirectoryName(_dataDir)))
                {
                    Directory.Delete(Path.GetDirectoryName(_dataDir), true);
                }
            }
            catch { /* 临时目录清理失败不影响测试结论 */ }
        }

        [TestMethod]
        public void IsRequested_Matches_Known_Flags_Case_Insensitive()
        {
            Assert.IsFalse(DiagnosticsSelfCheck.IsRequested(null));
            Assert.IsFalse(DiagnosticsSelfCheck.IsRequested(new string[0]));
            Assert.IsFalse(DiagnosticsSelfCheck.IsRequested(new[] { "--quiet" }));
            Assert.IsTrue(DiagnosticsSelfCheck.IsRequested(new[] { "--diagnostics" }));
            Assert.IsTrue(DiagnosticsSelfCheck.IsRequested(new[] { "--DIAGNOSTICS" }));
            Assert.IsTrue(DiagnosticsSelfCheck.IsRequested(new[] { "--selfcheck" }));
            Assert.IsTrue(DiagnosticsSelfCheck.IsRequested(new[] { "other", "/diagnostics" }));
        }

        [TestMethod]
        public void CheckDirectoryWritable_OK_On_Creatable_Path()
        {
            var result = DiagnosticsSelfCheck.CheckDirectoryWritable("测试目录", _dataDir);
            Assert.AreEqual(DiagnosticsSelfCheck.CheckStatus.Ok, result.Status);
            Assert.IsTrue(Directory.Exists(_dataDir), "探针应顺带创建目录");
            Assert.IsFalse(Directory.EnumerateFiles(_dataDir, ".diag_probe_*").Any(), "探针文件应被删除");
        }

        [TestMethod]
        public void CheckDirectoryWritable_Fails_On_Invalid_Path()
        {
            string invalid = "Z:\\definitely\\not|" + new string('x', 20) + "\\/real";
            var result = DiagnosticsSelfCheck.CheckDirectoryWritable("坏路径", invalid);
            Assert.AreEqual(DiagnosticsSelfCheck.CheckStatus.Fail, result.Status);
        }

        [TestMethod]
        public void CheckQuadrantCsvs_Reports_Rows_And_Bad_Lines()
        {
            Directory.CreateDirectory(_dataDir);
            string header = "task,score,result,is_completed";
            File.WriteAllText(Path.Combine(_dataDir, "1.csv"),
                header + "\n写周报,3,,False\n修bug,2,,True\n", new UTF8Encoding(false));
            // Q2: 含坏行（字段数 < 4）→ WARN
            File.WriteAllText(Path.Combine(_dataDir, "2.csv"),
                header + "\n只有两个字段,5\n", new UTF8Encoding(false));

            var result = DiagnosticsSelfCheck.CheckQuadrantCsvs(_dataDir);

            Assert.AreEqual(DiagnosticsSelfCheck.CheckStatus.Warn, result.Status);
            StringAssert.Contains(result.Detail, "Q1=2行");
            StringAssert.Contains(result.Detail, "坏行x1");
        }

        [TestMethod]
        public void CheckQuadrantCsvs_All_Missing_Files_Is_Ok()
        {
            Directory.CreateDirectory(_dataDir);
            var result = DiagnosticsSelfCheck.CheckQuadrantCsvs(_dataDir);
            Assert.AreEqual(DiagnosticsSelfCheck.CheckStatus.Ok, result.Status);
            StringAssert.Contains(result.Detail, "Q1=无文件");
        }

        [TestMethod]
        public void CheckJsonStores_Detects_Truncation_And_Mojibake()
        {
            Directory.CreateDirectory(_dataDir);
            File.WriteAllText(Path.Combine(_dataDir, "good.json"), "{\"a\":1}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_dataDir, "truncated.json"), "{\"a\":", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_dataDir, "mojibake.json"), "{\"a\":\"\uFFFD\uFFFD\"}", new UTF8Encoding(false));

            var result = DiagnosticsSelfCheck.CheckJsonStores(_dataDir);

            Assert.AreEqual(DiagnosticsSelfCheck.CheckStatus.Warn, result.Status);
            StringAssert.Contains(result.Detail, "truncated.json");
            StringAssert.Contains(result.Detail, "mojibake.json");
            Assert.IsFalse(result.Detail.Contains("good.json("), "good.json 不应被列为可疑");
        }

        [TestMethod]
        public void CheckJsonStores_Missing_Data_Dir_Is_Ok()
        {
            var result = DiagnosticsSelfCheck.CheckJsonStores(_dataDir);
            Assert.AreEqual(DiagnosticsSelfCheck.CheckStatus.Ok, result.Status);
        }

        [TestMethod]
        public void CheckDiskSpace_Reports_Available()
        {
            var result = DiagnosticsSelfCheck.CheckDiskSpace(Path.GetTempPath());
            // 本机/CI 磁盘剩余量不可假设，只验证不抛异常且给出盘符细节
            StringAssert.Contains(result.Detail, Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath())));
        }

        [TestMethod]
        public void BuildReport_Contains_Summary_And_All_Sections()
        {
            Directory.CreateDirectory(_dataDir);
            Directory.CreateDirectory(_recordingsDir);
            string report = DiagnosticsSelfCheck.BuildReport(_dataDir, _recordingsDir, skipProcessProbes: true);

            StringAssert.Contains(report, "TimeTask 诊断报告");
            StringAssert.Contains(report, "FAIL=");
            StringAssert.Contains(report, "[OK] 数据目录");
            StringAssert.Contains(report, "[OK] 四象限 CSV");
            StringAssert.Contains(report, "[OK] JSON 存储");
            StringAssert.Contains(report, "[OK] 磁盘空间");
            StringAssert.Contains(report, "FunASR 环境");
        }
    }
}
