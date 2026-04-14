using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace TimeTask.Tests
{
    [TestClass]
    public class VoiceRecognitionQualityEngineTests
    {
        private string _tempRoot;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "TimeTaskTests", "VoiceQuality", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_tempRoot, "conversations"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [TestMethod]
        public void BuildAndPersist_GeneratesReportWithAcceptedAndRejectedExamples()
        {
            var sessions = new List<ConversationSession>
            {
                new ConversationSession
                {
                    SessionId = "s1",
                    StartTime = DateTime.Now,
                    Segments = new List<ConversationSegment>
                    {
                        new ConversationSegment { RecognizedText = "提醒我明天下午三点提交周报" },
                        new ConversationSegment { RecognizedText = "为什么这个事情要这样弄呢" },
                        new ConversationSegment { RecognizedText = "对啊" }
                    }
                }
            };

            var serializer = new JavaScriptSerializer();
            File.WriteAllText(
                Path.Combine(_tempRoot, "conversations", "sessions.json"),
                serializer.Serialize(sessions));

            var tasks = new List<ItemGrid>
            {
                new ItemGrid { Task = "提醒我明天下午三点提交周报", IsActive = true },
                new ItemGrid { Task = "对啊", IsActive = true }
            };

            var engine = new VoiceRecognitionQualityEngine(_tempRoot);
            var report = engine.BuildAndPersist(tasks, DateTime.Now);

            Assert.IsNotNull(report);
            Assert.AreEqual(1, report.SessionCount);
            Assert.AreEqual(3, report.SegmentCount);
            Assert.IsTrue(report.AcceptedTaskLikeSegments >= 1);
            Assert.IsTrue(report.QuestionLikeSegments >= 1);
            Assert.AreEqual(1, report.LikelyNoisyActiveTaskCount);
            Assert.IsTrue(File.Exists(Path.Combine(_tempRoot, "strategy", "voice_recognition_quality.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_tempRoot, "strategy", "voice_recognition_quality.md")));
        }
    }
}
