using System;
using System.Windows;

namespace TimeTask
{
    public partial class SetLearningPlanWindow : Window
    {
        public string Subject { get; private set; }
        public string Goal { get; private set; }
        public string Duration { get; private set; }
        public DateTime? StartDate { get; private set; }

        public SetLearningPlanWindow()
        {
            InitializeComponent();
            StartDatePicker.SelectedDate = DateTime.Today;
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SubjectTextBox.Text))
            {
                MessageBox.Show("请输入学习科目", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                SubjectTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(GoalTextBox.Text))
            {
                MessageBox.Show("请输入学习目标", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                GoalTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(DurationTextBox.Text))
            {
                MessageBox.Show("请输入学习时长", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                DurationTextBox.Focus();
                return;
            }

            if (!StartDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("请选择开始日期", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                StartDatePicker.Focus();
                return;
            }

            this.Subject = SubjectTextBox.Text.Trim();
            this.Goal = GoalTextBox.Text.Trim();
            this.Duration = DurationTextBox.Text.Trim();
            this.StartDate = StartDatePicker.SelectedDate.Value;

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
