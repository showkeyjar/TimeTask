using System;
using System.Windows;

namespace TimeTask
{
    public partial class SetLongTermGoalWindow : Window
    {
        public string GoalDescription { get; private set; }
        public string Duration { get; private set; }
        public bool IsLearningPlan { get; private set; }

        public SetLongTermGoalWindow()
        {
            InitializeComponent();
            UpdateUI();
        }

        private void GoalType_Checked(object sender, RoutedEventArgs e)
        {
            if (GoalDescriptionLabel != null && LearningPlanRadioButton != null)
            {
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            if (GoalDescriptionLabel == null || LearningPlanRadioButton == null)
                return;

            bool isLearningPlan = LearningPlanRadioButton.IsChecked == true;
            IsLearningPlan = isLearningPlan;

            if (isLearningPlan)
            {
                GoalDescriptionLabel.Text = I18n.T("SetGoal_LabelLearningTarget");
            }
            else
            {
                GoalDescriptionLabel.Text = I18n.T("SetGoal_LabelDescription");
            }
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GoalDescriptionTextBox.Text) || string.IsNullOrWhiteSpace(DurationTextBox.Text))
            {
                MessageBox.Show(I18n.T("SetGoal_MessageFillRequired"), I18n.T("SetGoal_TitleInputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.GoalDescription = GoalDescriptionTextBox.Text.Trim();
            this.Duration = DurationTextBox.Text.Trim();

            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
