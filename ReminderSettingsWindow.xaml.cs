using System;
using System.ComponentModel;
using System.Configuration;
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
        private string _metricsWindowLabel = string.Empty;
        private string _suggestionHitRateText = "0.0%";
        private string _interruptionIndexText = "0.0%";
        private string _topEffectiveActionText = "N/A";
        private string _suggestionOutcomeText = string.Empty;
        private string _recommendedStuckThresholdText = string.Empty;
        private string _recommendedDailyNudgeLimitText = string.Empty;
        private bool _proactiveAssistEnabled = true;
        private bool _behaviorLearningEnabled = true;
        private bool _stuckNudgesEnabled = true;
        private bool _llmSkillAssistEnabled = true;
        private int _quietHoursStart = 22;
        private int _quietHoursEnd = 8;
        
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

        public string MetricsWindowLabel
        {
            get => _metricsWindowLabel;
            set
            {
                _metricsWindowLabel = value;
                OnPropertyChanged(nameof(MetricsWindowLabel));
            }
        }

        public string SuggestionHitRateText
        {
            get => _suggestionHitRateText;
            set
            {
                _suggestionHitRateText = value;
                OnPropertyChanged(nameof(SuggestionHitRateText));
            }
        }

        public string InterruptionIndexText
        {
            get => _interruptionIndexText;
            set
            {
                _interruptionIndexText = value;
                OnPropertyChanged(nameof(InterruptionIndexText));
            }
        }

        public string TopEffectiveActionText
        {
            get => _topEffectiveActionText;
            set
            {
                _topEffectiveActionText = value;
                OnPropertyChanged(nameof(TopEffectiveActionText));
            }
        }

        public string SuggestionOutcomeText
        {
            get => _suggestionOutcomeText;
            set
            {
                _suggestionOutcomeText = value;
                OnPropertyChanged(nameof(SuggestionOutcomeText));
            }
        }

        public string RecommendedStuckThresholdText
        {
            get => _recommendedStuckThresholdText;
            set
            {
                _recommendedStuckThresholdText = value;
                OnPropertyChanged(nameof(RecommendedStuckThresholdText));
            }
        }

        public string RecommendedDailyNudgeLimitText
        {
            get => _recommendedDailyNudgeLimitText;
            set
            {
                _recommendedDailyNudgeLimitText = value;
                OnPropertyChanged(nameof(RecommendedDailyNudgeLimitText));
            }
        }

        public bool ProactiveAssistEnabled
        {
            get => _proactiveAssistEnabled;
            set
            {
                _proactiveAssistEnabled = value;
                OnPropertyChanged(nameof(ProactiveAssistEnabled));
            }
        }

        public bool BehaviorLearningEnabled
        {
            get => _behaviorLearningEnabled;
            set
            {
                _behaviorLearningEnabled = value;
                OnPropertyChanged(nameof(BehaviorLearningEnabled));
            }
        }

        public bool StuckNudgesEnabled
        {
            get => _stuckNudgesEnabled;
            set
            {
                _stuckNudgesEnabled = value;
                OnPropertyChanged(nameof(StuckNudgesEnabled));
            }
        }

        public bool LlmSkillAssistEnabled
        {
            get => _llmSkillAssistEnabled;
            set
            {
                _llmSkillAssistEnabled = value;
                OnPropertyChanged(nameof(LlmSkillAssistEnabled));
            }
        }

        public int QuietHoursStart
        {
            get => _quietHoursStart;
            set
            {
                _quietHoursStart = value;
                OnPropertyChanged(nameof(QuietHoursStart));
            }
        }

        public int QuietHoursEnd
        {
            get => _quietHoursEnd;
            set
            {
                _quietHoursEnd = value;
                OnPropertyChanged(nameof(QuietHoursEnd));
            }
        }
        
        public ReminderSettingsWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeLocalizedDefaults();
            LoadCurrentSettings();
        }

        private void InitializeLocalizedDefaults()
        {
            MetricsWindowLabel = I18n.Tf("ReminderSettings_MetricsWindowFormat", 7);
            SuggestionOutcomeText = I18n.T("ReminderSettings_OutcomeDefault");
            RecommendedStuckThresholdText = I18n.Tf("ReminderSettings_MinutesFormat", 90);
            RecommendedDailyNudgeLimitText = I18n.Tf("ReminderSettings_PerDayFormat", 2);
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
                LoadProactiveSettings();
                LoadProfileMetrics();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading reminder settings: {ex.Message}");
                LoadDefaultSettings();
                LoadProactiveSettings();
                LoadProfileMetrics();
            }
        }
        
        private void LoadDefaultSettings()
        {
            FirstWarningAfterDays = 1;
            SecondWarningAfterDays = 3;
            StaleTaskThresholdDays = 14;
            MaxInactiveWarnings = 3;
            ReminderCheckIntervalMinutes = 5;
            ProactiveAssistEnabled = true;
            BehaviorLearningEnabled = true;
            StuckNudgesEnabled = true;
            LlmSkillAssistEnabled = true;
            QuietHoursStart = 22;
            QuietHoursEnd = 8;
        }

        private void LoadProactiveSettings()
        {
            ProactiveAssistEnabled = GetAppSettingBool("ProactiveAssistEnabled", true);
            BehaviorLearningEnabled = GetAppSettingBool("BehaviorLearningEnabled", true);
            StuckNudgesEnabled = GetAppSettingBool("StuckNudgesEnabled", true);
            LlmSkillAssistEnabled = GetAppSettingBool("LlmSkillAssistEnabled", true);
            QuietHoursStart = GetAppSettingInt("QuietHoursStart", 22, 0, 23);
            QuietHoursEnd = GetAppSettingInt("QuietHoursEnd", 8, 0, 23);
        }

        private static bool GetAppSettingBool(string key, bool defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
        }

        private static int GetAppSettingInt(string key, int defaultValue, int min, int max)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (int.TryParse(value, out int parsed))
            {
                return Math.Max(min, Math.Min(max, parsed));
            }

            return defaultValue;
        }

        private void LoadProfileMetrics()
        {
            try
            {
                var manager = new UserProfileManager();
                var metrics = manager.GetDashboardMetrics(7);

                MetricsWindowLabel = I18n.Tf("ReminderSettings_MetricsWindowFormat", metrics.WindowDays);
                SuggestionHitRateText = $"{metrics.HitRate:P1}";
                InterruptionIndexText = $"{metrics.InterruptionIndex:P1}";
                TopEffectiveActionText = ToActionLabel(metrics.TopEffectiveActionId);
                SuggestionOutcomeText = I18n.Tf(
                    "ReminderSettings_OutcomeFormat",
                    metrics.SuggestionsShown,
                    metrics.SuggestionsAccepted,
                    metrics.SuggestionsDeferred,
                    metrics.SuggestionsRejected);

                var recommendation = manager.GetAdaptiveNudgeRecommendation(7);
                RecommendedStuckThresholdText = I18n.Tf("ReminderSettings_MinutesFormat", recommendation.RecommendedStuckThresholdMinutes);
                RecommendedDailyNudgeLimitText = I18n.Tf("ReminderSettings_PerDayFormat", recommendation.RecommendedDailyNudgeLimit);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading profile metrics: {ex.Message}");
                MetricsWindowLabel = I18n.Tf("ReminderSettings_MetricsWindowFormat", 7);
                SuggestionHitRateText = "N/A";
                InterruptionIndexText = "N/A";
                TopEffectiveActionText = "N/A";
                SuggestionOutcomeText = "N/A";
                RecommendedStuckThresholdText = "N/A";
                RecommendedDailyNudgeLimitText = "N/A";
            }
        }

        private static string ToActionLabel(string actionId)
        {
            switch ((actionId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "start_10_min": return I18n.T("ReminderSettings_ActionStart10");
                case "split_20_min": return I18n.T("ReminderSettings_ActionSplit20");
                case "delegate_or_drop": return I18n.T("ReminderSettings_ActionDelegate");
                case "pause_and_switch": return I18n.T("ReminderSettings_ActionPauseSwitch");
                case "decision_now": return I18n.T("ReminderSettings_ActionDecisionNow");
                case "fallback_min_step": return I18n.T("ReminderSettings_ActionFallback");
                case "n/a":
                case "":
                    return "N/A";
                default:
                    return actionId;
            }
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
                    SaveProactiveSettings();
                    
                    Properties.Settings.Default.Save();
                    
                    MessageBox.Show(I18n.T("ReminderSettings_SaveSuccess"), I18n.T("ReminderSettings_TitleSaveSuccess"),
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving reminder settings: {ex.Message}");
                    MessageBox.Show(I18n.T("ReminderSettings_SaveFailed"), I18n.T("ReminderSettings_TitleSaveFailed"),
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private bool ValidateSettings()
        {
            if (FirstWarningAfterDays < 1 || FirstWarningAfterDays > 30)
            {
                MessageBox.Show(I18n.T("ReminderSettings_ValidationFirstWarning"), I18n.T("ReminderSettings_TitleValidation"), 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (SecondWarningAfterDays < FirstWarningAfterDays || SecondWarningAfterDays > 60)
            {
                MessageBox.Show(I18n.T("ReminderSettings_ValidationSecondWarning"), I18n.T("ReminderSettings_TitleValidation"), 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (StaleTaskThresholdDays < SecondWarningAfterDays || StaleTaskThresholdDays > 365)
            {
                MessageBox.Show(I18n.T("ReminderSettings_ValidationStaleThreshold"), I18n.T("ReminderSettings_TitleValidation"), 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (MaxInactiveWarnings < 1 || MaxInactiveWarnings > 10)
            {
                MessageBox.Show(I18n.T("ReminderSettings_ValidationMaxWarnings"), I18n.T("ReminderSettings_TitleValidation"), 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            if (ReminderCheckIntervalMinutes < 1 || ReminderCheckIntervalMinutes > 60)
            {
                MessageBox.Show(I18n.T("ReminderSettings_ValidationInterval"), I18n.T("ReminderSettings_TitleValidation"), 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (QuietHoursStart < 0 || QuietHoursStart > 23 || QuietHoursEnd < 0 || QuietHoursEnd > 23)
            {
                MessageBox.Show(I18n.T("ReminderSettings_ValidationQuietHours"), I18n.T("ReminderSettings_TitleValidation"),
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            
            return true;
        }

        private void SaveProactiveSettings()
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = config.AppSettings.Settings;

            SetOrAddAppSetting(settings, "ProactiveAssistEnabled", ProactiveAssistEnabled ? "true" : "false");
            SetOrAddAppSetting(settings, "BehaviorLearningEnabled", BehaviorLearningEnabled ? "true" : "false");
            SetOrAddAppSetting(settings, "StuckNudgesEnabled", StuckNudgesEnabled ? "true" : "false");
            SetOrAddAppSetting(settings, "LlmSkillAssistEnabled", LlmSkillAssistEnabled ? "true" : "false");
            SetOrAddAppSetting(settings, "QuietHoursStart", QuietHoursStart.ToString());
            SetOrAddAppSetting(settings, "QuietHoursEnd", QuietHoursEnd.ToString());

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        private static void SetOrAddAppSetting(KeyValueConfigurationCollection settings, string key, string value)
        {
            if (settings[key] == null)
            {
                settings.Add(key, value);
            }
            else
            {
                settings[key].Value = value;
            }
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(I18n.T("ReminderSettings_ResetConfirm"), I18n.T("ReminderSettings_TitleResetConfirm"), 
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
