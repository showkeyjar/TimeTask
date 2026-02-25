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
            ReminderMessage = I18n.Tf("TaskReminder_DueMessageFormat", dueTime);
            Suggestions = new List<string>
            {
                I18n.T("TaskReminder_DueSuggestionConfirm"),
                I18n.T("TaskReminder_DueSuggestionPostpone"),
                I18n.T("TaskReminder_DueSuggestionEdit")
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
                InactiveTime = I18n.Tf("TaskReminder_InactiveWeeksFormat", weeks);
            }
            else if (inactiveDuration.TotalDays >= 1)
            {
                InactiveTime = I18n.Tf("TaskReminder_InactiveDaysFormat", (int)inactiveDuration.TotalDays);
            }
            else if (inactiveDuration.TotalHours >= 1)
            {
                InactiveTime = I18n.Tf("TaskReminder_InactiveHoursFormat", (int)inactiveDuration.TotalHours);
            }
            else
            {
                InactiveTime = I18n.T("TaskReminder_InactiveLessThanHour");
            }
        }

        private void ApplyDueReminderMode(DateTime dueTime)
        {
            WindowTitleText.Text = I18n.T("TaskReminder_DueHeader");
            TaskMetaLabelText.Text = I18n.T("TaskReminder_DueOriginalTimeLabel");
            InactiveTime = dueTime.ToString("yyyy-MM-dd HH:mm");

            CompleteTaskButton.Content = I18n.T("TaskReminder_DueButtonConfirm");
            UpdateTaskButton.Content = I18n.T("TaskReminder_DueButtonPostpone");
            DecomposeTaskButton.Visibility = Visibility.Collapsed;
            SnoozeButton.Content = I18n.T("TaskReminder_DueButtonEdit");
            CloseButton.Content = I18n.T("TaskReminder_ButtonClose");
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
