using System.Windows;

namespace TimeTask
{
    public partial class SetLongTermGoalWindow : Window
    {
        public string GoalDescription { get; private set; }
        public string Duration { get; private set; }

        public SetLongTermGoalWindow()
        {
            InitializeComponent();
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GoalDescriptionTextBox.Text) || string.IsNullOrWhiteSpace(DurationTextBox.Text))
            {
                MessageBox.Show("Please enter both a goal description and a duration.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            this.GoalDescription = GoalDescriptionTextBox.Text;
            this.Duration = DurationTextBox.Text;
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
