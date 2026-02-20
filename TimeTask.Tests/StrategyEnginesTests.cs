using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TimeTask;

namespace TimeTask.Tests
{
    [TestClass]
    public class StrategyEnginesTests
    {
        private string _tempRoot;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "TimeTaskTests", "StrategyEngines", Guid.NewGuid().ToString("N"));
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
        public void DecisionEngine_RankTasks_PrioritizesGoalLinkedImportantTask()
        {
            var engine = new DecisionEngine(_tempRoot);
            var life = new LifeProfileSnapshot
            {
                Strengths = new List<string> { "goal_oriented" },
                PeakHours = new List<int> { DateTime.Now.Hour }
            };

            var tasks = new List<ItemGrid>
            {
                new ItemGrid { Task = "闲杂低价值", IsActive = true, Importance = "Low", Urgency = "Low", LastProgressDate = DateTime.Now },
                new ItemGrid { Task = "推进核心里程碑", IsActive = true, Importance = "High", Urgency = "High", LongTermGoalId = "g1", LastProgressDate = DateTime.Now }
            };

            var ranked = engine.RankTasks(tasks, life, "g1", DateTime.Now);

            Assert.IsNotNull(ranked);
            Assert.AreEqual(2, ranked.Count);
            Assert.AreEqual("推进核心里程碑", ranked[0].TaskName);
        }

        [TestMethod]
        public void WeeklyReviewEngine_GenerateAndPersist_CreatesReportFiles()
        {
            var engine = new WeeklyReviewEngine(_tempRoot);
            var now = DateTime.Now;
            var tasks = new List<ItemGrid>
            {
                new ItemGrid { Task = "A", IsActive = true, CreatedDate = now.AddDays(-1), LastProgressDate = now.AddDays(-4) },
                new ItemGrid { Task = "B", IsActive = false, CreatedDate = now.AddDays(-2), LastModifiedDate = now.AddDays(-1) }
            };
            var goals = new List<LongTermGoal>
            {
                new LongTermGoal { Id = "g1", Description = "年度目标", IsActive = true }
            };
            var ranked = new List<TaskDecisionScore>
            {
                new TaskDecisionScore { TaskName = "A", Score = 0.92 }
            };

            var report = engine.GenerateAndPersist(tasks, goals, new LifeProfileSnapshot(), ranked, now, force: true);

            Assert.IsNotNull(report);
            string folder = Path.Combine(_tempRoot, "strategy", "weekly_reviews");
            Assert.IsTrue(Directory.Exists(folder));
            Assert.IsTrue(Directory.GetFiles(folder, "*.json").Any());
            Assert.IsTrue(Directory.GetFiles(folder, "*.md").Any());
        }

        [TestMethod]
        public void GoalHierarchyEngine_BuildAndPersist_CreatesHierarchyJson()
        {
            var engine = new GoalHierarchyEngine(_tempRoot);
            var goals = new List<LongTermGoal>
            {
                new LongTermGoal
                {
                    Id = "g1",
                    Description = "打造个人产品",
                    TotalDuration = "6个月",
                    IsActive = true
                }
            };
            var tasks = new List<ItemGrid>
            {
                new ItemGrid { Task = "完成用户访谈", IsActive = true, LongTermGoalId = "g1", OriginalScheduledDay = 10 },
                new ItemGrid { Task = "发布MVP版本", IsActive = true, LongTermGoalId = "g1", OriginalScheduledDay = 45 }
            };

            var snapshot = engine.BuildAndPersist(goals, tasks, DateTime.Now);

            Assert.IsNotNull(snapshot);
            Assert.AreEqual(1, snapshot.Goals.Count);
            string file = Path.Combine(_tempRoot, "strategy", "goal_hierarchy.json");
            Assert.IsTrue(File.Exists(file));
        }

        [TestMethod]
        public void DecisionEngine_CustomWeights_CanPromoteUrgentTask()
        {
            var engine = new DecisionEngine(_tempRoot, new DecisionEngineOptions
            {
                LongTermWeight = 0.1,
                UrgencyWeight = 1.2,
                StrengthFitWeight = 1.0,
                EnergyFitWeight = 1.0,
                RiskPenaltyWeight = 1.0,
                SnapshotTopCount = 5
            });

            var life = new LifeProfileSnapshot();
            var tasks = new List<ItemGrid>
            {
                new ItemGrid { Task = "目标任务", IsActive = true, Importance = "High", Urgency = "Low", LongTermGoalId = "g1", LastProgressDate = DateTime.Now },
                new ItemGrid { Task = "紧急救火", IsActive = true, Importance = "High", Urgency = "High", LastProgressDate = DateTime.Now }
            };

            var ranked = engine.RankTasks(tasks, life, "g1", DateTime.Now);

            Assert.IsNotNull(ranked);
            Assert.AreEqual(2, ranked.Count);
            Assert.AreEqual("紧急救火", ranked[0].TaskName);
        }
    }
}
