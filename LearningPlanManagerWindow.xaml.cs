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
            DurationTextBlock.Text = I18n.Tf("Lpm_DurationFormat", _currentPlan.TotalDuration);

            double progress = _currentPlan.ProgressPercentage;
            ProgressBar.Value = progress;
            ProgressTextBlock.Text = $"{progress:F1}%";
            ProgressDetailTextBlock.Text = I18n.Tf("Lpm_StagesFormat", _currentPlan.CompletedStages, _currentPlan.TotalStages);
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
            MilestoneCountTextBlock.Text = I18n.Tf("Lpm_MilestoneCountFormat", _milestones.Count);
        }

        private void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            if (MilestonesDataGrid.SelectedItem == null)
            {
                MessageBox.Show(I18n.T("Lpm_SelectMilestoneFirst"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedMilestone = MilestonesDataGrid.SelectedItem as LearningMilestone;
            if (selectedMilestone == null) return;

            if (selectedMilestone.IsCompleted)
            {
                MessageBox.Show(I18n.T("Lpm_AlreadyCompleted"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                I18n.Tf("Lpm_ActivateConfirmFormat", selectedMilestone.StageName, selectedMilestone.Description),
                I18n.T("Lpm_ActivateConfirmTitle"),
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

            MessageBox.Show(I18n.Tf("Lpm_AddedFormat", milestone.StageName), I18n.T("Title_Done"), MessageBoxButton.OK, MessageBoxImage.Information);
            _dataChanged = true;
        }

        private void MarkCompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (MilestonesDataGrid.SelectedItem == null)
            {
                MessageBox.Show(I18n.T("Lpm_SelectMilestoneFirst"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedMilestone = MilestonesDataGrid.SelectedItem as LearningMilestone;
            if (selectedMilestone == null) return;

            var result = MessageBox.Show(
                I18n.Tf("Lpm_MarkConfirmFormat", selectedMilestone.StageName),
                I18n.T("Lpm_MarkConfirmTitle"),
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

                MessageBox.Show(I18n.Tf("Lpm_MarkedDoneFormat", selectedMilestone.StageName), I18n.T("Title_Done"), MessageBoxButton.OK, MessageBoxImage.Information);
                _dataChanged = true;
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                I18n.Tf("Lpm_DeleteConfirmFormat", _currentPlan.Subject),
                I18n.T("Lpm_DeleteConfirmTitle"),
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
