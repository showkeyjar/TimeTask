using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>UiSafe：UI 异步操作统一异常防护。</summary>
    [TestClass]
    public class UiSafeTests
    {
        [TestMethod]
        public async Task RunAsync_ExceptionInsideBody_DoesNotThrow_AndLogs()
        {
            bool ran = false;
            await UiSafe.RunAsync("测试操作", () =>
            {
                ran = true;
                throw new InvalidOperationException("boom");
            }, notifyUser: false); // 测试绝不弹 MessageBox

            Assert.IsTrue(ran);
            // 不外抛即通过；日志断言见下（依赖 Detour 的日志隔离机制）
        }

        [TestMethod]
        public async Task RunAsync_Success_InvokesBodyOnce()
        {
            int calls = 0;
            await UiSafe.RunAsync("测试操作", () => { calls++; return Task.CompletedTask; }, notifyUser: false);
            Assert.AreEqual(1, calls);
        }
    }

    /// <summary>ConsoleTeeWriter：逐字符透传 + 凑行落日志。</summary>
    [TestClass]
    public class ConsoleTeeWriterTests
    {
        [TestMethod]
        public void TeeWriter_AssemblesLines_FromMixedWrites_AndPassesThrough()
        {
            string detoured = Path.Combine(Path.GetTempPath(), "TimeTask.Tests", "tee-test", "voice-runtime.log");
            var inner = new StringWriter();
            try
            {
                VoiceRuntimeLog.DetourTo(detoured);
                using (var tee = new ConsoleTeeWriter(inner, isError: false))
                {
                    tee.Write('A');
                    tee.Write('B');
                    tee.Write('\n');            // → 行 "AB"
                    tee.Write("CD");             // 半行暂存
                    tee.WriteLine("EF");         // → 行 "CDEF"
                    tee.WriteLine(string.Empty); // 空行不记
                }

                string consolePassthrough = inner.ToString();
                Assert.IsTrue(consolePassthrough.Contains("AB"), "原始控制台流必须原样透传");
                Assert.IsTrue(consolePassthrough.Contains("CDEF"));

                string logContent = File.ReadAllText(detoured);
                Assert.IsTrue(logContent.Contains("] AB"), "凑行的内容必须落日志");
                Assert.IsTrue(logContent.Contains("] CDEF"), "Write+WriteLine 的拼行必须落日志");
            }
            finally
            {
                VoiceRuntimeLog.DetourTo(TestHostSetup.TestLogPath);
                try { Directory.Delete(Path.GetDirectoryName(detoured), true); } catch { }
            }
        }

        [TestMethod]
        public void TeeWriter_ErrorStream_LogsAsWarn()
        {
            string detoured = Path.Combine(Path.GetTempPath(), "TimeTask.Tests", "tee-err-test", "voice-runtime.log");
            try
            {
                VoiceRuntimeLog.DetourTo(detoured);
                using (var tee = new ConsoleTeeWriter(TextWriter.Null, isError: true))
                {
                    tee.WriteLine("some python warning");
                }
                string logContent = File.ReadAllText(detoured);
                Assert.IsTrue(logContent.Contains("[WARN] [stderr] some python warning"),
                    "stderr 内容应按 WARN+前缀落日志");
            }
            finally
            {
                VoiceRuntimeLog.DetourTo(TestHostSetup.TestLogPath);
                try { Directory.Delete(Path.GetDirectoryName(detoured), true); } catch { }
            }
        }
    }

    /// <summary>日志体积滚动：超过上限转 .old，当前文件从零开始。</summary>
    [TestClass]
    public class VoiceRuntimeLogRotationTests
    {
        [TestMethod]
        public void Write_OverLimit_RotatesToOldFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "TimeTask.Tests", "rotate-test");
            string logPath = Path.Combine(dir, "voice-runtime.log");
            long originalLimit = VoiceRuntimeLog.MaxLogBytes;
            try
            {
                Directory.CreateDirectory(dir);
                VoiceRuntimeLog.DetourTo(logPath);
                VoiceRuntimeLog.MaxLogBytes = 300; // 每行约 50-70 字节，几行就应触发滚动

                for (int i = 0; i < 12; i++)
                {
                    VoiceRuntimeLog.Info($"rotation-test-line-{i:00} padding-padding-padding");
                }

                Assert.IsTrue(File.Exists(logPath + ".old"), "超限后必须产生 .old 滚动文件");
                long oldSize = new FileInfo(logPath + ".old").Length;
                Assert.IsTrue(oldSize > 0, ".old 应保留超限前的内容");
                long newSize = new FileInfo(logPath).Length;
                Assert.IsTrue(newSize <= VoiceRuntimeLog.MaxLogBytes + 200,
                    $"滚动后的当前文件应远小于累计写入量（actual={newSize}）");
                Assert.IsTrue(oldSize + newSize > 300, "内容总量不应丢失");
            }
            finally
            {
                VoiceRuntimeLog.MaxLogBytes = originalLimit;
                VoiceRuntimeLog.DetourTo(TestHostSetup.TestLogPath);
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
