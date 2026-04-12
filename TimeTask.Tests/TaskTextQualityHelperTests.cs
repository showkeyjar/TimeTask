using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TimeTask.Tests
{
    [TestClass]
    public class TaskTextQualityHelperTests
    {
        [TestMethod]
        public void IsMeaningfulTaskText_ActionableSentence_ShouldReturnTrue()
        {
            Assert.IsTrue(TaskTextQualityHelper.IsMeaningfulTaskText("现在给他打电话问"));
        }

        [TestMethod]
        public void IsMeaningfulTaskText_ChitchatAndFiller_ShouldReturnFalse()
        {
            Assert.IsFalse(TaskTextQualityHelper.IsMeaningfulTaskText("对啊"));
            Assert.IsFalse(TaskTextQualityHelper.IsMeaningfulTaskText("帮我查一下"));
            Assert.IsFalse(TaskTextQualityHelper.IsMeaningfulTaskText("这个软件"));
        }

        [TestMethod]
        public void AnalyzeVoiceTaskCandidate_QuestionLikeLongSpeech_ShouldBeRejected()
        {
            var analysis = TaskTextQualityHelper.AnalyzeVoiceTaskCandidate("为什么发挥市糖仪要当这个调情锅他之前因为没有没得格式国家给国家弄");

            Assert.IsFalse(analysis.IsMeaningfulTask);
            Assert.IsTrue(analysis.IsQuestionLike || analysis.IsLongFreeformSpeech);
        }

        [TestMethod]
        public void AnalyzeVoiceTaskCandidate_ReminderSentence_ShouldBeAccepted()
        {
            var analysis = TaskTextQualityHelper.AnalyzeVoiceTaskCandidate("提醒我明天下午三点给客户提交周报");

            Assert.IsTrue(analysis.IsMeaningfulTask);
            Assert.IsTrue(analysis.HasTimeExpression);
            Assert.IsTrue(analysis.IsReminderCandidate);
        }

        [TestMethod]
        public void ExtractKeywords_ShouldSkipPriorityAndFillerWords()
        {
            var keywords = TaskTextQualityHelper.ExtractKeywords("High Medium 现在给他打电话问户口这个");

            CollectionAssert.DoesNotContain(keywords, "High");
            CollectionAssert.DoesNotContain(keywords, "Medium");
            CollectionAssert.DoesNotContain(keywords, "现在");
            CollectionAssert.Contains(keywords, "打电话");
        }
    }
}
