using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using TimeTask;

namespace TimeTask.Tests
{
    [TestClass]
    public class ThinkingToolAdvisorTests
    {
        [TestMethod]
        public void GetAllowedSkillIds_IncludesClassicThinkingTools()
        {
            var ids = ThinkingToolAdvisor.GetAllowedSkillIds();
            Assert.IsTrue(ids.Contains("five_whys"));
            Assert.IsTrue(ids.Contains("first_principles"));
            Assert.IsTrue(ids.Contains("pareto_80_20"));
            Assert.IsTrue(ids.Contains("swot_scan"));
            Assert.IsTrue(ids.Contains("premortem"));
            Assert.IsTrue(ids.Contains("ooda_loop"));
            Assert.IsTrue(ids.Contains("smart_goal"));
            Assert.IsTrue(ids.Contains("cost_benefit"));
        }

        [TestMethod]
        public void RecommendForTask_BugScenario_PrefersRootCauseTool()
        {
            var result = ThinkingToolAdvisor.RecommendForTask(
                "线上故障反复出现，需要尽快修复并避免复发",
                "High",
                "High",
                TimeSpan.FromDays(1),
                3);

            Assert.IsTrue(result.Any(r => r.SkillId == "five_whys"));
        }

        [TestMethod]
        public void RecommendForTask_StrategyScenario_CanSuggestSwotAndCostBenefit()
        {
            var result = ThinkingToolAdvisor.RecommendForTask(
                "评估新市场进入策略并比较不同商业模式投入产出",
                "High",
                "Low",
                TimeSpan.FromDays(0),
                4);

            Assert.IsTrue(result.Any(r => r.SkillId == "swot_scan" || r.SkillId == "cost_benefit"));
        }

        [TestMethod]
        public void AnalyzeTask_FiveWhys_ReturnsStructuredReportWithActions()
        {
            var report = ThinkingToolAdvisor.AnalyzeTask(
                "线上支付故障反复出现",
                "High",
                "High",
                TimeSpan.FromDays(2),
                "five_whys");

            Assert.IsNotNull(report);
            Assert.AreEqual("five_whys", report.SkillId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.Diagnostic));
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.DecisionRule));
            Assert.IsTrue(report.Risks.Count >= 1);
            Assert.IsTrue(report.Actions.Count >= 2);
        }
    }
}
