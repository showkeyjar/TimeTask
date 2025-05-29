using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
// Using the root namespace ItemGrid to match the rest of the application
// using TimeTask.Models;  // This is commented out to use the root namespace ItemGrid
using TimeTask.Services;
using System.Windows.Data;
using System.Globalization;
using System.Windows.Media;

namespace TimeTask
{
    public class InvertBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }
    }

    public partial class AddTaskWindow : Window
    {
        private readonly Services.ILLMService _llmService;
        private readonly ILogger<AddTaskWindow> _logger;
        private string _originalTaskDescription = string.Empty;
        private bool _isAnalyzing = false;
        private bool _isClassifying = false;

        public string TaskDescription { get; private set; } = string.Empty;
        public int SelectedListIndex { get; private set; }
        public bool IsTaskAdded { get; private set; } = false;
        public ItemGrid? NewTask { get; private set; }

        public AddTaskWindow(Services.ILLMService llmService, ILogger<AddTaskWindow>? logger = null)
        {
            InitializeComponent();
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Initialize UI elements
            if (ListSelectorComboBox != null)
            {
                ListSelectorComboBox.ItemsSource = new List<string> 
                { 
                    "重要且紧急 (重要 & 紧急)",
                    "重要不紧急 (重要 & 不紧急)",
                    "不重要但紧急 (不重要 & 紧急)",
                    "不重要不紧急 (不重要 & 不紧急)" 
                };
                ListSelectorComboBox.SelectedIndex = 0; // Default to first category
            }

            // InvertBoolConverter is now defined in XAML
        }

        // Removed older synchronous AddTaskButton_Click method. The async version below is used.

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DialogResult = false;
                Close();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in CancelButton_Click");
                throw;
            }
        }

        private void ResetClarificationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TaskDescriptionTextBox != null)
                {
                    TaskDescriptionTextBox.Text = _originalTaskDescription; // Restore original text
                    TaskDescriptionTextBox.Focus(); // Set focus back to the textbox
                }

                if (ClarificationPromptText != null)
                    ClarificationPromptText.Visibility = Visibility.Collapsed;
                    
                if (ResetClarificationButton != null)
                    ResetClarificationButton.Visibility = Visibility.Collapsed;
                
                if (AddTaskButton != null)
                    AddTaskButton.Content = "Add Task";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in ResetClarificationButton_Click");
                throw;
            }
        }

        private void TaskDescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (AutoClassifyCheckBox?.IsChecked == true && !string.IsNullOrWhiteSpace(TaskDescriptionTextBox?.Text))
            {
                // Fire and forget the async operation
                _ = AutoClassifyTaskAsync().ConfigureAwait(false);
            }
        }

        private async Task AutoClassifyTaskAsync()
        {
            if (_isAnalyzing || _isClassifying) return;
            
            _isClassifying = true;
            try
            {
                var taskDescription = TaskDescriptionTextBox?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(taskDescription)) return;

                // Small delay to avoid too many rapid classifications
                await Task.Delay(500);
                if (taskDescription != TaskDescriptionTextBox?.Text?.Trim()) return;

                var result = await _llmService.GetTaskPriorityAsync(taskDescription ?? string.Empty);
                var importance = result.Importance ?? string.Empty;
                var urgency = result.Urgency ?? string.Empty;
                
                // Map importance/urgency to category index (0-3)
                int categoryIndex = (importance == "重要" ? 0 : 2) + (urgency == "紧急" ? 0 : 1);
                if (categoryIndex < 0) categoryIndex = 0;
                if (categoryIndex > 3) categoryIndex = 3;
                
                if (ListSelectorComboBox != null && ListSelectorComboBox.Items.Count > categoryIndex)
                {
                    ListSelectorComboBox.SelectedIndex = categoryIndex;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during auto-classification");
            }
            finally
            {
                _isClassifying = false;
            }
        }

        private void AutoClassifyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (ListSelectorComboBox == null) return;
            
            if (AutoClassifyCheckBox?.IsChecked == true && !string.IsNullOrWhiteSpace(TaskDescriptionTextBox?.Text))
            {
                _ = AutoClassifyTaskAsync();
            }
        }

        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (TaskDescriptionTextBox == null || AnalyzeButton == null) return;

            string originalContent = AnalyzeButton.Content?.ToString() ?? "Analyze Task";
            AnalyzeButton.IsEnabled = false;
            AnalyzeButton.Content = "Analyzing...";

            try
            {
                var taskDescription = TaskDescriptionTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(taskDescription))
                {
                    MessageBox.Show("Please enter a task description first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var result = await _llmService.GetTaskPriorityAsync(taskDescription ?? string.Empty).ConfigureAwait(true);
                MessageBox.Show($"Task Analysis:\n\nTask: {taskDescription}\nImportance: {result.Importance}\nUrgency: {result.Urgency}", 
                    "Task Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error analyzing task");
                MessageBox.Show($"Error analyzing task: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (AnalyzeButton != null)
                {
                    AnalyzeButton.IsEnabled = true;
                    AnalyzeButton.Content = originalContent;
                }
            }
        }

        private async Task AddTaskButton_ClickAsync()
        {
            await Task.Yield(); // Ensure we're in an async context
            if (TaskDescriptionTextBox == null || ListSelectorComboBox == null || AddTaskButton == null)
            {
                _logger?.LogError("UI elements are not properly initialized");
                return;
            }

            string currentTaskDescription = TaskDescriptionTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(currentTaskDescription))
            {
                _logger?.LogWarning("Task description is empty");
                ShowStatusMessage("任务描述不能为空", System.Windows.Media.Colors.Red);
                return;
            }

            // Get the selected category from the ComboBox
            string category = ListSelectorComboBox.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(category))
            {
                ShowStatusMessage("请选择任务类别", System.Windows.Media.Colors.Red);
                return;
            }

            // Show loading state
            AddTaskButton.IsEnabled = false;
            AddTaskButton.Content = "处理中...";

            try
            {
                // Create new task using the root namespace ItemGrid
                NewTask = new ItemGrid
                {
                    Task = currentTaskDescription,
                    // Only set properties that exist in the root namespace ItemGrid
                    CreatedDate = DateTime.Now,
                    IsActive = true,
                    // Set importance and urgency as strings using the selected category
                    Importance = GetImportanceFromCategory(category),
                    Urgency = GetUrgencyFromCategory(category),
                    // Set a default result
                    Result = "待处理"
                };

                // Return success
                IsTaskAdded = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error adding task");
                ShowStatusMessage($"添加任务时出错: {ex.Message}", System.Windows.Media.Colors.Red);
            }
            finally
            {
                // Reset button state
                if (AddTaskButton != null)
                {
                    AddTaskButton.IsEnabled = true;
                    AddTaskButton.Content = "添加任务";
                }
            }
        }

        private string GetImportanceFromCategory(string category)
        {
            // Map category to importance (1-5)
            return (category switch
            {
                "重要且紧急" => 5,
                "重要不紧急" => 5,
                "紧急不重要" => 3,
                "不紧急不重要" => 1,
                _ => 3 // Default to medium importance
            }).ToString();
        }

        private string GetUrgencyFromCategory(string category)
        {
            // Map category to urgency (1-5)
            return (category switch
            {
                "重要且紧急" => 5,
                "重要不紧急" => 3,
                "紧急不重要" => 5,
                "不紧急不重要" => 1,
                _ => 3 // Default to medium urgency
            }).ToString();
        }

        private void ShowStatusMessage(string message, System.Windows.Media.Color color)
        {
            Dispatcher.Invoke(() =>
            {
                if (StatusTextBlock != null)
                {
                    StatusTextBlock.Text = message;
                    StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(color);
                }
            });
        }

        private void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            // Fire and forget the async operation
            _ = AddTaskButton_ClickAsync().ContinueWith(t => 
            {
                if (t.Exception != null)
                {
                    _logger?.LogError(t.Exception, "Error in AddTaskButton_Click");
                    Dispatcher.Invoke(() => 
                        MessageBox.Show("An error occurred while adding the task.", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error));
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
} // Closing brace for namespace TimeTask
