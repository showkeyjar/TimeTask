using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TimeTask
{
    public class DecisionEngineOptions
    {
        public double LongTermWeight { get; set; } = 0.45;
        public double UrgencyWeight { get; set; } = 0.35;
        public double StrengthFitWeight { get; set; } = 1.0;
        public double EnergyFitWeight { get; set; } = 1.0;
        public double RiskPenaltyWeight { get; set; } = 1.0;
        public int SnapshotTopCount { get; set; } = 5;
    }

    public class TaskDecisionScore
    {
        public string TaskName { get; set; }
        public string GoalId { get; set; }
        public string Importance { get; set; }
        public string Urgency { get; set; }
        public double Score { get; set; }
        public double LongTermValue { get; set; }
        public double UrgencyValue { get; set; }
        public double StrengthFit { get; set; }
        public double EnergyFit { get; set; }
        public double RiskPenalty { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    public class DecisionEngine
    {
        private readonly string _decisionPath;
        private readonly object _sync = new object();
        private DecisionEngineOptions _options;

        public DecisionEngine(string dataPath, DecisionEngineOptions options = null)
        {
            string strategyPath = Path.Combine(dataPath, "strategy");
            Directory.CreateDirectory(strategyPath);
            _decisionPath = Path.Combine(strategyPath, "decision_focus_snapshot.json");
            _options = options ?? new DecisionEngineOptions();
        }

        public void SetOptions(DecisionEngineOptions options)
        {
            _options = options ?? new DecisionEngineOptions();
        }

        public List<TaskDecisionScore> RankTasks(List<ItemGrid> tasks, LifeProfileSnapshot lifeProfile, string activeGoalId, DateTime now)
        {
            tasks ??= new List<ItemGrid>();
            var activeTasks = tasks.Where(t => t != null && t.IsActive).ToList();
            lifeProfile ??= new LifeProfileSnapshot();

            var ranked = activeTasks
                .Select(t => BuildScore(t, lifeProfile, activeGoalId, now, _options))
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.TaskName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ranked;
        }

        public string BuildFocusBrief(List<TaskDecisionScore> ranked, int topN = 3)
        {
            if (ranked == null || ranked.Count == 0)
            {
                return "当前没有可聚焦的活跃任务。";
            }

            int take = Math.Max(1, Math.Min(topN, ranked.Count));
            var top = ranked.Take(take).ToList();
            string head = string.Join("、", top.Select(t => t.TaskName));
            return $"本周期建议聚焦：{head}";
        }

        public void PersistSnapshot(List<TaskDecisionScore> ranked, DateTime now)
        {
            if (ranked == null)
            {
                return;
            }

            lock (_sync)
            {
                try
                {
                    var payload = new
                    {
                        generatedAt = now,
                        top = ranked.Take(Math.Max(1, _options.SnapshotTopCount)).ToList()
                    };
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(_decisionPath, JsonSerializer.Serialize(payload, options));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DecisionEngine persist failed: {ex.Message}");
                }
            }
        }

        private static TaskDecisionScore BuildScore(ItemGrid task, LifeProfileSnapshot lifeProfile, string activeGoalId, DateTime now, DecisionEngineOptions options)
        {
            options ??= new DecisionEngineOptions();
            double longTerm = 0.15;
            if (!string.IsNullOrWhiteSpace(task.LongTermGoalId))
            {
                longTerm += 0.55;
                if (string.Equals(task.LongTermGoalId, activeGoalId, StringComparison.OrdinalIgnoreCase))
                {
                    longTerm += 0.15;
                }
            }

            bool highImportance = string.Equals(task.Importance, "High", StringComparison.OrdinalIgnoreCase);
            bool highUrgency = string.Equals(task.Urgency, "High", StringComparison.OrdinalIgnoreCase);
            double urgency = (highImportance, highUrgency) switch
            {
                (true, true) => 0.65,
                (true, false) => 0.55,
                (false, true) => 0.45,
                _ => 0.25
            };

            bool hasGoal = !string.IsNullOrWhiteSpace(task.LongTermGoalId);
            bool strengthGoalOrientation = (lifeProfile.Strengths ?? new List<string>())
                .Any(s => string.Equals(s, "goal_oriented", StringComparison.OrdinalIgnoreCase));
            double strengthFit = hasGoal && strengthGoalOrientation ? 0.15 : 0.0;

            int currentHour = now.Hour;
            var peakHours = lifeProfile.PeakHours ?? new List<int>();
            bool inPeakWindow = peakHours.Any(h => Math.Abs(h - currentHour) <= 1);
            double energyFit = inPeakWindow ? 0.12 : 0.02;

            double riskPenalty = 0.0;
            if (task.LastProgressDate < now.AddDays(-3))
            {
                riskPenalty += 0.1;
            }
            if (!hasGoal && !highUrgency)
            {
                riskPenalty += 0.08;
            }
            if ((lifeProfile.RiskTriggers ?? new List<string>())
                .Any(s => string.Equals(s, "urgency_overload", StringComparison.OrdinalIgnoreCase)) &&
                highUrgency && !hasGoal)
            {
                riskPenalty += 0.08;
            }

            double score = (longTerm * options.LongTermWeight)
                + (urgency * options.UrgencyWeight)
                + (strengthFit * options.StrengthFitWeight)
                + (energyFit * options.EnergyFitWeight)
                - (riskPenalty * options.RiskPenaltyWeight);

            var reasons = new List<string>();
            if (hasGoal) reasons.Add("绑定长期目标");
            if (highImportance && highUrgency) reasons.Add("重要且紧急");
            else if (highImportance) reasons.Add("重要性高");
            else if (highUrgency) reasons.Add("紧急性高");
            if (inPeakWindow) reasons.Add("匹配高效时段");
            if (riskPenalty > 0.12) reasons.Add("存在执行风险");

            return new TaskDecisionScore
            {
                TaskName = task.Task ?? "(未命名任务)",
                GoalId = task.LongTermGoalId,
                Importance = task.Importance,
                Urgency = task.Urgency,
                Score = Math.Round(score, 4),
                LongTermValue = Math.Round(longTerm, 4),
                UrgencyValue = Math.Round(urgency, 4),
                StrengthFit = Math.Round(strengthFit, 4),
                EnergyFit = Math.Round(energyFit, 4),
                RiskPenalty = Math.Round(riskPenalty, 4),
                Reasons = reasons
            };
        }
    }
}
