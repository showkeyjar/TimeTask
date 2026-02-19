using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using TimeTask;

namespace TimeTask.Tests
{
    [TestClass]
    public class UserProfileManagerTests
    {
        private string _tempRoot;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "TimeTaskTests", "UserProfileManager", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
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
        public void GetRankedStuckSuggestions_FeedbackImprovesWinningAction()
        {
            var manager = new UserProfileManager(_tempRoot);

            for (int i = 0; i < 8; i++)
            {
                manager.RecordSuggestionShown("delegate_or_drop");
                manager.RecordSuggestionFeedback("delegate_or_drop", "rejected");
                manager.RecordSuggestionShown("split_20_min");
                manager.RecordSuggestionFeedback("split_20_min", "accepted");
            }

            var task = new ItemGrid
            {
                Task = "write document",
                Importance = "Low",
                Urgency = "High"
            };
            var ranked = manager.GetRankedStuckSuggestions(task, TimeSpan.FromHours(6));

            Assert.IsNotNull(ranked);
            Assert.IsTrue(ranked.Count > 0);
            Assert.AreEqual("split_20_min", ranked[0].Id, "Accepted action should become top recommendation.");
        }

        [TestMethod]
        public void GetAdaptiveNudgeRecommendation_BadQualityTurnsConservative()
        {
            var manager = new UserProfileManager(_tempRoot);

            for (int i = 0; i < 12; i++)
            {
                manager.RecordSuggestionShown("start_10_min");
                manager.RecordSuggestionFeedback("start_10_min", "rejected");
            }

            var recommendation = manager.GetAdaptiveNudgeRecommendation(7);
            Assert.IsNotNull(recommendation);
            Assert.IsTrue(recommendation.RecommendedStuckThresholdMinutes >= 120);
            Assert.AreEqual(1, recommendation.RecommendedDailyNudgeLimit);
        }

        [TestMethod]
        public void GetAdaptiveNudgeRecommendation_ConfidenceIncreasesWithSamples()
        {
            var manager = new UserProfileManager(_tempRoot);

            for (int i = 0; i < 10; i++)
            {
                manager.RecordSuggestionShown("start_10_min");
            }

            var recommendation = manager.GetAdaptiveNudgeRecommendation(7);
            Assert.IsNotNull(recommendation);
            Assert.IsTrue(recommendation.RecommendationConfidence >= 0.5 && recommendation.RecommendationConfidence <= 0.6);
        }
    }
}
