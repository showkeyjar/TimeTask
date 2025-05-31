using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Threading.Tasks; // For async operations

namespace TimeTask
{
    // Removed duplicate namespace declaration
    public partial class AddTaskWindow : Window
    {
        private LlmService _llmService;
        private bool _isClarificationRound = false; // State for clarification
        private string _originalTaskDescription = string.Empty; // To store original task if clarification is needed

        public string TaskDescription { get; private set; }
        public int SelectedListIndex { get; private set; } // 0-indexed
        public bool IsTaskAdded { get; private set; } = false;
        public ItemGrid NewTask { get; private set; } // The newly created task object
        public List<string> UserRoles { get; private set; }
        public string SelectedTaskRole { get; private set; }

        public AddTaskWindow(LlmService llmService, List<string> userRoles)
        {
            InitializeComponent(); // Keep this at the top
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            UserRoles = userRoles ?? new List<string>();

            // Populate ComboBox for lists (existing code)
            ListSelectorComboBox.ItemsSource = new List<string> {
                "重要且紧急", "重要不紧急", "不重要但紧急", "不重要不紧急"
            };
            ListSelectorComboBox.SelectedIndex = 0;

            // Populate new ComboBox for roles
            RoleSelectorComboBox.ItemsSource = UserRoles;
            if (UserRoles.Any())
            {
                RoleSelectorComboBox.SelectedIndex = 0; // Default selection
            }
            else
            {
                // Handle case where no roles are passed (e.g., add a default "Unassigned" or disable)
                RoleSelectorComboBox.IsEnabled = false;
                // Optionally, add a placeholder item like "Unassigned" if it's disabled
                // RoleSelectorComboBox.Items.Add("Unassigned");
                // RoleSelectorComboBox.SelectedIndex = 0;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void ResetClarificationButton_Click(object sender, RoutedEventArgs e)
        {
            TaskDescriptionTextBox.Text = _originalTaskDescription; // Restore original text
            ClarificationBorder.Visibility = Visibility.Collapsed; // Changed
            // ClarificationPromptText.Visibility = Visibility.Collapsed; // Old
            // ResetClarificationButton.Visibility = Visibility.Collapsed; // Old
            AddTaskButton.Content = "Add Task";
            _isClarificationRound = false;
            TaskDescriptionTextBox.Focus(); // Set focus back to the textbox
        }

        private async void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            string currentTaskDescription = TaskDescriptionTextBox.Text.Trim();
            SelectedListIndex = ListSelectorComboBox.SelectedIndex;
            SelectedTaskRole = RoleSelectorComboBox.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(currentTaskDescription))
            {
                MessageBox.Show("Task description cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (SelectedListIndex < 0)
            {
                MessageBox.Show("Please select a list.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Disable buttons during processing
            AddTaskButton.IsEnabled = false;
            CancelButton.IsEnabled = false;

            try
            {
                if (!_isClarificationRound)
                {
                    // Initial Submission: Analyze Clarity
                    _originalTaskDescription = currentTaskDescription; // Save for potential reset
                    var (status, question) = await _llmService.AnalyzeTaskClarityAsync(currentTaskDescription);

                    if (status == ClarityStatus.NeedsClarification)
                    {
                        ClarificationPromptText.Text = question; // This still needs to be set
                        ClarificationBorder.Visibility = Visibility.Visible; // Changed
                        // ClarificationPromptText.Visibility = Visibility.Visible; // Old
                        // ResetClarificationButton.Visibility = Visibility.Visible; // Old
                        AddTaskButton.Content = "Submit Clarified Task";
                        _isClarificationRound = true;
                        TaskDescriptionTextBox.Focus(); // Focus on textbox for user to edit
                        return; // Wait for user to clarify
                    }
                    // If Clear or Unknown, proceed directly to prioritization
                }

                // Prioritization & Task Creation (either directly or after clarification)
                TaskDescription = currentTaskDescription; // Final task description
                var (importance, urgency) = await _llmService.GetTaskPriorityAsync(TaskDescription);

                NewTask = new ItemGrid
                {
                    Task = TaskDescription,
                    Importance = importance,
                    Urgency = urgency,
                    Score = 0, // Default score
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    LastModifiedDate = DateTime.Now,
                    Result = string.Empty,
                    AssignedRole = SelectedTaskRole ?? "Unassigned" // Assign the selected role
                };

                IsTaskAdded = true;
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Consider how to handle state if error occurs mid-process
                // For now, re-enable buttons and let user retry or cancel
            }
            finally
            {
                AddTaskButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
            }
        }
    }
} // Closing brace for namespace TimeTask
