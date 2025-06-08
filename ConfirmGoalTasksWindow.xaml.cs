using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TimeTask
{
    // Helper Class for Display
    public class SelectableProposedTask : ProposedDailyTask
    {
        public bool IsSelected { get; set; }
    }

    public partial class ConfirmGoalTasksWindow : Window
    {
        public List<SelectableProposedTask> AllProposedTasks { get; private set; }
        public List<ProposedDailyTask> SelectedTasks { get; private set; }

        public ConfirmGoalTasksWindow(List<ProposedDailyTask> proposedTasks)
        {
            InitializeComponent();

            AllProposedTasks = proposedTasks.Select(task => new SelectableProposedTask
            {
                Day = task.Day,
                TaskDescription = task.TaskDescription,
                Quadrant = task.Quadrant,
                EstimatedTime = task.EstimatedTime,
                IsSelected = true // Default to selected
            }).ToList();

            TasksListBox.ItemsSource = AllProposedTasks;
            SelectedTasks = new List<ProposedDailyTask>();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedTasks.Clear();
            foreach (var selectableTask in AllProposedTasks)
            {
                if (selectableTask.IsSelected)
                {
                    // Add the underlying ProposedDailyTask or the SelectableProposedTask itself
                    // Since SelectableProposedTask inherits ProposedDailyTask, we can add it directly
                    // if no further distinction is needed later for the core ProposedDailyTask type.
                    // For clarity, creating a new ProposedDailyTask or casting might be preferred
                    // if the receiver expects exactly ProposedDailyTask instances without the IsSelected member.
                    // However, for this context, adding the selectableTask (which is a ProposedDailyTask) is fine.
                    SelectedTasks.Add(selectableTask);
                }
            }
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
