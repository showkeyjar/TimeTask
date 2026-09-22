using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>LlmRetryPolicy：瞬时失败重试、非瞬时错误豁免、正常内容绝不重试。</summary>
    [TestClass]
    public class LlmRetryPolicyTests
    {
        // ---- IsRetryable 分类 ----

        [DataRow("Error from Zhipu AI: HTTP 502 - Bad Gateway")]
        [DataRow("Error from Zhipu AI: HTTP 503 - Service Unavailable")]
        [DataRow("Error from Zhipu AI: HTTP 500 - Internal Server Error")]
        [DataRow("Error from Zhipu AI: HTTP 429 - Too Many Requests")]
        [DataRow("Error from Zhipu AI: HTTP 408 - Request Timeout")]
        [DataRow("Error from Zhipu AI: HTTP 521 - Web Server Is Down")]
        [DataTestMethod]
        public void IsRetryable_ServerOrThrottleErrors_AreRetryable(string result)
        {
            Assert.IsTrue(LlmRetryPolicy.IsRetryable(result), result);
        }

        [TestMethod]
        public void IsRetryable_TimeoutDummyResponse_IsRetryable()
        {
            // Betalgo 超时路径的文案（timed out or canceled 是历史措辞，本质是超时）
            Assert.IsTrue(LlmRetryPolicy.IsRetryable(
                "LLM dummy response (request timed out or canceled after 120s). Prompt: x"));
        }

        [TestMethod]
        public void IsRetryable_NetworkExceptionMessages_AreRetryable()
        {
            Assert.IsTrue(LlmRetryPolicy.IsRetryable("Error from Zhipu AI: 发送请求时出错。"));
            Assert.IsTrue(LlmRetryPolicy.IsRetryable("Error from LLM: HttpRequestException: Connection refused"));
            Assert.IsTrue(LlmRetryPolicy.IsRetryable("Error from LLM: The operation timed out"));
        }

        [TestMethod]
        public void IsRetryable_CallerCancellation_IsNeverRetryable()
        {
            Assert.IsFalse(LlmRetryPolicy.IsRetryable("Error from Zhipu AI: request cancelled."));
            Assert.IsFalse(LlmRetryPolicy.IsRetryable("Error from LLM: request cancelled by caller."));
        }

        [TestMethod]
        public void IsRetryable_AuthAndConfigErrors_AreNotRetryable()
        {
            Assert.IsFalse(LlmRetryPolicy.IsRetryable("Error from Zhipu AI: HTTP 401 - unauthorized"));
            Assert.IsFalse(LlmRetryPolicy.IsRetryable("Error from Zhipu AI: HTTP 403 - forbidden"));
            Assert.IsFalse(LlmRetryPolicy.IsRetryable(
                "LLM dummy response (Configuration Error: API key missing or placeholder). Prompt: x"));
        }

        [TestMethod]
        public void IsRetryable_ParseAndFormatErrors_AreNotRetryable()
        {
            Assert.IsFalse(LlmRetryPolicy.IsRetryable("Error from Zhipu AI: Invalid response format"));
            Assert.IsFalse(LlmRetryPolicy.IsRetryable("Error from LLM: Could not parse the entire response. Details: x"));
        }

        [TestMethod]
        public void IsRetryable_NormalContentIsNeverRetryable_EvenIfItMentionsTimeout()
        {
            // 会议转写完全可能聊到“连接超时”——正常内容绝不能触发重试（重复计费+延迟）
            Assert.IsFalse(LlmRetryPolicy.IsRetryable("我们讨论了连接超时怎么处理，决定把超时设为三十秒。"));
            Assert.IsFalse(LlmRetryPolicy.IsRetryable("HTTP 500 错误的含义是服务器内部错误。"));
        }

        [TestMethod]
        public void IsRetryable_NullOrEmpty_IsRetryable()
        {
            Assert.IsTrue(LlmRetryPolicy.IsRetryable(null));
            Assert.IsTrue(LlmRetryPolicy.IsRetryable(string.Empty));
            Assert.IsTrue(LlmRetryPolicy.IsRetryable("   "));
        }

        [TestMethod]
        public void IsRetryable_UnknownErrorShape_IsNotRetryable()
        {
            // 未知错误保守不重试
            Assert.IsFalse(LlmRetryPolicy.IsRetryable("Error from LLM: something completely unexpected"));
        }

        // ---- ExecuteAsync 重试循环 ----

        private static Task<string> Result(string s) => Task.FromResult(s);

        [TestMethod]
        public async Task ExecuteAsync_TransientThenSuccess_RetriesAndSucceeds()
        {
            int calls = 0;
            string final = await LlmRetryPolicy.ExecuteAsync(
                ct => { calls++; return calls < 3
                    ? Result("Error from Zhipu AI: HTTP 502 - Bad Gateway")
                    : Result("ok"); },
                3, CancellationToken.None,
                (d, ct) => Task.CompletedTask);

            Assert.AreEqual("ok", final);
            Assert.AreEqual(3, calls);
        }

        [TestMethod]
        public async Task ExecuteAsync_NonRetryableError_DoesNotRetry()
        {
            int calls = 0;
            string final = await LlmRetryPolicy.ExecuteAsync(
                ct => { calls++; return Result("Error from LLM: request cancelled by caller."); },
                3, CancellationToken.None,
                (d, ct) => Task.CompletedTask);

            Assert.IsTrue(final.Contains("cancelled"));
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public async Task ExecuteAsync_AlwaysTransient_StopsAtMaxAttempts()
        {
            int calls = 0;
            string final = await LlmRetryPolicy.ExecuteAsync(
                ct => { calls++; return Result("Error from Zhipu AI: HTTP 503 - down"); },
                3, CancellationToken.None,
                (d, ct) => Task.CompletedTask);

            Assert.IsTrue(final.Contains("503"));
            Assert.AreEqual(3, calls);
        }

        [TestMethod]
        public async Task ExecuteAsync_MaxAttemptsOne_SingleCall()
        {
            int calls = 0;
            await LlmRetryPolicy.ExecuteAsync(
                ct => { calls++; return Result("Error from Zhipu AI: HTTP 502 - x"); },
                1, CancellationToken.None,
                (d, ct) => Task.CompletedTask);
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public async Task ExecuteAsync_CancellationDuringBackoff_ReturnsLastResult()
        {
            int calls = 0;
            using (var cts = new CancellationTokenSource())
            {
                string final = await LlmRetryPolicy.ExecuteAsync(
                    ct => { calls++; return Result("Error from Zhipu AI: HTTP 502 - x"); },
                    3, cts.Token,
                    (d, ct) => { cts.Cancel(); return Task.FromCanceled(ct); });

                // 等待被打断：立即返回最后一次结果，不抛异常
                Assert.IsTrue(final.Contains("502"));
                Assert.AreEqual(1, calls);
            }
        }

        [TestMethod]
        public void DelayForAttempt_IsBoundedAndIncreasing()
        {
            TimeSpan d1 = LlmRetryPolicy.DelayForAttempt(1);
            TimeSpan d2 = LlmRetryPolicy.DelayForAttempt(2);
            TimeSpan d3 = LlmRetryPolicy.DelayForAttempt(3);
            TimeSpan d9 = LlmRetryPolicy.DelayForAttempt(9);

            Assert.IsTrue(d1 >= TimeSpan.FromSeconds(1) && d1 < TimeSpan.FromSeconds(2), d1.ToString());
            Assert.IsTrue(d2 >= TimeSpan.FromSeconds(2) && d2 < TimeSpan.FromSeconds(3), d2.ToString());
            Assert.IsTrue(d3 >= TimeSpan.FromSeconds(4) && d3 < TimeSpan.FromSeconds(5), d3.ToString());
            Assert.IsTrue(d9 >= TimeSpan.FromSeconds(8) && d9 < TimeSpan.FromSeconds(9), d9.ToString());
        }
    }

    /// <summary>AutoUpdateService.ParseVersion：对真实世界 tag 形态的容忍度。</summary>
    [TestClass]
    public class AutoUpdateVersionParsingTests
    {
        [TestMethod]
        public void ParseVersion_PlainForms()
        {
            // NormalizeVersion 统一返回四段式（Version 的 Equals 严格比较未指定段，须用四段式断言）
            Assert.AreEqual(new Version(1, 2, 3, 0), AutoUpdateService.ParseVersion("v1.2.3"));
            Assert.AreEqual(new Version(1, 2, 3, 0), AutoUpdateService.ParseVersion("1.2.3"));
            Assert.AreEqual(new Version(1, 2, 3, 4), AutoUpdateService.ParseVersion("1.2.3.4"));
            Assert.AreEqual(new Version(3, 0, 0, 0), AutoUpdateService.ParseVersion("3"));
        }

        [TestMethod]
        public void ParseVersion_PrefixedAndSuffixedForms()
        {
            Assert.AreEqual(new Version(2, 0, 1, 0), AutoUpdateService.ParseVersion("release-2.0.1"));
            Assert.AreEqual(new Version(1, 2, 3, 0), AutoUpdateService.ParseVersion("v1.2.3-beta.1"));
            Assert.AreEqual(new Version(0, 9, 0, 0), AutoUpdateService.ParseVersion("V0.9-beta"));
        }

        [TestMethod]
        public void ParseVersion_MixedTextForms()
        {
            // 真实 GitHub Release 常见形态：name 里带产品名+日期
            Assert.AreEqual(new Version(1, 2, 0, 0), AutoUpdateService.ParseVersion("TimeTask v1.2（2026-09-22）"));
            Assert.AreEqual(new Version(1, 2, 3, 0), AutoUpdateService.ParseVersion("  TimeTask v1.2.3 正式版  "));
            Assert.AreEqual(new Version(2026, 9, 22, 0), AutoUpdateService.ParseVersion("build 2026.9.22"));
        }

        [TestMethod]
        public void ParseVersion_Unparseable_ReturnsNull()
        {
            Assert.IsNull(AutoUpdateService.ParseVersion(null));
            Assert.IsNull(AutoUpdateService.ParseVersion("   "));
            Assert.IsNull(AutoUpdateService.ParseVersion("no version here"));
        }
    }

    /// <summary>VoiceRuntimeLog 测试重定向：单测不污染用户真实日志。</summary>
    [TestClass]
    public class VoiceRuntimeLogDetourTests
    {
        [TestMethod]
        public void DetourTo_WritesToDetouredPath_AndNullRestores()
        {
            string detoured = Path.Combine(Path.GetTempPath(), "TimeTask.Tests", "detour-test", "voice-runtime.log");
            try
            {
                VoiceRuntimeLog.DetourTo(detoured);
                Assert.AreEqual(detoured, VoiceRuntimeLog.LogFilePath);

                string marker = "detour-marker-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                VoiceRuntimeLog.Info(marker);

                Assert.IsTrue(File.Exists(detoured), "重定向后日志应写入临时路径");
                string content = File.ReadAllText(detoured);
                Assert.IsTrue(content.Contains(marker), "日志内容应包含标记");

                VoiceRuntimeLog.DetourTo(null);
                string restored = VoiceRuntimeLog.LogFilePath;
                Assert.IsFalse(string.Equals(restored, detoured, StringComparison.OrdinalIgnoreCase),
                    "恢复后不应再指向重定向路径");
            }
            finally
            {
                // 关键：恢复到 AssemblyInit 设置的全程序集重定向路径，而不是 null——
                // 否则按执行顺序排在后面的测试类会直接写进用户真实日志
                // （实测 RecordingRetentionTests/ReminderSyncHostTests 曾因此泄漏）。
                VoiceRuntimeLog.DetourTo(TestHostSetup.TestLogPath);
                try { Directory.Delete(Path.GetDirectoryName(detoured), true); } catch { }
            }
        }
    }
}
