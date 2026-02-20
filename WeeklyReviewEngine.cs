using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TimeTask
{
    public class WeeklyReviewReport
    {
        public string WeekKey { get; set; }
        public DateTime WeekStart { get; set; }
        public DateTime WeekEnd { get; set; }
        public int ActiveTaskCount { get; set; }
        public int CompletedThisWeek { get; set; }
        public int CreatedThisWeek { get; set; }
        public int StuckTaskCount { get; set; }
        public int GoalLinkedActiveCount { get; set; }
        public List<string> Wins { get; set; } = new List<string>();
        public List<string> Risks { get; set; } = new List<string>();
        public List<string> NextWeekStrategy { get; set; } = new List<string>();
        public List<TaskDecisionScore> TopFocusTasks { get; set; } = new List<TaskDecisionScore>();
    }

    public class WeeklyReviewEngine
    {
        private readonly string _weeklyPath;
        private readonly object _sync = new object();

        public WeeklyReviewEngine(string dataPath)
        {
            _weeklyPath = Path.Combine(dataPath, "strategy", "weekly_reviews");
            Directory.CreateDirectory(_weeklyPath);
        }

        public WeeklyReviewReport GenerateAndPersist(
            List<ItemGrid> allTasks,
            List<LongTermGoal> goals,
            LifeProfileSnapshot lifeProfile,
            List<TaskDecisionScore> rankedTasks,
            DateTime now,
            bool force = false)
        {
            lock (_sync)
            {
                DateTime weekStart = StartOfWeek(now.Date, DayOfWeek.Monday);
                DateTime weekEnd = weekStart.AddDays(6);
                var calendar = CultureInfo.InvariantCulture.Calendar;
                int weekNumber = calendar.GetWeekOfYear(weekStart, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                int weekYear = weekStart.AddDays(3).Year;
                string weekKey = $"{weekYear}-W{weekNumber:D2}";
                string weekJsonPath = Path.Combine(_weeklyPath, $"weekly_review_{weekKey}.json");
                string weekMdPath = Path.Combine(_weeklyPath, $"weekly_review_{weekKey}.md");

                if (!force && File.Exists(weekJsonPath))
                {
                    return null;
                }

                var tasks = allTasks ?? new List<ItemGrid>();
                var activeTasks = tasks.Where(t => t != null && t.IsActive).ToList();
                var completedThisWeek = tasks.Count(t => t != null && !t.IsActive && t.LastModifiedDate >= weekStart && t.LastModifiedDate <= weekEnd.AddDays(1));
                var createdThisWeek = tasks.Count(t => t != null && t.CreatedDate >= weekStart && t.CreatedDate <= weekEnd.AddDays(1));
                var stuckTasks = activeTasks.Count(t => t.LastProgressDate < now.AddDays(-3));
                var goalLinked = activeTasks.Count(t => !string.IsNullOrWhiteSpace(t.LongTermGoalId));

                var wins = new List<string>();
                if (completedThisWeek >= 5) wins.Add("本周完成任务数量稳定，执行动能良好。");
                if (goalLinked >= Math.Max(2, activeTasks.Count / 2)) wins.Add("多数活跃任务已连接长期目标。");
                if ((lifeProfile?.ExecutionReliability ?? 0) >= 0.65) wins.Add("行为数据反映出较高执行可靠度。");
                if (!wins.Any()) wins.Add("已保持持续执行，建议下周进一步提升关键任务占比。");

                var risks = new List<string>();
                if (stuckTasks >= Math.Max(2, activeTasks.Count / 2)) risks.Add("卡住任务占比偏高，需快速拆解或取舍。");
                if (goalLinked < Math.Max(1, activeTasks.Count / 3)) risks.Add("长期目标关联任务偏少，存在短期事务牵引风险。");
                if ((lifeProfile?.InterruptionSensitivity ?? 0) >= 0.55) risks.Add("打断敏感度较高，建议减少临时提醒。");
                if (!risks.Any()) risks.Add("整体风险可控，保持当前节奏。");

                var nextWeek = new List<string>();
                if (rankedTasks != null && rankedTasks.Any())
                {
                    nextWeek.Add($"优先推进前3项：{string.Join("、", rankedTasks.Take(3).Select(t => t.TaskName))}");
                }
                nextWeek.Add(stuckTasks > 0 ? "为每个卡住任务定义一个30分钟可执行动作。" : "维持高价值任务的连续推进。");
                nextWeek.Add(goalLinked < Math.Max(1, activeTasks.Count / 3) ? "新增至少1个与长期目标绑定的关键任务。" : "继续围绕长期目标安排周计划。");

                var report = new WeeklyReviewReport
                {
                    WeekKey = weekKey,
                    WeekStart = weekStart,
                    WeekEnd = weekEnd,
                    ActiveTaskCount = activeTasks.Count,
                    CompletedThisWeek = completedThisWeek,
                    CreatedThisWeek = createdThisWeek,
                    StuckTaskCount = stuckTasks,
                    GoalLinkedActiveCount = goalLinked,
                    Wins = wins,
                    Risks = risks,
                    NextWeekStrategy = nextWeek,
                    TopFocusTasks = (rankedTasks ?? new List<TaskDecisionScore>()).Take(5).ToList()
                };

                Persist(weekJsonPath, weekMdPath, report, goals);
                return report;
            }
        }

        private static DateTime StartOfWeek(DateTime date, DayOfWeek startDay)
        {
            int diff = (7 + (date.DayOfWeek - startDay)) % 7;
            return date.AddDays(-diff);
        }

        private static void Persist(string jsonPath, string mdPath, WeeklyReviewReport report, List<LongTermGoal> goals)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, options));

                var sb = new StringBuilder();
                sb.AppendLine($"# 周复盘 {report.WeekKey}");
                sb.AppendLine();
                sb.AppendLine($"- 周期: {report.WeekStart:yyyy-MM-dd} ~ {report.WeekEnd:yyyy-MM-dd}");
                sb.AppendLine($"- 活跃任务: {report.ActiveTaskCount}");
                sb.AppendLine($"- 本周完成: {report.CompletedThisWeek}");
                sb.AppendLine($"- 本周新增: {report.CreatedThisWeek}");
                sb.AppendLine($"- 卡住任务: {report.StuckTaskCount}");
                sb.AppendLine($"- 目标关联活跃任务: {report.GoalLinkedActiveCount}");
                sb.AppendLine();
                sb.AppendLine("## 本周亮点");
                foreach (var win in report.Wins)
                {
                    sb.AppendLine($"- {win}");
                }
                sb.AppendLine();
                sb.AppendLine("## 风险提示");
                foreach (var risk in report.Risks)
                {
                    sb.AppendLine($"- {risk}");
                }
                sb.AppendLine();
                sb.AppendLine("## 下周策略");
                foreach (var item in report.NextWeekStrategy)
                {
                    sb.AppendLine($"- {item}");
                }
                sb.AppendLine();
                sb.AppendLine("## 下周聚焦任务");
                foreach (var t in report.TopFocusTasks)
                {
                    sb.AppendLine($"- {t.TaskName}（评分 {t.Score:F2}）");
                }
                if (goals != null && goals.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine("## 活跃长期目标");
                    foreach (var g in goals.Where(g => g != null && g.IsActive))
                    {
                        sb.AppendLine($"- {g.Description}");
                    }
                }

                File.WriteAllText(mdPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WeeklyReviewEngine persist failed: {ex.Message}");
            }
        }
    }
}
