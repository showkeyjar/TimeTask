using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>
    /// 测试宿主全局初始化：把 VoiceRuntimeLog 重定向到临时目录，
    /// 单测绝不写用户真实日志（%AppData%\TimeTask\logs\voice-runtime.log）。
    /// </summary>
    [TestClass]
    public class TestHostSetup
    {
        /// <summary>全程序集共用的重定向日志路径（个别测试临时改道后必须恢复到它，不能恢复 null）。</summary>
        public static string TestLogPath { get; } =
            Path.Combine(Path.GetTempPath(), "TimeTask.Tests", "logs", "voice-runtime.log");

        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            VoiceRuntimeLog.DetourTo(TestLogPath);
        }
    }
}
