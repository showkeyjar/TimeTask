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
                MessageBox.Show($"加载统计数据时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<ItemGrid> GetAllTasks()
        {
            var allTasks = new List<ItemGrid>();
            string dataPath = Path.Combine(_currentPath, "data");

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
            // 由于我们无法直接知道每个任务属于哪个象限，我们需要从UI获取信息
            // 或者根据重要性和紧急性推断象限
            var quadrantCounts = new Dictionary<string, int>
            {
                {"重要且紧急", 0},
                {"重要不紧急", 0},
                {"不重要但紧急", 0},
                {"不重要不紧急", 0}
            };

            foreach (var task in allTasks)
            {
                string quadrant = DetermineQuadrant(task.Importance, task.Urgency);
                quadrantCounts[quadrant]++;
            }

            int totalTasks = allTasks.Count;
            double q1Percent = totalTasks > 0 ? (double)quadrantCounts["重要且紧急"] / totalTasks * 100 : 0;
            double q2Percent = totalTasks > 0 ? (double)quadrantCounts["重要不紧急"] / totalTasks * 100 : 0;
            double q3Percent = totalTasks > 0 ? (double)quadrantCounts["不重要但紧急"] / totalTasks * 100 : 0;
            double q4Percent = totalTasks > 0 ? (double)quadrantCounts["不重要不紧急"] / totalTasks * 100 : 0;

            Q1Label.Text = $"重要且紧急: {quadrantCounts["重要且紧急"]} ({q1Percent:F1}%)";
            Q2Label.Text = $"重要不紧急: {quadrantCounts["重要不紧急"]} ({q2Percent:F1}%)";
            Q3Label.Text = $"不重要但紧急: {quadrantCounts["不重要但紧急"]} ({q3Percent:F1}%)";
            Q4Label.Text = $"不重要不紧急: {quadrantCounts["不重要不紧急"]} ({q4Percent:F1}%)";

            // 设置进度条的宽度（通过绑定到实际宽度）
            Q1ProgressBar.Width = Q1ProgressBar.ActualWidth * q1Percent / 100;
            Q2ProgressBar.Width = Q2ProgressBar.ActualWidth * q2Percent / 100;
            Q3ProgressBar.Width = Q3ProgressBar.ActualWidth * q3Percent / 100;
            Q4ProgressBar.Width = Q4ProgressBar.ActualWidth * q4Percent / 100;
        }

        private string DetermineQuadrant(string importance, string urgency)
        {
            bool isImportant = importance?.ToLower() == "high" || importance?.ToLower() == "important";
            bool isUrgent = urgency?.ToLower() == "high" || urgency?.ToLower() == "urgent";

            if (isImportant && isUrgent) return "重要且紧急";
            if (isImportant && !isUrgent) return "重要不紧急";
            if (!isImportant && isUrgent) return "不重要但紧急";
            return "不重要不紧急";
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
                AvgCompletionTimeText.Text = $"{avgCompletionTime:F1} 天";
            }
            else
            {
                AvgCompletionTimeText.Text = "无完成任务";
            }

            // 找出最高效的时段（基于完成任务的时间段）
            var completedHours = completedTasks
                .GroupBy(t => t.CompletionTime.Value.Hour)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (completedHours != null)
            {
                MostProductiveTimeText.Text = $"{completedHours.Key} 点 (完成 {completedHours.Count()} 个任务)";
            }
            else
            {
                MostProductiveTimeText.Text = "无完成任务";
            }

            // 计算延期率
            int overdueTasks = allTasks.Count(t => t.IsActive && t.ReminderTime.HasValue && t.ReminderTime.Value < DateTime.Now);
            double delayRate = allTasks.Count > 0 ? (double)overdueTasks / allTasks.Count * 100 : 0;
            DelayRateText.Text = delayRate.ToString("F1") + "%";

            // 推荐改进意见
            if (delayRate > 30)
            {
                RecommendationText.Text = "任务延期率较高，建议重新评估任务优先级和时间安排";
            }
            else if (delayRate > 10)
            {
                RecommendationText.Text = "任务延期率适中，可适当优化时间管理";
            }
            else
            {
                RecommendationText.Text = "任务延期率较低，继续保持良好习惯";
            }

            // 任务类型分析
            UpdateTaskTypeAnalysis(allTasks);
        }

        private void UpdateTaskTypeAnalysis(List<ItemGrid> allTasks)
        {
            TaskTypeAnalysisListBox.Items.Clear();

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
                    Filter = "文本文件|*.txt|所有文件|*.*"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var allTasks = GetAllTasks();
                    
                    var report = GenerateStatisticsReport(allTasks);
                    File.WriteAllText(saveFileDialog.FileName, report);
                    
                    MessageBox.Show("统计报告已成功导出！", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出统计报告时发生错误: {ex.Message}", "导出错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GenerateStatisticsReport(List<ItemGrid> allTasks)
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("任务统计报告");
            report.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            // 概览统计
            report.AppendLine("=== 概览统计 ===");
            report.AppendLine($"总任务数: {allTasks.Count}");
            report.AppendLine($"已完成: {allTasks.Count(t => !t.IsActive)}");
            report.AppendLine($"进行中: {allTasks.Count(t => t.IsActive)}");
            report.AppendLine("完成率: " + (allTasks.Count > 0 ? ((double)allTasks.Count(t => !t.IsActive) / allTasks.Count * 100).ToString("F1") : 0) + "%");
            report.AppendLine();

            // 象限分布
            report.AppendLine("=== 象限分布 ===");
            var quadrantCounts = new Dictionary<string, int>
            {
                {"重要且紧急", 0},
                {"重要不紧急", 0},
                {"不重要但紧急", 0},
                {"不重要不紧急", 0}
            };

            foreach (var task in allTasks)
            {
                string quadrant = DetermineQuadrant(task.Importance, task.Urgency);
                quadrantCounts[quadrant]++;
            }

            int totalTasks = allTasks.Count;
            report.AppendLine("重要且紧急: " + quadrantCounts["重要且紧急"] + " (" + (totalTasks > 0 ? ((double)quadrantCounts["重要且紧急"] / totalTasks * 100).ToString("F1") : 0) + "%)");
            report.AppendLine("重要不紧急: " + quadrantCounts["重要不紧急"] + " (" + (totalTasks > 0 ? ((double)quadrantCounts["重要不紧急"] / totalTasks * 100).ToString("F1") : 0) + "%)");
            report.AppendLine("不重要但紧急: " + quadrantCounts["不重要但紧急"] + " (" + (totalTasks > 0 ? ((double)quadrantCounts["不重要但紧急"] / totalTasks * 100).ToString("F1") : 0) + "%)");
            report.AppendLine("不重要不紧急: " + quadrantCounts["不重要不紧急"] + " (" + (totalTasks > 0 ? ((double)quadrantCounts["不重要不紧急"] / totalTasks * 100).ToString("F1") : 0) + "%)");
            report.AppendLine();

            // 效率分析
            report.AppendLine("=== 效率分析 ===");
            var completedTasks = allTasks.Where(t => !t.IsActive && t.CompletionTime.HasValue);
            if (completedTasks.Any())
            {
                var avgCompletionTime = completedTasks.Average(t => (t.CompletionTime.Value - t.CreatedDate).TotalDays);
                report.AppendLine("平均完成时间: " + avgCompletionTime.ToString("F1") + " 天");
            }
            else
            {
                report.AppendLine("平均完成时间: 无完成任务");
            }

            int overdueTasks = allTasks.Count(t => t.IsActive && t.ReminderTime.HasValue && t.ReminderTime.Value < DateTime.Now);
            double delayRate = allTasks.Count > 0 ? (double)overdueTasks / allTasks.Count * 100 : 0;
            report.AppendLine("任务延期率: " + delayRate.ToString("F1") + "%");
            report.AppendLine();

            return report.ToString();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}