using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace TimeTask
{
    public partial class LearningPlanManagerWindow : Window
    {
        private LongTermGoal _currentPlan;
        private ObservableCollection<LearningMilestone> _milestones;
        private string _appDataPath;
        private bool _dataChanged = false;

        public LearningPlanManagerWindow(LongTermGoal plan, string appDataPath)
        {
            InitializeComponent();
            _currentPlan = plan;
            _appDataPath = appDataPath;
            _milestones = new ObservableCollection<LearningMilestone>();

            DisplayPlanInfo();
            LoadMilestones();
        }

        private void DisplayPlanInfo()
        {
            if (_currentPlan == null) return;

            SubjectTextBlock.Text = _currentPlan.Subject;
            GoalTextBlock.Text = _currentPlan.Description;
            DurationTextBlock.Text = $"学习时长: {_currentPlan.TotalDuration}";

            double progress = _currentPlan.ProgressPercentage;
            ProgressBar.Value = progress;
            ProgressTextBlock.Text = $"{progress:F1}%";
            ProgressDetailTextBlock.Text = $"{_currentPlan.CompletedStages}/{_currentPlan.TotalStages} 阶段";
        }

        private void LoadMilestones()
        {
            _milestones.Clear();
            if (_currentPlan == null) return;

            string milestonesCsvPath = Path.Combine(_appDataPath, $"learning_milestones_{_currentPlan.Id}.csv");
            if (File.Exists(milestonesCsvPath))
            {
                var allMilestones = HelperClass.ReadLearningMilestonesCsv(milestonesCsvPath);
                var planMilestones = allMilestones.Where(m => m.LearningPlanId == _currentPlan.Id)
                                                   .OrderBy(m => m.StageNumber)
                                                   .ToList();

                foreach (var milestone in planMilestones)
                {
                    _milestones.Add(milestone);
                }
            }

            MilestonesDataGrid.ItemsSource = _milestones;
            MilestoneCountTextBlock.Text = $"({_milestones.Count} 个里程碑)";
        }

        private void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            if (MilestonesDataGrid.SelectedItem == null)
            {
                MessageBox.Show("请先选择一个里程碑", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedMilestone = MilestonesDataGrid.SelectedItem as LearningMilestone;
            if (selectedMilestone == null) return;

            if (selectedMilestone.IsCompleted)
            {
                MessageBox.Show("该里程碑已完成，无需再次激活", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"是否将 '{selectedMilestone.StageName}' 添加到任务列表？\n\n描述: {selectedMilestone.Description}",
                "确认激活",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                AddMilestoneToTasks(selectedMilestone);
            }
        }

        private void AddMilestoneToTasks(LearningMilestone milestone)
        {
            string targetCsv = "2.csv";
            string filePath = Path.Combine(_appDataPath, targetCsv);
            var tasks = HelperClass.ReadCsv(filePath) ?? new List<ItemGrid>();

            var newTask = new ItemGrid
            {
                Task = $"[{_currentPlan.Subject}] {milestone.StageName}",
                Score = 0,
                Result = string.Empty,
                IsActive = true,
                Importance = "High",
                Urgency = "Low",
                CreatedDate = DateTime.Now,
                LastModifiedDate = DateTime.Now,
                ReminderTime = milestone.TargetDate,
                LongTermGoalId = milestone.Id
            };

            tasks.Add(newTask);
            HelperClass.WriteCsv(tasks, filePath);

            milestone.AssociatedTaskId = newTask.Task;
            SaveMilestones();

            MessageBox.Show($"已将 '{milestone.StageName}' 添加到任务列表", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            _dataChanged = true;
        }

        private void MarkCompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (MilestonesDataGrid.SelectedItem == null)
            {
                MessageBox.Show("请先选择一个里程碑", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedMilestone = MilestonesDataGrid.SelectedItem as LearningMilestone;
            if (selectedMilestone == null) return;

            var result = MessageBox.Show(
                $"是否将 '{selectedMilestone.StageName}' 标记为已完成？",
                "确认完成",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                selectedMilestone.IsCompleted = true;
                selectedMilestone.CompletedDate = DateTime.Now;
                SaveMilestones();

                _currentPlan.CompletedStages++;
                SaveLearningPlan();

                DisplayPlanInfo();
                LoadMilestones();

                MessageBox.Show($"里程碑 '{selectedMilestone.StageName}' 已标记为完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                _dataChanged = true;
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"确定要删除学习计划 '{_currentPlan.Subject}' 吗？\n\n此操作将删除该计划及其所有里程碑，且不可恢复。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                DeleteLearningPlan();
                this.DialogResult = true;
                this.Close();
            }
        }

        private void DeleteLearningPlan()
        {
            string plansCsvPath = Path.Combine(_appDataPath, "long_term_goals.csv");
            var allPlans = HelperClass.ReadLongTermGoalsCsv(plansCsvPath);
            allPlans.RemoveAll(p => p.Id == _currentPlan.Id);
            HelperClass.WriteLongTermGoalsCsv(allPlans, plansCsvPath);

            string milestonesCsvPath = Path.Combine(_appDataPath, $"learning_milestones_{_currentPlan.Id}.csv");
            if (File.Exists(milestonesCsvPath))
            {
                File.Delete(milestonesCsvPath);
            }
        }

        private void SaveMilestones()
        {
            string milestonesCsvPath = Path.Combine(_appDataPath, $"learning_milestones_{_currentPlan.Id}.csv");
            var allMilestones = HelperClass.ReadLearningMilestonesCsv(milestonesCsvPath) ?? new List<LearningMilestone>();

            foreach (var milestone in _milestones)
            {
                var existing = allMilestones.FirstOrDefault(m => m.Id == milestone.Id);
                if (existing != null)
                {
                    existing.IsCompleted = milestone.IsCompleted;
                    existing.CompletedDate = milestone.CompletedDate;
                    existing.AssociatedTaskId = milestone.AssociatedTaskId;
                }
            }

            HelperClass.WriteLearningMilestonesCsv(allMilestones, milestonesCsvPath);
        }

        private void SaveLearningPlan()
        {
            string plansCsvPath = Path.Combine(_appDataPath, "long_term_goals.csv");
            var allPlans = HelperClass.ReadLongTermGoalsCsv(plansCsvPath);

            var existingPlan = allPlans.FirstOrDefault(p => p.Id == _currentPlan.Id);
            if (existingPlan != null)
            {
                existingPlan.CompletedStages = _currentPlan.CompletedStages;
            }

            HelperClass.WriteLongTermGoalsCsv(allPlans, plansCsvPath);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = _dataChanged;
            this.Close();
        }
    }
}
