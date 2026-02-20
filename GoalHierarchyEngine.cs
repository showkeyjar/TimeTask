using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TimeTask
{
    public class GoalHierarchyItem
    {
        public string GoalId { get; set; }
        public string GoalDescription { get; set; }
        public string TimeHorizon { get; set; }
        public List<string> YearlyThemes { get; set; } = new List<string>();
        public List<string> QuarterlyMilestones { get; set; } = new List<string>();
        public List<string> WeeklyCommitments { get; set; } = new List<string>();
    }

    public class GoalHierarchySnapshot
    {
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public List<GoalHierarchyItem> Goals { get; set; } = new List<GoalHierarchyItem>();
    }

    public class GoalHierarchyEngine
    {
        private readonly string _filePath;
        private readonly object _sync = new object();

        public GoalHierarchyEngine(string dataPath)
        {
            string strategyPath = Path.Combine(dataPath, "strategy");
            Directory.CreateDirectory(strategyPath);
            _filePath = Path.Combine(strategyPath, "goal_hierarchy.json");
        }

        public GoalHierarchySnapshot BuildAndPersist(List<LongTermGoal> activeGoals, List<ItemGrid> allTasks, DateTime now)
        {
            lock (_sync)
            {
                var snapshot = Build(activeGoals, allTasks, now);
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, options));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"GoalHierarchyEngine persist failed: {ex.Message}");
                }
                return snapshot;
            }
        }

        private static GoalHierarchySnapshot Build(List<LongTermGoal> activeGoals, List<ItemGrid> allTasks, DateTime now)
        {
            var goals = activeGoals ?? new List<LongTermGoal>();
            var tasks = allTasks ?? new List<ItemGrid>();
            var result = new GoalHierarchySnapshot { GeneratedAt = now };

            foreach (var goal in goals.Where(g => g != null && g.IsActive))
            {
                var related = tasks
                    .Where(t => t != null && t.IsActive && string.Equals(t.LongTermGoalId, goal.Id, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => t.OriginalScheduledDay > 0 ? t.OriginalScheduledDay : int.MaxValue)
                    .ThenBy(t => t.CreatedDate)
                    .ToList();

                int durationDays = ParseDurationDays(goal.TotalDuration);
                string horizon = durationDays <= 120 ? "1年内目标" : durationDays <= 365 ? "1-3年目标" : "3年以上目标";

                var yearlyThemes = BuildYearlyThemes(goal, related);
                var milestones = BuildQuarterlyMilestones(related);
                var weekly = related.Take(5).Select(t => t.Task).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();

                result.Goals.Add(new GoalHierarchyItem
                {
                    GoalId = goal.Id,
                    GoalDescription = goal.Description,
                    TimeHorizon = horizon,
                    YearlyThemes = yearlyThemes,
                    QuarterlyMilestones = milestones,
                    WeeklyCommitments = weekly
                });
            }

            return result;
        }

        private static List<string> BuildYearlyThemes(LongTermGoal goal, List<ItemGrid> related)
        {
            var themes = new List<string>();
            if (goal != null && goal.IsLearningPlan)
            {
                themes.Add("能力建设与系统学习");
            }

            int highImportant = related.Count(t => string.Equals(t.Importance, "High", StringComparison.OrdinalIgnoreCase));
            if (highImportant >= Math.Max(1, related.Count / 2))
            {
                themes.Add("高价值任务优先推进");
            }

            var keywordThemes = related
                .SelectMany(t => (t.Task ?? string.Empty).Split(new[] { ' ', ',', '，', '。', ';', '；', '-', '_', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(w => w.Length >= 2)
                .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(2)
                .Select(g => $"围绕「{g.Key}」持续深化")
                .ToList();
            themes.AddRange(keywordThemes);

            if (!themes.Any())
            {
                themes.Add("聚焦核心目标，持续推进关键里程碑");
            }
            return themes.Distinct().Take(4).ToList();
        }

        private static List<string> BuildQuarterlyMilestones(List<ItemGrid> related)
        {
            var result = new List<string>();
            if (related == null || !related.Any())
            {
                return result;
            }

            var grouped = related
                .Select(t =>
                {
                    int day = t.OriginalScheduledDay > 0 ? t.OriginalScheduledDay : 90;
                    int q = Math.Min(4, Math.Max(1, ((day - 1) / 30) + 1));
                    return new { Quarter = q, Task = t.Task };
                })
                .GroupBy(x => x.Quarter)
                .OrderBy(g => g.Key);

            foreach (var g in grouped)
            {
                string picks = string.Join("、", g.Select(x => x.Task).Where(t => !string.IsNullOrWhiteSpace(t)).Take(2));
                if (string.IsNullOrWhiteSpace(picks))
                {
                    continue;
                }
                result.Add($"Q{g.Key}: 完成 {picks}");
            }
            return result.Take(8).ToList();
        }

        private static int ParseDurationDays(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 90;
            }

            var match = Regex.Match(text, @"\d+");
            if (!match.Success || !int.TryParse(match.Value, out int value))
            {
                return 90;
            }

            if (text.Contains("年")) return value * 365;
            if (text.Contains("月")) return value * 30;
            if (text.Contains("周")) return value * 7;
            return value;
        }
    }
}
