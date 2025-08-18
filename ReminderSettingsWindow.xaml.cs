using System;
using System.ComponentModel;
using System.Windows;

namespace TimeTask
{
    public partial class ReminderSettingsWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private int _firstWarningAfterDays;
        private int _secondWarningAfterDays;
        private int _staleTaskThresholdDays;
        private int _maxInactiveWarnings;
        private int _reminderCheckIntervalMinutes;
        
        public int FirstWarningAfterDays
        {
            get => _firstWarningAfterDays;
            set
            {
                _firstWarningAfterDays = value;
                OnPropertyChanged(nameof(FirstWarningAfterDays));
            }
        }
        
        public int SecondWarningAfterDays
        {
            get => _secondWarningAfterDays;
            set
            {
                _secondWarningAfterDays = value;
                OnPropertyChanged(nameof(SecondWarningAfterDays));
            }
        }
        
        public int StaleTaskThresholdDays
        {
            get => _staleTaskThresholdDays;
            set
            {
                _staleTaskThresholdDays = value;
                OnPropertyChanged(nameof(StaleTaskThresholdDays));
            }
        }
        
        public int MaxInactiveWarnings
        {
            get => _maxInactiveWarnings;
            set
            {
                _maxInactiveWarnings = value;
                OnPropertyChanged(nameof(MaxInactiveWarnings));
            }
        }
        
        public int ReminderCheckIntervalMinutes
        {
            get => _reminderCheckIntervalMinutes;
            set
            {
                _reminderCheckIntervalMinutes = value;
                OnPropertyChanged(nameof(ReminderCheckIntervalMinutes));
            }
        }
        
        public ReminderSettingsWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadCurrentSettings();
        }
        
        private void LoadCurrentSettings()
        {
            try
            {
                FirstWarningAfterDays = Properties.Settings.Default.FirstWarningAfterDays;
                SecondWarningAfterDays = Properties.Settings.Default.SecondWarningAfterDays;
                StaleTaskThresholdDays = Properties.Settings.Default.StaleTaskThresholdDays;
                MaxInactiveWarnings = Properties.Settings.Default.MaxInactiveWarnings;
                ReminderCheckIntervalMinutes = Properties.Settings.Default.ReminderCheckIntervalSeconds / 60;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading reminder settings: {ex.Message}");
                LoadDefaultSettings();
            }
        }
        
        private void LoadDefaultSettings()
        {
            FirstWarningAfterDays = 1;
            SecondWarningAfterDays = 3;
            StaleTaskThresholdDays = 14;
            MaxInactiveWarnings = 3;
            ReminderCheckIntervalMinutes = 5;
        }
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateSettings())
            {
                try
                {
                    Properties.Settings.Default.FirstWarningAfterDays = FirstWarningAfterDays;
                    Properties.Settings.Default.SecondWarningAfterDays = SecondWarningAfterDays;
                    Properties.Settings.Default.StaleTaskThresholdDays = StaleTaskThresholdDays;
                    Properties.Settings.Default.MaxInactiveWarnings = MaxInactiveWarnings;
                    Properties.Settings.Default.ReminderCheckIntervalSeconds = ReminderCheckIntervalMinutes * 60;
                    
                    Properties.Settings.Default.Save();
                    
                    MessageBox.Show("设置已保存成功！重启应用程序后生效。", "保存成功", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving reminder settings: {ex.Message}");
                    MessageBox.Show("保存设置时发生错误，请重试。", "保存失败", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private bool ValidateSettings()
        {
            if (FirstWarningAfterDays < 1 || FirstWarningAfterDays > 30)
            {
                MessageBox.Show("第一次提醒间隔必须在1-30天之间。", "设置错误", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (SecondWarningAfterDays < FirstWarningAfterDays || SecondWarningAfterDays > 60)
            {
                MessageBox.Show("第二次提醒间隔必须大于第一次提醒间隔且不超过60天。", "设置错误", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (StaleTaskThresholdDays < SecondWarningAfterDays || StaleTaskThresholdDays > 365)
            {
                MessageBox.Show("任务过期阈值必须大于第二次提醒间隔且不超过365天。", "设置错误", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (MaxInactiveWarnings < 1 || MaxInactiveWarnings > 10)
            {
                MessageBox.Show("最大提醒次数必须在1-10次之间。", "设置错误", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (ReminderCheckIntervalMinutes < 1 || ReminderCheckIntervalMinutes > 60)
            {
                MessageBox.Show("检查间隔必须在1-60分钟之间。", "设置错误", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            return true;
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要恢复默认设置吗？", "确认重置", 
                                       MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                LoadDefaultSettings();
            }
        }
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}