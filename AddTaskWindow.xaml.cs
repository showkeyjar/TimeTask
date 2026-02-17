using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input; // Required for MouseButtonEventArgs, MouseButtonState
using System.Threading.Tasks; // For async operations

namespace TimeTask
{
    // Removed duplicate namespace declaration
    public partial class AddTaskWindow : Window
    {
        private LlmService _llmService;
        private DatabaseService _databaseService;
        private bool _isClarificationRound = false; // State for clarification
        private string _originalTaskDescription = string.Empty; // To store original task if clarification is needed
        private bool _isLlmConfigErrorNotified = false; // Flag to track if user has been notified of LLM config error
        private readonly IntentRecognizer _intentRecognizer = new IntentRecognizer();

        public string TaskDescription { get; private set; }
        public int SelectedListIndex { get; private set; } // 0-indexed
        public bool TaskAdded { get; private set; } = false; // Renamed from IsTaskAdded for clarity
        public ItemGrid NewTask { get; private set; } // The newly created task object

        // 预填充任务描述（用于从草稿添加）
        private string _preFilledDescription = null;
        private string _preFilledQuadrant = null;
        private bool _skipClarificationForPrefilled = true;

        // 兼容旧版本的构造函数
        public AddTaskWindow(DatabaseService databaseService, LlmService llmService, int? defaultQuadrantIndex = null)
        {
            InitializeComponent();
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));

            InitializeCombobox(defaultQuadrantIndex);
        }

        // 兼容旧版本：只传 LlmService
        public AddTaskWindow(LlmService llmService, int? defaultQuadrantIndex = null)
        {
            InitializeComponent();
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _databaseService = null;

            InitializeCombobox(defaultQuadrantIndex);
        }

        private void InitializeCombobox(int? defaultQuadrantIndex)
        {
            // Populate ComboBox
            ListSelectorComboBox.ItemsSource = new List<string> {
                "重要且紧急", // Important & Urgent
                "重要不紧急", // Important & Not Urgent
                "不重要但紧急", // Not Important & Urgent
                "不重要不紧急"  // Not Important & Not Urgent
            };
            ListSelectorComboBox.SelectedIndex = 0; // Default to "重要且紧急"

            // Pre-select based on defaultQuadrantIndex if provided
            if (defaultQuadrantIndex.HasValue)
            {
                if (defaultQuadrantIndex.Value >= 0 && defaultQuadrantIndex.Value < ListSelectorComboBox.Items.Count)
                {
                    ListSelectorComboBox.SelectedIndex = defaultQuadrantIndex.Value;
                }
            }

            // Populate Reminder Time ComboBoxes
            for (int i = 0; i < 24; i++) ReminderHourComboBox.Items.Add(i.ToString("D2"));
            for (int i = 0; i < 60; i++) ReminderMinuteComboBox.Items.Add(i.ToString("D2"));

            // Set default selections
            EnableReminderCheckBox.IsChecked = false;
            ReminderDatePicker.SelectedDate = DateTime.Today;
            ReminderHourComboBox.SelectedIndex = 0; // Default to "00"
            ReminderMinuteComboBox.SelectedIndex = 0; // Default to "00"
        }

        /// <summary>
        /// 预填充任务描述（用于从草稿添加）
        /// </summary>
        public void SetPreFilledTask(string description, string quadrant)
        {
            if (!string.IsNullOrWhiteSpace(description))
            {
                _preFilledDescription = description;
                TaskDescriptionTextBox.Text = description;
            }

            if (!string.IsNullOrWhiteSpace(quadrant))
            {
                _preFilledQuadrant = quadrant;
                // 映射象限名称到索引
                var quadrantMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "重要且紧急", 0 },
                    { "重要不紧急", 1 },
                    { "不重要但紧急", 2 },
                    { "不重要紧急", 2 }, // 兼容不同表述
                    { "不重要不紧急", 3 }
                };

                if (quadrantMap.TryGetValue(quadrant, out int index))
                {
                    ListSelectorComboBox.SelectedIndex = index;
                }
            }
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
            string currentTaskDescription = NormalizeTaskText(TaskDescriptionTextBox.Text);
            SelectedListIndex = ListSelectorComboBox.SelectedIndex;
            string configErrorSubstring = "LLM dummy response (Configuration Error: API key missing or placeholder)";

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
                if (!_isClarificationRound && !(_skipClarificationForPrefilled && !string.IsNullOrWhiteSpace(_preFilledDescription)))
                {
                    // Initial Submission: Analyze Clarity
                    _originalTaskDescription = currentTaskDescription; // Save for potential reset
                    var (status, question) = await _llmService.AnalyzeTaskClarityAsync(currentTaskDescription);

                    // Check for LLM configuration error after clarity analysis
                    // string configErrorSubstring is now defined at the beginning of the method
                    if (!_isLlmConfigErrorNotified && question != null && question.Contains(configErrorSubstring))
                    {
                        MessageBox.Show("The AI assistant features may be limited due to a configuration issue (e.g., missing or placeholder API key). Please check the application's setup if you expect full AI functionality.",
                                        "LLM Configuration Issue", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _isLlmConfigErrorNotified = true;
                    }

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
                var (ruleImportance, ruleUrgency) = _intentRecognizer.EstimatePriority(TaskDescription);

                // Check for LLM configuration error after priority analysis
                // string configErrorSubstring has been defined above
                if (!_isLlmConfigErrorNotified &&
                    ((llmImportance != null && llmImportance.Contains(configErrorSubstring)) ||
                     (llmUrgency != null && llmUrgency.Contains(configErrorSubstring))))
                {
                    MessageBox.Show("The AI assistant features may be limited due to a configuration issue (e.g., missing or placeholder API key). Please check the application's setup if you expect full AI functionality.",
                                    "LLM Configuration Issue", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _isLlmConfigErrorNotified = true;
                }

                // --- LLM Suggestion Logic ---
                var (finalImportanceByAi, finalUrgencyByAi, sourceTag) = MergePriority(llmImportance, llmUrgency, ruleImportance, ruleUrgency);
                int suggestedIndex = GetIndexFromPriority(finalImportanceByAi, finalUrgencyByAi);
                ListSelectorComboBox.SelectedIndex = suggestedIndex;

                if (suggestedIndex != -1 && ListSelectorComboBox.SelectedItem != null)
                {
                    string label = ListSelectorComboBox.SelectedItem as string;
                    LlmSuggestionText.Text = $"AI建议象限（{sourceTag}）: {label}";
                    LlmSuggestionText.Visibility = Visibility.Visible;
                }
                else
                {
                    // Handle cases where suggestion is ambiguous or mapping fails
                    LlmSuggestionText.Text = "AI建议暂不可用，请手动选择象限。";
                    LlmSuggestionText.Visibility = Visibility.Collapsed; // Or Visible with a different message
                }
                // --- End LLM Suggestion Logic ---

                // User confirms or changes selection, then clicks "Add Task" again (or it's the first time)
                // The final selection is captured by:
                SelectedListIndex = ListSelectorComboBox.SelectedIndex;
                // This line was already here, but its role is now more significant

                // Update NewTask's Importance and Urgency based on the final ComboBox selection
                var (finalImportance, finalUrgency) = GetPriorityFromIndex(SelectedListIndex);

                DateTime? reminderTime = null;
                if (EnableReminderCheckBox.IsChecked == true && ReminderDatePicker.SelectedDate.HasValue)
                {
                    DateTime date = ReminderDatePicker.SelectedDate.Value;
                    int hour = 0;
                    int minute = 0;

                    if (ReminderHourComboBox.SelectedItem != null && int.TryParse(ReminderHourComboBox.SelectedItem.ToString(), out int h))
                    {
                        hour = h;
                    }
                    if (ReminderMinuteComboBox.SelectedItem != null && int.TryParse(ReminderMinuteComboBox.SelectedItem.ToString(), out int m))
                    {
                        minute = m;
                    }
                    // Ensure hour and minute are parsed successfully, otherwise they remain 0 or previously parsed value.
                    // A more robust solution might involve explicit validation here if 00:00 is not always a desired default.
                    reminderTime = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0);
                }

                NewTask = new ItemGrid
                {
                    Task = TaskDescription,
                    Importance = finalImportance, // Updated based on final selection
                    Urgency = finalUrgency,   // Updated based on final selection
                    Score = 0, // Default score
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    LastModifiedDate = DateTime.Now,
                    Result = string.Empty,
                    ReminderTime = reminderTime
                };

                TaskAdded = true;
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
        public static int GetIndexFromPriority(string importance, string urgency)
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
            return 1; // 对未知结果偏向“重要不紧急”，降低默认紧急打扰
        }

        // Helper method to map ComboBox index back to Importance/Urgency strings
        public static (string Importance, string Urgency) GetPriorityFromIndex(int index)
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

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private static bool IsKnownPriority(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string v = value.Trim().ToLowerInvariant();
            return v == "high" || v == "medium" || v == "low";
        }

        private (string importance, string urgency, string sourceTag) MergePriority(
            string llmImportance,
            string llmUrgency,
            string ruleImportance,
            string ruleUrgency)
        {
            bool llmValid = IsKnownPriority(llmImportance) && IsKnownPriority(llmUrgency)
                && !ContainsDummy(llmImportance)
                && !ContainsDummy(llmUrgency);

            if (llmValid)
            {
                return (llmImportance, llmUrgency, "LLM");
            }

            bool ruleValid = IsKnownPriority(ruleImportance) && IsKnownPriority(ruleUrgency);
            if (ruleValid)
            {
                return (ruleImportance, ruleUrgency, "规则");
            }

            return ("Medium", "Low", "默认");
        }

        private static bool ContainsDummy(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf("dummy response", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string NormalizeTaskText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string trimmed = raw.Trim();
            string extracted = _intentRecognizer.ExtractTaskDescription(trimmed);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted.Trim();
            }

            return trimmed;
        }
    }
} // Closing brace for namespace TimeTask
