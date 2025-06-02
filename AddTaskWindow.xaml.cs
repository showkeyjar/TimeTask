using System;
using System.Collections.Generic;
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

        public AddTaskWindow(LlmService llmService)
        {
            InitializeComponent();
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));

            // Populate ComboBox
            ListSelectorComboBox.ItemsSource = new List<string> {
                "重要且紧急", // Important & Urgent
                "重要不紧急", // Important & Not Urgent
                "不重要但紧急", // Not Important & Urgent
                "不重要不紧急"  // Not Important & Not Urgent
            };
            ListSelectorComboBox.SelectedIndex = 0; // Default to "重要且紧急"
        }

        // Removed older synchronous AddTaskButton_Click method. The async version below is used.

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
                var (llmImportance, llmUrgency) = await _llmService.GetTaskPriorityAsync(TaskDescription);

                // --- LLM Suggestion Logic ---
                int suggestedIndex = GetIndexFromPriority(llmImportance, llmUrgency);
                ListSelectorComboBox.SelectedIndex = suggestedIndex;

                if (suggestedIndex != -1 && ListSelectorComboBox.SelectedItem != null)
                {
                    LlmSuggestionText.Text = $"LLM Suggests: {ListSelectorComboBox.SelectedItem as string}";
                    LlmSuggestionText.Visibility = Visibility.Visible;
                }
                else
                {
                    // Handle cases where suggestion is ambiguous or mapping fails
                    LlmSuggestionText.Text = "LLM suggestion unavailable.";
                    LlmSuggestionText.Visibility = Visibility.Collapsed; // Or Visible with a different message
                }
                // --- End LLM Suggestion Logic ---

                // User confirms or changes selection, then clicks "Add Task" again (or it's the first time)
                // The final selection is captured by:
                SelectedListIndex = ListSelectorComboBox.SelectedIndex;
                // This line was already here, but its role is now more significant

                // Update NewTask's Importance and Urgency based on the final ComboBox selection
                var (finalImportance, finalUrgency) = GetPriorityFromIndex(SelectedListIndex);

                NewTask = new ItemGrid
                {
                    Task = TaskDescription,
                    Importance = finalImportance, // Updated based on final selection
                    Urgency = finalUrgency,   // Updated based on final selection
                    Score = 0, // Default score
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    LastModifiedDate = DateTime.Now,
                    Result = string.Empty
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

        // Helper method to map LLM priority to ComboBox index
        internal static int GetIndexFromPriority(string importance, string urgency)
        {
            // Normalize inputs to lower case for robust comparison
            importance = importance?.ToLowerInvariant() ?? "unknown";
            urgency = urgency?.ToLowerInvariant() ?? "unknown";

            if (importance == "high" && urgency == "high") return 0; // Important & Urgent
            if (importance == "high" && urgency == "low") return 1;  // Important & Not Urgent
            if (importance == "low" && urgency == "high") return 2;  // Not Important & Urgent
            if (importance == "low" && urgency == "low") return 3;   // Not Important & Not Urgent

            // Default or fallback for Medium/Unknown - could be -1 to indicate no selection
            // Or a specific category like "Important & Urgent"
            return 0; // Defaulting to "Important & Urgent" for now
        }

        // Helper method to map ComboBox index back to Importance/Urgency strings
        internal static (string Importance, string Urgency) GetPriorityFromIndex(int index)
        {
            switch (index)
            {
                case 0: return ("High", "High");   // Important & Urgent
                case 1: return ("High", "Low");    // Important & Not Urgent
                case 2: return ("Low", "High");    // Not Important & Urgent
                case 3: return ("Low", "Low");     // Not Important & Not Urgent
                default: return ("Medium", "Medium"); // Default if index is unexpected
            }
        }
    }
} // Closing brace for namespace TimeTask
