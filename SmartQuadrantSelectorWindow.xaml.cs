using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimeTask
{
    public partial class SmartQuadrantSelectorWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private List<SubTaskWithQuadrant> _subTasks;
        private bool _isLoading;
        private string _loadingStatus;
        
        public List<SubTaskWithQuadrant> SubTasks
        {
            get => _subTasks;
            set
            {
                _subTasks = value;
                OnPropertyChanged(nameof(SubTasks));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public string LoadingStatus
        {
            get => _loadingStatus;
            set
            {
                _loadingStatus = value;
                OnPropertyChanged(nameof(LoadingStatus));
            }
        }
        
        public Dictionary<string, int> TaskQuadrantAssignments { get; private set; }
        public bool LoadSucceeded { get; private set; } = false;
        public string LoadErrorMessage { get; private set; }
        public int LoadedSubTaskCount => SubTasks?.Count ?? 0;
        
        public SmartQuadrantSelectorWindow()
        {
            InitializeComponent();
            DataContext = this;
            TaskQuadrantAssignments = new Dictionary<string, int>();
            SubTasks = new List<SubTaskWithQuadrant>();
            IsLoading = false;
            LoadingStatus = string.Empty;
        }
        
        public SmartQuadrantSelectorWindow(List<string> subTaskDescriptions, ILlmService llmService) : this()
        {
            _ = InitializeSubTasksFromDescriptionsAsync(subTaskDescriptions, llmService);
        }

        public SmartQuadrantSelectorWindow(string parentTaskDescription, ILlmService llmService, bool loadFromParentTask) : this()
        {
            _ = InitializeFromParentTaskAsync(parentTaskDescription, llmService);
        }

        private async System.Threading.Tasks.Task InitializeFromParentTaskAsync(string parentTaskDescription, ILlmService llmService)
        {
            IsLoading = true;
            ConfirmButton.IsEnabled = false;
            LoadingStatus = I18n.T("SmartQuadrant_LoadingDecompose");
            try
            {
                if (llmService == null || string.IsNullOrWhiteSpace(parentTaskDescription))
                {
                    LoadSucceeded = false;
                    LoadErrorMessage = I18n.T("SmartQuadrant_InvalidInput");
                    LoadingStatus = LoadErrorMessage;
                    return;
                }

                var (decompositionStatus, subTaskStrings) = await llmService.DecomposeTaskAsync(parentTaskDescription);
                if (decompositionStatus != DecompositionStatus.NeedsDecomposition || subTaskStrings == null || !subTaskStrings.Any())
                {
                    LoadSucceeded = false;
                    LoadErrorMessage = I18n.Tf("SmartQuadrant_NoDecomposeResultFormat", decompositionStatus);
                    LoadingStatus = LoadErrorMessage;
                    return;
                }

                await InitializeSubTasksFromDescriptionsAsync(subTaskStrings, llmService);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SmartQuadrantSelectorWindow load failed: {ex.Message}");
                LoadSucceeded = false;
                LoadErrorMessage = I18n.Tf("SmartQuadrant_LoadFailedFormat", ex.Message);
                LoadingStatus = LoadErrorMessage;
            }
            finally
            {
                IsLoading = false;
                ConfirmButton.IsEnabled = LoadSucceeded && LoadedSubTaskCount > 0;
            }
        }

        private async System.Threading.Tasks.Task InitializeSubTasksFromDescriptionsAsync(List<string> subTaskDescriptions, ILlmService llmService)
        {
            IsLoading = true;
            ConfirmButton.IsEnabled = false;
            LoadingStatus = I18n.T("SmartQuadrant_LoadingRecommend");
            var subTasks = new List<SubTaskWithQuadrant>();
            TaskQuadrantAssignments.Clear();

            foreach (var description in subTaskDescriptions ?? new List<string>())
            {
                var subTask = new SubTaskWithQuadrant
                {
                    TaskDescription = description,
                    RecommendedQuadrant = 1, // 默认推荐到重要不紧急
                    SelectedQuadrant = 1
                };
                
                // 使用LLM智能推荐象限
                try
                {
                    var (importance, urgency) = await llmService.AnalyzeTaskPriorityAsync(description);
                    subTask.RecommendedQuadrant = GetQuadrantIndex(importance, urgency);
                    subTask.SelectedQuadrant = subTask.RecommendedQuadrant;
                    subTask.RecommendationText = I18n.Tf("SmartQuadrant_AiSuggestFormat", GetQuadrantName(subTask.RecommendedQuadrant), LocalizePriority(importance), LocalizePriority(urgency));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error analyzing task priority: {ex.Message}");
                    subTask.RecommendationText = I18n.Tf("SmartQuadrant_AiSuggestFallbackFormat", GetQuadrantName(1));
                }
                
                subTasks.Add(subTask);
                TaskQuadrantAssignments[description] = subTask.SelectedQuadrant;
            }
            
            SubTasks = subTasks;
            LoadSucceeded = SubTasks.Count > 0;
            LoadingStatus = LoadSucceeded ? I18n.T("SmartQuadrant_LoadDone") : I18n.T("SmartQuadrant_NoTaskLoaded");
            IsLoading = false;
            ConfirmButton.IsEnabled = LoadSucceeded && LoadedSubTaskCount > 0;
        }
        
        private int GetQuadrantIndex(string importance, string urgency)
        {
            bool isImportant = importance?.ToLower().Contains("high") == true;
            bool isUrgent = urgency?.ToLower().Contains("high") == true;
            
            if (isImportant && isUrgent) return 0; // 重要且紧急
            if (isImportant && !isUrgent) return 1; // 重要不紧急
            if (!isImportant && isUrgent) return 2; // 不重要但紧急
            return 3; // 不重要不紧急
        }
        
        private string GetQuadrantName(int quadrantIndex)
        {
            return quadrantIndex switch
            {
                0 => I18n.T("Quadrant_ImportantUrgent"),
                1 => I18n.T("Quadrant_ImportantNotUrgent"),
                2 => I18n.T("Quadrant_NotImportantUrgent"),
                3 => I18n.T("Quadrant_NotImportantNotUrgent"),
                _ => I18n.T("SmartQuadrant_UnknownQuadrant")
            };
        }

        private static string LocalizePriority(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return I18n.T("Priority_Unknown");
            }

            string v = value.Trim().ToLowerInvariant();
            if (v == "high") return I18n.T("Priority_High");
            if (v == "medium") return I18n.T("Priority_Medium");
            if (v == "low") return I18n.T("Priority_Low");
            return value;
        }
        
        private void QuadrantButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int quadrantIndex))
                {
                    // 找到对应的子任务
                    var taskContainer = FindParent<Border>(button);
                    if (taskContainer?.DataContext is SubTaskWithQuadrant subTask)
                    {
                        subTask.SelectedQuadrant = quadrantIndex;
                        TaskQuadrantAssignments[subTask.TaskDescription] = quadrantIndex;
                        
                        // 更新视觉反馈
                        UpdateQuadrantButtonStyles(taskContainer, quadrantIndex);
                    }
                }
            }
        }
        
        private void UpdateQuadrantButtonStyles(Border taskContainer, int selectedQuadrant)
        {
            // 重置所有按钮样式
            var buttons = FindChildren<Button>(taskContainer);
            foreach (var btn in buttons)
            {
                if (btn.Tag is string tag && int.TryParse(tag, out int quadrant))
                {
                    if (quadrant == selectedQuadrant)
                    {
                        btn.Background = new SolidColorBrush(Color.FromRgb(230, 247, 255)); // 选中状态
                        btn.BorderThickness = new Thickness(3);
                    }
                    else
                    {
                        btn.Background = Brushes.White; // 未选中状态
                        btn.BorderThickness = new Thickness(2);
                    }
                }
            }
        }
        
        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否所有任务都已分配
            if (TaskQuadrantAssignments.Count != SubTasks?.Count)
            {
                MessageBox.Show(I18n.T("SmartQuadrant_AssignAll"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            
            if (parent is T parentT)
                return parentT;
            
            return FindParent<T>(parent);
        }
        
        private IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T childT)
                    yield return childT;
                
                foreach (var descendant in FindChildren<T>(child))
                    yield return descendant;
            }
        }
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public class SubTaskWithQuadrant : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private string _taskDescription;
        private int _recommendedQuadrant;
        private int _selectedQuadrant;
        private string _recommendationText;
        
        public string TaskDescription
        {
            get => _taskDescription;
            set
            {
                _taskDescription = value;
                OnPropertyChanged(nameof(TaskDescription));
            }
        }
        
        public int RecommendedQuadrant
        {
            get => _recommendedQuadrant;
            set
            {
                _recommendedQuadrant = value;
                OnPropertyChanged(nameof(RecommendedQuadrant));
            }
        }
        
        public int SelectedQuadrant
        {
            get => _selectedQuadrant;
            set
            {
                _selectedQuadrant = value;
                OnPropertyChanged(nameof(SelectedQuadrant));
            }
        }
        
        public string RecommendationText
        {
            get => _recommendationText;
            set
            {
                _recommendationText = value;
                OnPropertyChanged(nameof(RecommendationText));
            }
        }
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
