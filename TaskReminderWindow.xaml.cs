using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace TimeTask
{
    public partial class TaskReminderWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private readonly bool _isDueReminderMode;
        private ItemGrid _task;
        private string _taskDescription;
        private string _inactiveTime;
        private string _reminderMessage;
        private List<string> _suggestions;
        private bool _hasDecompositionSuggestion;
        
        public ItemGrid Task 
        { 
            get => _task; 
            set 
            { 
                _task = value; 
                OnPropertyChanged(nameof(Task));
                UpdateTaskInfo();
            } 
        }
        
        public string TaskDescription
        {
            get => _taskDescription;
            set
            {
                _taskDescription = value;
                OnPropertyChanged(nameof(TaskDescription));
            }
        }
        
        public string InactiveTime
        {
            get => _inactiveTime;
            set
            {
                _inactiveTime = value;
                OnPropertyChanged(nameof(InactiveTime));
            }
        }
        
        public string ReminderMessage
        {
            get => _reminderMessage;
            set
            {
                _reminderMessage = value;
                OnPropertyChanged(nameof(ReminderMessage));
            }
        }
        
        public List<string> Suggestions
        {
            get => _suggestions;
            set
            {
                _suggestions = value;
                OnPropertyChanged(nameof(Suggestions));
                OnPropertyChanged(nameof(HasSuggestions));
            }
        }
        
        public bool HasSuggestions => Suggestions != null && Suggestions.Any();
        
        public bool CanDecompose
        {
            get => _hasDecompositionSuggestion;
            set
            {
                _hasDecompositionSuggestion = value;
                OnPropertyChanged(nameof(CanDecompose));
            }
        }
        
        public TaskReminderResult Result { get; private set; } = TaskReminderResult.Dismissed;
        
        public TaskReminderWindow()
        {
            InitializeComponent();
            DataContext = this;
        }
        
        public TaskReminderWindow(ItemGrid task, string reminderMessage, List<string> suggestions) : this()
        {
            Task = task;
            ReminderMessage = reminderMessage;
            Suggestions = suggestions ?? new List<string>();
            
            // 检查是否有分解建议
            string[] decompositionKeywords = { "break it down", "decompose", "smaller pieces", "sub-tasks", "subtasks", "分解", "拆分" };
            CanDecompose = Suggestions.Any(s => decompositionKeywords.Any(keyword => 
                s.ToLowerInvariant().Contains(keyword.ToLowerInvariant())));
        }

        public TaskReminderWindow(ItemGrid task, DateTime dueTime) : this()
        {
            _isDueReminderMode = true;
            Task = task;
            ReminderMessage = $"任务已到提醒时间（{dueTime:yyyy-MM-dd HH:mm}）。请选择下一步操作。";
            Suggestions = new List<string>
            {
                "确认提醒：本次提醒完成并清除提醒时间",
                "延后30分钟：保持任务提醒并自动顺延",
                "编辑时间：重新设置提醒时间，避免误触发"
            };
            CanDecompose = false;
            ApplyDueReminderMode(dueTime);
        }
        
        private void UpdateTaskInfo()
        {
            if (Task == null) return;
            
            TaskDescription = Task.Task;
            
            var inactiveDuration = DateTime.Now - Task.LastModifiedDate;
            if (inactiveDuration.TotalDays >= 7)
            {
                int weeks = (int)(inactiveDuration.TotalDays / 7);
                InactiveTime = $"{weeks} 周未更新";
            }
            else if (inactiveDuration.TotalDays >= 1)
            {
                InactiveTime = $"{(int)inactiveDuration.TotalDays} 天未更新";
            }
            else if (inactiveDuration.TotalHours >= 1)
            {
                InactiveTime = $"{(int)inactiveDuration.TotalHours} 小时未更新";
            }
            else
            {
                InactiveTime = "不到1小时未更新";
            }
        }

        private void ApplyDueReminderMode(DateTime dueTime)
        {
            WindowTitleText.Text = "⏰ 定时提醒确认";
            TaskMetaLabelText.Text = "原定提醒时间:";
            InactiveTime = dueTime.ToString("yyyy-MM-dd HH:mm");

            CompleteTaskButton.Content = "✓ 确认提醒";
            UpdateTaskButton.Content = "⏱ 延后30分钟";
            DecomposeTaskButton.Visibility = Visibility.Collapsed;
            SnoozeButton.Content = "🗓 编辑时间";
            CloseButton.Content = "关闭";
        }
        
        private void CompleteTaskButton_Click(object sender, RoutedEventArgs e)
        {
            Result = _isDueReminderMode ? TaskReminderResult.ReminderConfirmed : TaskReminderResult.Completed;
            DialogResult = true;
            Close();
        }
        
        private void UpdateTaskButton_Click(object sender, RoutedEventArgs e)
        {
            Result = _isDueReminderMode ? TaskReminderResult.ReminderPostponed : TaskReminderResult.Updated;
            DialogResult = true;
            Close();
        }
        
        private void DecomposeTaskButton_Click(object sender, RoutedEventArgs e)
        {
            Result = TaskReminderResult.Decompose;
            DialogResult = true;
            Close();
        }
        
        private void SnoozeButton_Click(object sender, RoutedEventArgs e)
        {
            Result = _isDueReminderMode ? TaskReminderResult.ReminderEditTime : TaskReminderResult.Snoozed;
            DialogResult = true;
            Close();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Result = TaskReminderResult.Dismissed;
            DialogResult = false;
            Close();
        }
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public enum TaskReminderResult
    {
        Dismissed,
        Completed,
        Updated,
        Decompose,
        Snoozed,
        ReminderConfirmed,
        ReminderPostponed,
        ReminderEditTime
    }
}
