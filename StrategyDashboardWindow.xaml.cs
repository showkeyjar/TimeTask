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
                StatusText.Text = I18n.Tf("Strategy_StatusLoadedFormat", DateTime.Now);
            }
            catch (Exception ex)
            {
                StatusText.Text = I18n.Tf("Strategy_StatusLoadFailedFormat", ex.Message);
            }
        }

        private void LoadLifeProfile()
        {
            string path = Path.Combine(_strategyPath, "user_life_profile.json");
            if (!File.Exists(path))
            {
                ProfileHeadlineText.Text = I18n.T("Strategy_NoProfileData");
                StrengthsList.ItemsSource = new List<string>();
                RisksList.ItemsSource = new List<string>();
                PeakHoursList.ItemsSource = new List<string>();
                return;
            }

            string json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<LifeProfileSnapshot>(json) ?? new LifeProfileSnapshot();
            GeneratedAtText.Text = I18n.Tf("Strategy_LastUpdatedFormat", profile.GeneratedAt);
            ProfileHeadlineText.Text = I18n.Tf("Strategy_ProfileHeadlineFormat", profile.ExecutionReliability, profile.InterruptionSensitivity, profile.ActiveTaskCount);
            StrengthsList.ItemsSource = profile.Strengths?.Any() == true ? profile.Strengths : new List<string> { I18n.T("Strategy_None") };
            RisksList.ItemsSource = profile.RiskTriggers?.Any() == true ? profile.RiskTriggers : new List<string> { I18n.T("Strategy_None") };
            PeakHoursList.ItemsSource = profile.PeakHours?.Any() == true
                ? profile.PeakHours.Select(h => $"{h:D2}:00").ToList()
                : new List<string> { I18n.T("Strategy_None") };
        }

        private void LoadGoalHierarchy()
        {
            string path = Path.Combine(_strategyPath, "goal_hierarchy.json");
            if (!File.Exists(path))
            {
                GoalHierarchySummaryText.Text = I18n.T("Strategy_NoGoalHierarchyData");
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
                GoalHierarchySummaryText.Text = I18n.T("Strategy_NoActiveGoal");
                YearThemesList.ItemsSource = new List<string>();
                QuarterMilestonesList.ItemsSource = new List<string>();
                WeekCommitmentsList.ItemsSource = new List<string>();
                return;
            }

            GoalHierarchySummaryText.Text = $"{first.GoalDescription}（{first.TimeHorizon}）";
            YearThemesList.ItemsSource = first.YearlyThemes?.Any() == true ? first.YearlyThemes : new List<string> { I18n.T("Strategy_None") };
            QuarterMilestonesList.ItemsSource = first.QuarterlyMilestones?.Any() == true ? first.QuarterlyMilestones : new List<string> { I18n.T("Strategy_None") };
            WeekCommitmentsList.ItemsSource = first.WeeklyCommitments?.Any() == true ? first.WeeklyCommitments : new List<string> { I18n.T("Strategy_None") };
        }

        private void LoadDecisionSnapshot()
        {
            string path = Path.Combine(_strategyPath, "decision_focus_snapshot.json");
            if (!File.Exists(path))
            {
                FocusTasksGrid.ItemsSource = new List<StrategyFocusTaskView>();
                ThinkingToolsText.Text = I18n.T("Strategy_ThinkingToolHintWaiting");
                return;
            }

            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var list = new List<StrategyFocusTaskView>();
            var toolHints = new List<string>();
            if (doc.RootElement.TryGetProperty("top", out JsonElement top) && top.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in top.EnumerateArray())
                {
                    string task = item.TryGetProperty("TaskName", out var taskProp) ? taskProp.GetString() : "(未命名)";
                    double score = item.TryGetProperty("Score", out var scoreProp) ? scoreProp.GetDouble() : 0.0;
                    string importance = item.TryGetProperty("Importance", out var importanceProp) ? importanceProp.GetString() : "Unknown";
                    string urgency = item.TryGetProperty("Urgency", out var urgencyProp) ? urgencyProp.GetString() : "Unknown";
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

                    var tools = ThinkingToolAdvisor.RecommendForTask(task, importance, urgency, TimeSpan.Zero, 2);
                    if (tools.Count > 0)
                    {
                        string toolText = string.Join("、", tools.Select(t => t.Title).Distinct().Take(2));
                        toolHints.Add($"- {task}：{toolText}");
                    }
                }
            }

            FocusTasksGrid.ItemsSource = list;
            ThinkingToolsText.Text = toolHints.Count > 0
                ? string.Join(Environment.NewLine, toolHints.Take(4))
                : I18n.T("Strategy_NoSuggestion");
        }

        private void LoadWeeklyReview()
        {
            string weeklyFolder = Path.Combine(_strategyPath, "weekly_reviews");
            if (!Directory.Exists(weeklyFolder))
            {
                WeeklyStrategyText.Text = I18n.T("Strategy_NoWeeklyReview");
                return;
            }

            string latest = Directory.GetFiles(weeklyFolder, "weekly_review_*.json")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(latest))
            {
                WeeklyStrategyText.Text = I18n.T("Strategy_NoWeeklyReview");
                return;
            }

            string json = File.ReadAllText(latest);
            var report = JsonSerializer.Deserialize<WeeklyReviewReport>(json);
            if (report == null)
            {
                WeeklyStrategyText.Text = I18n.T("Strategy_WeeklyReviewReadFailed");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(I18n.Tf("Strategy_PeriodFormat", report.WeekStart, report.WeekEnd));
            sb.AppendLine(I18n.Tf("Strategy_ActiveStuckFormat", report.ActiveTaskCount, report.StuckTaskCount));
            sb.AppendLine();
            sb.AppendLine(I18n.T("Strategy_NextWeekPlanHeader"));
            foreach (var s in report.NextWeekStrategy ?? new List<string>())
            {
                sb.AppendLine($"- {s}");
            }
            WeeklyStrategyText.Text = sb.ToString();
        }
    }
}
