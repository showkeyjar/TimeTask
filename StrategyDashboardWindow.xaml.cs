using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace TimeTask
{
    public class StrategyFocusTaskView
    {
        public string TaskName { get; set; }
        public string Score { get; set; }
        public string ReasonText { get; set; }
    }

    public partial class StrategyDashboardWindow : Window
    {
        private readonly string _strategyPath;

        public StrategyDashboardWindow(string dataPath)
        {
            InitializeComponent();
            _strategyPath = Path.Combine(dataPath, "strategy");
            LoadDashboard();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadDashboard();
        }

        private void LoadDashboard()
        {
            try
            {
                Directory.CreateDirectory(_strategyPath);
                LoadLifeProfile();
                LoadGoalHierarchy();
                LoadDecisionSnapshot();
                LoadWeeklyReview();
                StatusText.Text = $"已加载：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"加载失败: {ex.Message}";
            }
        }

        private void LoadLifeProfile()
        {
            string path = Path.Combine(_strategyPath, "user_life_profile.json");
            if (!File.Exists(path))
            {
                ProfileHeadlineText.Text = "暂无画像数据，等待系统完成策略周期。";
                StrengthsList.ItemsSource = new List<string>();
                RisksList.ItemsSource = new List<string>();
                PeakHoursList.ItemsSource = new List<string>();
                return;
            }

            string json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<LifeProfileSnapshot>(json) ?? new LifeProfileSnapshot();
            GeneratedAtText.Text = $"最后更新：{profile.GeneratedAt:yyyy-MM-dd HH:mm}";
            ProfileHeadlineText.Text = $"执行可靠度 {profile.ExecutionReliability:P0}，打断敏感度 {profile.InterruptionSensitivity:P0}，活跃任务 {profile.ActiveTaskCount}。";
            StrengthsList.ItemsSource = profile.Strengths?.Any() == true ? profile.Strengths : new List<string> { "暂无" };
            RisksList.ItemsSource = profile.RiskTriggers?.Any() == true ? profile.RiskTriggers : new List<string> { "暂无" };
            PeakHoursList.ItemsSource = profile.PeakHours?.Any() == true
                ? profile.PeakHours.Select(h => $"{h:D2}:00").ToList()
                : new List<string> { "暂无" };
        }

        private void LoadGoalHierarchy()
        {
            string path = Path.Combine(_strategyPath, "goal_hierarchy.json");
            if (!File.Exists(path))
            {
                GoalHierarchySummaryText.Text = "暂无目标层级数据。";
                YearThemesList.ItemsSource = new List<string>();
                QuarterMilestonesList.ItemsSource = new List<string>();
                WeekCommitmentsList.ItemsSource = new List<string>();
                return;
            }

            string json = File.ReadAllText(path);
            var hierarchy = JsonSerializer.Deserialize<GoalHierarchySnapshot>(json) ?? new GoalHierarchySnapshot();
            var first = hierarchy.Goals?.FirstOrDefault();
            if (first == null)
            {
                GoalHierarchySummaryText.Text = "当前无活跃长期目标。";
                YearThemesList.ItemsSource = new List<string>();
                QuarterMilestonesList.ItemsSource = new List<string>();
                WeekCommitmentsList.ItemsSource = new List<string>();
                return;
            }

            GoalHierarchySummaryText.Text = $"{first.GoalDescription}（{first.TimeHorizon}）";
            YearThemesList.ItemsSource = first.YearlyThemes?.Any() == true ? first.YearlyThemes : new List<string> { "暂无" };
            QuarterMilestonesList.ItemsSource = first.QuarterlyMilestones?.Any() == true ? first.QuarterlyMilestones : new List<string> { "暂无" };
            WeekCommitmentsList.ItemsSource = first.WeeklyCommitments?.Any() == true ? first.WeeklyCommitments : new List<string> { "暂无" };
        }

        private void LoadDecisionSnapshot()
        {
            string path = Path.Combine(_strategyPath, "decision_focus_snapshot.json");
            if (!File.Exists(path))
            {
                FocusTasksGrid.ItemsSource = new List<StrategyFocusTaskView>();
                return;
            }

            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var list = new List<StrategyFocusTaskView>();
            if (doc.RootElement.TryGetProperty("top", out JsonElement top) && top.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in top.EnumerateArray())
                {
                    string task = item.TryGetProperty("TaskName", out var taskProp) ? taskProp.GetString() : "(未命名)";
                    double score = item.TryGetProperty("Score", out var scoreProp) ? scoreProp.GetDouble() : 0.0;
                    string reason = "待补充";
                    if (item.TryGetProperty("Reasons", out var reasonsProp) && reasonsProp.ValueKind == JsonValueKind.Array)
                    {
                        reason = string.Join("、", reasonsProp.EnumerateArray().Select(r => r.GetString()).Where(r => !string.IsNullOrWhiteSpace(r)).Take(2));
                        if (string.IsNullOrWhiteSpace(reason))
                        {
                            reason = "待补充";
                        }
                    }

                    list.Add(new StrategyFocusTaskView
                    {
                        TaskName = task,
                        Score = score.ToString("F2"),
                        ReasonText = reason
                    });
                }
            }

            FocusTasksGrid.ItemsSource = list;
        }

        private void LoadWeeklyReview()
        {
            string weeklyFolder = Path.Combine(_strategyPath, "weekly_reviews");
            if (!Directory.Exists(weeklyFolder))
            {
                WeeklyStrategyText.Text = "暂无周复盘。";
                return;
            }

            string latest = Directory.GetFiles(weeklyFolder, "weekly_review_*.json")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(latest))
            {
                WeeklyStrategyText.Text = "暂无周复盘。";
                return;
            }

            string json = File.ReadAllText(latest);
            var report = JsonSerializer.Deserialize<WeeklyReviewReport>(json);
            if (report == null)
            {
                WeeklyStrategyText.Text = "周复盘文件读取失败。";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"周期：{report.WeekStart:yyyy-MM-dd} ~ {report.WeekEnd:yyyy-MM-dd}");
            sb.AppendLine($"活跃任务：{report.ActiveTaskCount}，卡住任务：{report.StuckTaskCount}");
            sb.AppendLine();
            sb.AppendLine("下周策略：");
            foreach (var s in report.NextWeekStrategy ?? new List<string>())
            {
                sb.AppendLine($"- {s}");
            }
            WeeklyStrategyText.Text = sb.ToString();
        }
    }
}
