using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TimeTask.Tests
{
    [TestClass]
    public class IntentRecognizerTests
    {
        [TestMethod]
        public void ScoreTaskLikelihood_TaskSentence_ShouldBeHigh()
        {
            var recognizer = new IntentRecognizer();
            double score = recognizer.ScoreTaskLikelihood("提醒我今天下班前提交周报");
            Assert.IsTrue(score >= 0.55, $"Expected >= 0.55, actual={score:F2}");
        }

        [TestMethod]
        public void ScoreTaskLikelihood_Chitchat_ShouldBeLow()
        {
            var recognizer = new IntentRecognizer();
            double score = recognizer.ScoreTaskLikelihood("今天天气不错，晚上看电影吧");
            Assert.IsTrue(score < 0.55, $"Expected < 0.55, actual={score:F2}");
        }

        [TestMethod]
        public void EstimatePriority_UrgentAndImportant_ShouldReturnHighHigh()
        {
            var recognizer = new IntentRecognizer();
            var (importance, urgency) = recognizer.EstimatePriority("这个生产 bug 很关键，今天必须修复");

            Assert.AreEqual("High", importance);
            Assert.AreEqual("High", urgency);
        }

        [TestMethod]
        public void EstimateQuadrant_HighLow_ShouldBeImportantNotUrgent()
        {
            var recognizer = new IntentRecognizer();
            string quadrant = recognizer.EstimateQuadrant("High", "Low");
            Assert.AreEqual("重要不紧急", quadrant);
        }
    }
}
