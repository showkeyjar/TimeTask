using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TimeTask
{
    /// <summary>
    /// TaskStatisticsWindow.xaml 的交互逻辑
    /// </summary>
    public partial class TaskStatisticsWindow : Window
    {
        private string _currentPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

        public TaskStatisticsWindow()
        {
            InitializeComponent();
            LoadStatistics();
        }

        private void LoadStatistics()
        {
            try
            {
                // 获取所有CSV文件中的任务数据
                var allTasks = GetAllTasks();

                // 更新概览统计
                UpdateOverviewStats(allTasks);

                // 更新象限分布
                UpdateQuadrantDistribution(allTasks);

                // 更新最近活动
                UpdateRecentActivity(allTasks);

                // 更新效率分析
                UpdateEfficiencyAnalysis(allTasks);
            }
            catch (Exception ex)
            {
                MessageBox.Show(I18n.Tf("TaskStats_ErrorLoadFormat", ex.Message), I18n.T("Title_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<ItemGrid> GetAllTasks()
        {
            var allTasks = new List<ItemGrid>();
            string dataPath = AppPaths.DataDir;

            // 读取所有象限的CSV文件
            for (int i = 1; i <= 4; i++)
            {
                string csvFile = Path.Combine(dataPath, $"{i}.csv");
                if (File.Exists(csvFile))
                {
                    var tasks = HelperClass.ReadCsv(csvFile);
                    if (tasks != null)
                    {
                        allTasks.AddRange(tasks);
                    }
                }
            }

            return allTasks;
        }

        private void UpdateOverviewStats(List<ItemGrid> allTasks)
        {
            int totalTasks = allTasks.Count;
            int completedTasks = allTasks.Count(t => !t.IsActive);
            int activeTasks = allTasks.Count(t => t.IsActive);
            double completionRate = totalTasks > 0 ? (double)completedTasks / totalTasks * 100 : 0;

            TotalTasksText.Text = totalTasks.ToString();
            CompletedTasksText.Text = completedTasks.ToString();
            ActiveTasksText.Text = activeTasks.ToString();
            CompletionRateText.Text = $"{completionRate:F1}%";
        }

        private void UpdateQuadrantDistribution(List<ItemGrid> allTasks)
        {
            // 象限编号 1..4（展示名复用 Quadrant_* 资源键，与四象限主界面口径一致）
            var quadrantCounts = new int[5];

            foreach (var task in allTasks)
            {
                int quadrant = DetermineQuadrant(task.Importance, task.Urgency);
                quadrantCounts[quadrant]++;
            }

            int totalTasks = allTasks.Count;
            var labelNames = new[]
            {
                I18n.T("Quadrant_ImportantUrgent"),
                I18n.T("Quadrant_ImportantNotUrgent"),
                I18n.T("Quadrant_NotImportantUrgent"),
                I18n.T("Quadrant_NotImportantNotUrgent")
            };

            Q1Label.Text = I18n.Tf("TaskStats_QuadrantCountFormat", labelNames[0], quadrantCounts[1], FormatPercent(quadrantCounts[1], totalTasks));
            Q2Label.Text = I18n.Tf("TaskStats_QuadrantCountFormat", labelNames[1], quadrantCounts[2], FormatPercent(quadrantCounts[2], totalTasks));
            Q3Label.Text = I18n.Tf("TaskStats_QuadrantCountFormat", labelNames[2], quadrantCounts[3], FormatPercent(quadrantCounts[3], totalTasks));
            Q4Label.Text = I18n.Tf("TaskStats_QuadrantCountFormat", labelNames[3], quadrantCounts[4], FormatPercent(quadrantCounts[4], totalTasks));

            // 设置进度条的宽度（通过绑定到实际宽度）
            Q1ProgressBar.Width = Q1ProgressBar.ActualWidth * PercentOf(quadrantCounts[1], totalTasks) / 100;
            Q2ProgressBar.Width = Q2ProgressBar.ActualWidth * PercentOf(quadrantCounts[2], totalTasks) / 100;
            Q3ProgressBar.Width = Q3ProgressBar.ActualWidth * PercentOf(quadrantCounts[3], totalTasks) / 100;
            Q4ProgressBar.Width = Q4ProgressBar.ActualWidth * PercentOf(quadrantCounts[4], totalTasks) / 100;
        }

        private static double PercentOf(int count, int total)
        {
            return total > 0 ? (double)count / total * 100 : 0;
        }

        private static string FormatPercent(int count, int total)
        {
            return PercentOf(count, total).ToString("F1");
        }

        /// <summary>按重要/紧急推断象限编号（1..4），展示名走资源键。</summary>
        private int DetermineQuadrant(string importance, string urgency)
        {
            bool isImportant = importance?.ToLower() == "high" || importance?.ToLower() == "important";
            bool isUrgent = urgency?.ToLower() == "high" || urgency?.ToLower() == "urgent";

            if (isImportant && isUrgent) return 1;
            if (isImportant && !isUrgent) return 2;
            if (!isImportant && isUrgent) return 3;
            return 4;
        }

        private void UpdateRecentActivity(List<ItemGrid> allTasks)
        {
            RecentActivityListBox.Items.Clear();

            // 按最后修改日期排序，取最近的10个任务
            var recentTasks = allTasks
                .OrderByDescending(t => t.LastModifiedDate)
                .Take(10);

            foreach (var task in recentTasks)
            {
                var activity = new
                {
                    Description = task.Task,
                    Timestamp = task.LastModifiedDate
                };
                RecentActivityListBox.Items.Add(activity);
            }
        }

        private void UpdateEfficiencyAnalysis(List<ItemGrid> allTasks)
        {
            // 计算平均完成时间（对于已完成的任务）
            var completedTasks = allTasks.Where(t => !t.IsActive && t.CompletionTime.HasValue);
            if (completedTasks.Any())
            {
                var avgCompletionTime = completedTasks.Average(t => (t.CompletionTime.Value - t.CreatedDate).TotalDays);
                AvgCompletionTimeText.Text = I18n.Tf("TaskStats_DaysFormat", avgCompletionTime.ToString("F1"));
            }
            else
            {
                AvgCompletionTimeText.Text = I18n.T("TaskStats_NoCompleted");
            }

            // 找出最高效的时段（基于完成任务的时间段）
            var completedHours = completedTasks
                .GroupBy(t => t.CompletionTime.Value.Hour)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (completedHours != null)
            {
                MostProductiveTimeText.Text = I18n.Tf("TaskStats_ProductiveHourFormat", completedHours.Key, completedHours.Count());
            }
            else
            {
                MostProductiveTimeText.Text = I18n.T("TaskStats_NoCompleted");
            }

            // 计算延期率
            int overdueTasks = allTasks.Count(t => t.IsActive && t.ReminderTime.HasValue && t.ReminderTime.Value < DateTime.Now);
            double delayRate = allTasks.Count > 0 ? (double)overdueTasks / allTasks.Count * 100 : 0;
            DelayRateText.Text = delayRate.ToString("F1") + "%";

            // 推荐改进意见
            if (delayRate > 30)
            {
                RecommendationText.Text = I18n.T("TaskStats_RecommendHigh");
            }
            else if (delayRate > 10)
            {
                RecommendationText.Text = I18n.T("TaskStats_RecommendMedium");
            }
            else
            {
                RecommendationText.Text = I18n.T("TaskStats_RecommendLow");
            }

            // 任务类型分析
            UpdateTaskTypeAnalysis(allTasks);
        }

        private void UpdateTaskTypeAnalysis(List<ItemGrid> allTasks)
        {
            TaskTypeAnalysisListBox.Items.Clear();
            if (allTasks.Count == 0)
            {
                return;
            }

            var typeGroups = allTasks
                .GroupBy(t => t.Importance ?? "Unknown")
                .Select(g => new
                {
                    Category = g.Key,
                    Count = g.Count(),
                    Percentage = $"{(double)g.Count() / allTasks.Count * 100:F1}%"
                })
                .OrderByDescending(g => g.Count);

            foreach (var group in typeGroups)
            {
                TaskTypeAnalysisListBox.Items.Add(group);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadStatistics();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"TaskStatistics_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                    Filter = I18n.T("TaskStats_ExportFilter")
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var allTasks = GetAllTasks();
                    
                    var report = GenerateStatisticsReport(allTasks);
                    File.WriteAllText(saveFileDialog.FileName, report);
                    
                    MessageBox.Show(I18n.T("TaskStats_ExportSuccess"), I18n.T("TaskStats_ExportSuccessTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(I18n.Tf("TaskStats_ExportErrorFormat", ex.Message), I18n.T("TaskStats_ExportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GenerateStatisticsReport(List<ItemGrid> allTasks)
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine(I18n.T("TaskStats_ReportTitle"));
            report.AppendLine(I18n.Tf("TaskStats_ReportGeneratedAtFormat", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            report.AppendLine();

            // 概览统计
            report.AppendLine(I18n.T("TaskStats_ReportOverviewSection"));
            report.AppendLine(I18n.Tf("TaskStats_ReportTotalFormat", allTasks.Count));
            report.AppendLine(I18n.Tf("TaskStats_ReportCompletedFormat", allTasks.Count(t => !t.IsActive)));
            report.AppendLine(I18n.Tf("TaskStats_ReportActiveFormat", allTasks.Count(t => t.IsActive)));
            report.AppendLine(I18n.Tf("TaskStats_ReportCompletionRateFormat", allTasks.Count > 0 ? ((double)allTasks.Count(t => !t.IsActive) / allTasks.Count * 100).ToString("F1") : "0"));
            report.AppendLine();

            // 象限分布
            report.AppendLine(I18n.T("TaskStats_ReportQuadrantSection"));
            var quadrantCounts = new int[5];

            foreach (var task in allTasks)
            {
                int quadrant = DetermineQuadrant(task.Importance, task.Urgency);
                quadrantCounts[quadrant]++;
            }

            int totalTasks = allTasks.Count;
            var labelNames = new[]
            {
                I18n.T("Quadrant_ImportantUrgent"),
                I18n.T("Quadrant_ImportantNotUrgent"),
                I18n.T("Quadrant_NotImportantUrgent"),
                I18n.T("Quadrant_NotImportantNotUrgent")
            };
            for (int q = 1; q <= 4; q++)
            {
                report.AppendLine(I18n.Tf("TaskStats_QuadrantCountFormat", labelNames[q - 1], quadrantCounts[q], FormatPercent(quadrantCounts[q], totalTasks)));
            }
            report.AppendLine();

            // 效率分析
            report.AppendLine(I18n.T("TaskStats_ReportEfficiencySection"));
            var completedTasks = allTasks.Where(t => !t.IsActive && t.CompletionTime.HasValue);
            if (completedTasks.Any())
            {
                var avgCompletionTime = completedTasks.Average(t => (t.CompletionTime.Value - t.CreatedDate).TotalDays);
                report.AppendLine(I18n.Tf("TaskStats_ReportAvgTimeFormat", avgCompletionTime.ToString("F1")));
            }
            else
            {
                report.AppendLine(I18n.T("TaskStats_ReportAvgTimeNone"));
            }

            int overdueTasks = allTasks.Count(t => t.IsActive && t.ReminderTime.HasValue && t.ReminderTime.Value < DateTime.Now);
            double delayRate = allTasks.Count > 0 ? (double)overdueTasks / allTasks.Count * 100 : 0;
            report.AppendLine(I18n.Tf("TaskStats_ReportDelayRateFormat", delayRate.ToString("F1")));
            report.AppendLine();

            return report.ToString();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
