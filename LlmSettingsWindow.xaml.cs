using System;
using System.Configuration;
using System.Windows;

namespace TimeTask
{
    public partial class LlmSettingsWindow : Window
    {
        public bool SettingsSaved { get; private set; } = false;

        public LlmSettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
            // It's good practice to handle the Loaded event for UI element interactions if needed,
            // but for direct property setting based on loaded XAML, constructor is fine.
            // For this task, direct access after InitializeComponent is acceptable.
            // Consider adding a LlmSettingsWindow_Loaded event handler if more complex setup is needed.
        }

        private void LoadSettings()
        {
            // Load existing App.config settings
            try
            {
                ApiKeyTextBox.Text = ConfigurationManager.AppSettings["OpenAIApiKey"] ?? string.Empty;
                ApiBaseUrlTextBox.Text = ConfigurationManager.AppSettings["LlmApiBaseUrl"] ?? string.Empty;
                ModelNameTextBox.Text = ConfigurationManager.AppSettings["LlmModelName"] ?? "gpt-3.5-turbo";
            }
            catch (ConfigurationErrorsException ex)
            {
                MessageBox.Show(this, "Error loading App.config settings: " + ex.Message, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Load new Properties.Settings.Default settings
            // EnableTeamSync
            try
            {
                EnableTeamSyncCheckBox.IsChecked = (bool)Properties.Settings.Default["EnableTeamSync"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'EnableTeamSync' not found. Defaulting to false. Error: {ex.Message}");
                EnableTeamSyncCheckBox.IsChecked = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'EnableTeamSync'. Defaulting to false. Error: {ex.Message}");
                EnableTeamSyncCheckBox.IsChecked = false;
            }

            // TeamRole
            try
            {
                TeamRoleTextBox.Text = (string)Properties.Settings.Default["TeamRole"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'TeamRole' not found. Defaulting to empty. Error: {ex.Message}");
                TeamRoleTextBox.Text = string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'TeamRole'. Defaulting to empty. Error: {ex.Message}");
                TeamRoleTextBox.Text = string.Empty;
            }

            // DbHost
            try
            {
                DbHostTextBox.Text = (string)Properties.Settings.Default["DbHost"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbHost' not found. Defaulting to empty. Error: {ex.Message}");
                DbHostTextBox.Text = string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbHost'. Defaulting to empty. Error: {ex.Message}");
                DbHostTextBox.Text = string.Empty;
            }

            // DbPort
            try
            {
                DbPortTextBox.Text = (string)Properties.Settings.Default["DbPort"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbPort' not found. Defaulting to 5432. Error: {ex.Message}");
                DbPortTextBox.Text = "5432";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbPort'. Defaulting to 5432. Error: {ex.Message}");
                DbPortTextBox.Text = "5432";
            }

            // DbName
            try
            {
                DbNameTextBox.Text = (string)Properties.Settings.Default["DbName"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbName' not found. Defaulting to empty. Error: {ex.Message}");
                DbNameTextBox.Text = string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbName'. Defaulting to empty. Error: {ex.Message}");
                DbNameTextBox.Text = string.Empty;
            }

            // DbUser
            try
            {
                DbUserTextBox.Text = (string)Properties.Settings.Default["DbUser"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbUser' not found. Defaulting to empty. Error: {ex.Message}");
                DbUserTextBox.Text = string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbUser'. Defaulting to empty. Error: {ex.Message}");
                DbUserTextBox.Text = string.Empty;
            }

            // DbPassword
            try
            {
                DbPasswordBox.Password = (string)Properties.Settings.Default["DbPassword"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbPassword' not found. Defaulting to empty. Error: {ex.Message}");
                DbPasswordBox.Password = string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbPassword'. Defaulting to empty. Error: {ex.Message}");
                DbPasswordBox.Password = string.Empty;
            }

            // SyncIntervalMinutes
            try
            {
                SyncIntervalTextBox.Text = ((int)Properties.Settings.Default["SyncIntervalMinutes"]).ToString();
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'SyncIntervalMinutes' not found. Defaulting to 30. Error: {ex.Message}");
                SyncIntervalTextBox.Text = "30";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'SyncIntervalMinutes'. Defaulting to 30. Error: {ex.Message}");
                SyncIntervalTextBox.Text = "30";
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Save existing App.config settings
            try
            {
                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                AppSettingsSection appSettings = config.AppSettings;

                // Add or update App.config settings
                if (appSettings.Settings["OpenAIApiKey"] == null)
                    appSettings.Settings.Add("OpenAIApiKey", ApiKeyTextBox.Text);
                else
                    appSettings.Settings["OpenAIApiKey"].Value = ApiKeyTextBox.Text;

                if (appSettings.Settings["LlmApiBaseUrl"] == null)
                    appSettings.Settings.Add("LlmApiBaseUrl", ApiBaseUrlTextBox.Text);
                else
                    appSettings.Settings["LlmApiBaseUrl"].Value = ApiBaseUrlTextBox.Text;

                if (appSettings.Settings["LlmModelName"] == null)
                    appSettings.Settings.Add("LlmModelName", ModelNameTextBox.Text);
                else
                    appSettings.Settings["LlmModelName"].Value = ModelNameTextBox.Text;

                string providerValue = string.Empty;
                if (!string.IsNullOrWhiteSpace(ApiBaseUrlTextBox.Text) && ApiBaseUrlTextBox.Text.Contains("bigmodel.cn"))
                {
                    providerValue = "zhipu";
                }
                else if (string.IsNullOrWhiteSpace(ApiBaseUrlTextBox.Text) || ApiBaseUrlTextBox.Text.Contains("api.openai.com"))
                {
                    providerValue = "openai";
                }

                if (appSettings.Settings["LlmProvider"] == null)
                    appSettings.Settings.Add("LlmProvider", providerValue);
                else
                    appSettings.Settings["LlmProvider"].Value = providerValue;

                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (ConfigurationErrorsException ex)
            {
                MessageBox.Show(this, "Error saving App.config settings: " + ex.Message, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SettingsSaved = false;
                this.DialogResult = false;
                return; // Exit if App.config saving fails
            }

            // Save new Properties.Settings.Default settings
            try
            {
                Properties.Settings.Default["EnableTeamSync"] = EnableTeamSyncCheckBox.IsChecked ?? false;
                Properties.Settings.Default["TeamRole"] = TeamRoleTextBox.Text;
                Properties.Settings.Default["DbHost"] = DbHostTextBox.Text;
                Properties.Settings.Default["DbPort"] = DbPortTextBox.Text;
                Properties.Settings.Default["DbName"] = DbNameTextBox.Text;
                Properties.Settings.Default["DbUser"] = DbUserTextBox.Text;
                Properties.Settings.Default["DbPassword"] = DbPasswordBox.Password; // Store plain text

                if (int.TryParse(SyncIntervalTextBox.Text, out int interval))
                {
                    Properties.Settings.Default["SyncIntervalMinutes"] = interval;
                }
                else
                {
                    // Handle error or default if parsing fails
                    Properties.Settings.Default["SyncIntervalMinutes"] = 30; // Default
                    MessageBox.Show(this, "Invalid Sync Interval. It has been reset to default (30 minutes).", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SyncIntervalTextBox.Text = "30"; // Update UI
                }

                // Construct and save the connection string
                string connString = $"Host={DbHostTextBox.Text};Port={DbPortTextBox.Text};Username={DbUserTextBox.Text};Password={DbPasswordBox.Password};Database={DbNameTextBox.Text};";
                Properties.Settings.Default["TeamTasksDbConnectionString"] = connString;

                Properties.Settings.Default.Save();

                SettingsSaved = true;
                MessageBox.Show(this, "All settings saved successfully. The application may need to re-initialize services to use new settings.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true; // Automatically closes the window and signals success
                this.Close();
            }
            catch (Exception ex) // Catch a more general exception for property settings
            {
                MessageBox.Show(this, "Error saving user settings: " + ex.Message, "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SettingsSaved = false;
                this.DialogResult = false;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsSaved = false;
            this.DialogResult = false; // Automatically closes the window and signals cancellation
            this.Close();
        }
    }
}
