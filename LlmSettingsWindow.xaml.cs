using System;
using System.Configuration;
using System.Windows;
using System.Collections.Generic; // Added for List<string>
using Betalgo.Ranul.OpenAI; // For OpenAIService, OpenAIOptions
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels; // For ChatCompletionCreateRequest, ChatMessage
using System.Threading.Tasks; // For Task
using System.Linq; // For FirstOrDefault
using System.Text.Json; // Added for JsonException


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
            EnableTeamSyncCheckBox.Click += EnableTeamSyncCheckBox_Click;
        }

        private void UpdateTeamSyncControlsEnabledState()
        {
            bool isEnabled = EnableTeamSyncCheckBox.IsChecked ?? false;
            if (TeamSyncDetailsPanel != null) // Check if the panel exists (it should if XAML is updated)
            {
                TeamSyncDetailsPanel.IsEnabled = isEnabled;
            }
            else // Fallback if panel wasn't created, disable individual controls
            {
                TeamRoleComboBox.IsEnabled = isEnabled;
                DbHostTextBox.IsEnabled = isEnabled;
                DbPortTextBox.IsEnabled = isEnabled;
                DbNameTextBox.IsEnabled = isEnabled;
                DbUserTextBox.IsEnabled = isEnabled;
                DbPasswordBox.IsEnabled = isEnabled;
                SyncIntervalTextBox.IsEnabled = isEnabled;
            }
        }

        private void EnableTeamSyncCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateTeamSyncControlsEnabledState();
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
            List<string> agileRoles = new List<string> {
                "Developer",
                "Product Owner",
                "Scrum Master",
                "QA/Tester",
                "Analyst",
                "Team Lead",
                "Designer",
                "DevOps Engineer",
                "Any", // For roles not explicitly listed or to see all tasks
                "Unassigned" // For tasks specifically marked as unassigned
            };
            TeamRoleComboBox.ItemsSource = agileRoles;

            string savedRole = string.Empty;
            try
            {
                savedRole = (string)Properties.Settings.Default["TeamRole"];
                if (!string.IsNullOrEmpty(savedRole) && agileRoles.Contains(savedRole))
                {
                    TeamRoleComboBox.SelectedItem = savedRole;
                }
                else if (agileRoles.Count > 0)
                {
                    TeamRoleComboBox.SelectedIndex = 0; // Default to the first role
                }
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'TeamRole' not found. Defaulting to first item. Error: {ex.Message}");
                if (agileRoles.Count > 0) TeamRoleComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'TeamRole'. Defaulting to first item. Error: {ex.Message}");
                if (agileRoles.Count > 0) TeamRoleComboBox.SelectedIndex = 0;
            }

            // DbHost
            string dbHost = string.Empty;
            try
            {
                dbHost = (string)Properties.Settings.Default["DbHost"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbHost' not found. Defaulting to 'localhost'. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbHost'. Defaulting to 'localhost'. Error: {ex.Message}");
            }
            DbHostTextBox.Text = !string.IsNullOrWhiteSpace(dbHost) ? dbHost : "localhost";

            // DbPort
            string dbPort = string.Empty;
            try
            {
                dbPort = (string)Properties.Settings.Default["DbPort"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbPort' not found. Defaulting to '5432'. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbPort'. Defaulting to '5432'. Error: {ex.Message}");
            }
            DbPortTextBox.Text = !string.IsNullOrWhiteSpace(dbPort) ? dbPort : "5432";

            // DbName
            string dbName = string.Empty;
            try
            {
                dbName = (string)Properties.Settings.Default["DbName"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbName' not found. Defaulting to 'team_tasks_db'. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbName'. Defaulting to 'team_tasks_db'. Error: {ex.Message}");
            }
            DbNameTextBox.Text = !string.IsNullOrWhiteSpace(dbName) ? dbName : "team_tasks_db";

            // DbUser
            string dbUser = string.Empty;
            try
            {
                dbUser = (string)Properties.Settings.Default["DbUser"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbUser' not found. Defaulting to 'postgres'. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbUser'. Defaulting to 'postgres'. Error: {ex.Message}");
            }
            DbUserTextBox.Text = !string.IsNullOrWhiteSpace(dbUser) ? dbUser : "postgres";

            // DbPassword
            string dbPassword = string.Empty;
            try
            {
                dbPassword = (string)Properties.Settings.Default["DbPassword"];
            }
            catch (System.Configuration.SettingsPropertyNotFoundException ex)
            {
                Console.WriteLine($"INFO: Settings property 'DbPassword' not found. Defaulting to '123456'. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Error loading setting 'DbPassword'. Defaulting to '123456'. Error: {ex.Message}");
            }
            DbPasswordBox.Password = !string.IsNullOrWhiteSpace(dbPassword) ? dbPassword : "123456";

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
            UpdateTeamSyncControlsEnabledState(); // Call after all settings are loaded
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
                try { Properties.Settings.Default["EnableTeamSync"] = EnableTeamSyncCheckBox.IsChecked ?? false; }
                catch (System.Configuration.SettingsPropertyNotFoundException ex) { Console.WriteLine($"ERROR: Could not save setting 'EnableTeamSync'. Property not found. Error: {ex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"ERROR: Could not save setting 'EnableTeamSync'. General error. Error: {ex.Message}"); }

                try
                {
                    if (TeamRoleComboBox.SelectedItem != null)
                    {
                        Properties.Settings.Default["TeamRole"] = TeamRoleComboBox.SelectedItem as string;
                    }
                    else
                    {
                        Properties.Settings.Default["TeamRole"] = string.Empty;
                    }
                }
                catch (System.Configuration.SettingsPropertyNotFoundException ex) { Console.WriteLine($"ERROR: Could not save setting 'TeamRole'. Property not found. Error: {ex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"ERROR: Could not save setting 'TeamRole'. General error. Error: {ex.Message}"); }

                try { Properties.Settings.Default["DbHost"] = DbHostTextBox.Text; }
                catch (System.Configuration.SettingsPropertyNotFoundException ex) { Console.WriteLine($"ERROR: Could not save setting 'DbHost'. Property not found. Error: {ex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"ERROR: Could not save setting 'DbHost'. General error. Error: {ex.Message}"); }

                try { Properties.Settings.Default["DbPort"] = DbPortTextBox.Text; }
                catch (System.Configuration.SettingsPropertyNotFoundException ex) { Console.WriteLine($"ERROR: Could not save setting 'DbPort'. Property not found. Error: {ex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"ERROR: Could not save setting 'DbPort'. General error. Error: {ex.Message}"); }

                try { Properties.Settings.Default["DbName"] = DbNameTextBox.Text; }
                catch (System.Configuration.SettingsPropertyNotFoundException ex) { Console.WriteLine($"ERROR: Could not save setting 'DbName'. Property not found. Error: {ex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"ERROR: Could not save setting 'DbName'. General error. Error: {ex.Message}"); }

                try { Properties.Settings.Default["DbUser"] = DbUserTextBox.Text; }
                catch (System.Configuration.SettingsPropertyNotFoundException ex) { Console.WriteLine($"ERROR: Could not save setting 'DbUser'. Property not found. Error: {ex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"ERROR: Could not save setting 'DbUser'. General error. Error: {ex.Message}"); }

                try { Properties.Settings.Default["DbPassword"] = DbPasswordBox.Password; }
                catch (System.Configuration.SettingsPropertyNotFoundException ex) { Console.WriteLine($"ERROR: Could not save setting 'DbPassword'. Property not found. Error: {ex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"ERROR: Could not save setting 'DbPassword'. General error. Error: {ex.Message}"); }

                try
                {
                    if (int.TryParse(SyncIntervalTextBox.Text, out int interval))
                    {
                        Properties.Settings.Default["SyncIntervalMinutes"] = interval;
                    }
                    else
                    {
                        Properties.Settings.Default["SyncIntervalMinutes"] = 30; // Default
                        MessageBox.Show(this, "Invalid Sync Interval. It has been reset to default (30 minutes).", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        SyncIntervalTextBox.Text = "30"; // Update UI
                    }
                }
                catch (System.Configuration.SettingsPropertyNotFoundException ex) { Console.WriteLine($"ERROR: Could not save setting 'SyncIntervalMinutes'. Property not found. Error: {ex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"ERROR: Could not save setting 'SyncIntervalMinutes'. General error. Error: {ex.Message}"); }

                try
                {
                    string connString = $"Host={DbHostTextBox.Text};Port={DbPortTextBox.Text};Username={DbUserTextBox.Text};Password={DbPasswordBox.Password};Database={DbNameTextBox.Text};";
                    Properties.Settings.Default["TeamTasksDbConnectionString"] = connString;
                }
                catch (System.Configuration.SettingsPropertyNotFoundException ex) { Console.WriteLine($"ERROR: Could not save setting 'TeamTasksDbConnectionString'. Property not found. Error: {ex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"ERROR: Could not save setting 'TeamTasksDbConnectionString'. General error. Error: {ex.Message}"); }

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

        private async void TestLlmConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            TestLlmConnectionButton.IsEnabled = false;
            TestResultTextBlock.Text = "Testing connection, please wait...";

            string apiKey = ApiKeyTextBox.Text;
            string apiBaseUrl = ApiBaseUrlTextBox.Text; // This can be empty
            string modelName = ModelNameTextBox.Text;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                TestResultTextBlock.Text = "Error: API Key cannot be empty.";
                TestLlmConnectionButton.IsEnabled = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                // Use a default model if empty, as LlmService does, or show error
                // For testing, it's better to ensure user provides one or use a known valid default
                modelName = "gpt-3.5-turbo"; // A common default
                TestResultTextBlock.Text = $"Info: Model Name was empty, using default '{modelName}'.\n";
            } else {
                TestResultTextBlock.Text = ""; // Clear previous messages if any
            }


            try
            {
                var options = new OpenAIOptions()
                {
                    ApiKey = apiKey
                };

                if (!string.IsNullOrWhiteSpace(apiBaseUrl))
                {
                    options.BaseDomain = apiBaseUrl;
                }

                // Use a specific provider for the temporary service, not the one from LlmService global config.
                // The Betalgo library itself handles the OpenAI endpoint by default if BaseDomain is not set.
                var tempLlmService = new Betalgo.Ranul.OpenAI.Managers.OpenAIService(options);

                var completionResult = await tempLlmService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
                {
                    Messages = new List<ChatMessage>
                    {
                        ChatMessage.FromSystem("You are a helpful assistant."), // Optional system message
                        ChatMessage.FromUser("Hello!")
                    },
                    Model = modelName,
                    MaxTokens = 50 // Keep it short and low-cost
                });

                if (completionResult.Successful)
                {
                    string responseMessage = completionResult.Choices.FirstOrDefault()?.Message.Content ?? "No content received.";
                    TestResultTextBlock.Text += $"Success! LLM responded: \"{responseMessage.Substring(0, Math.Min(responseMessage.Length, 100))}...\"";
                }
                else
                {
                    try
                    {
                        if (completionResult.Error != null)
                        {
                            // Accessing these properties might trigger JsonException if the Error object itself is malformed
                            string errorMessage = completionResult.Error.Message;
                            string errorCode = completionResult.Error.Code;
                            string errorType = completionResult.Error.Type;
                            TestResultTextBlock.Text += $"Test failed. Error: {errorMessage} (Code: {errorCode}, Type: {errorType})";
                        }
                        else
                        {
                            TestResultTextBlock.Text += "Test failed. LLM indicated failure but returned no structured error details.";
                            // Consider if there's any raw response part on completionResult to log.
                        }
                    }
                    catch (System.Text.Json.JsonException jsonExInner)
                    {
                        TestResultTextBlock.Text += $"Test failed. Could not parse the specific error details from LLM's error response. Path: {jsonExInner.Path}, Line: {jsonExInner.LineNumber}, Pos: {jsonExInner.BytePositionInLine}. Details: {jsonExInner.Message}";
                    }
                }
            }
            catch (System.Text.Json.JsonException jsonExOuter)
            {
                string detailedErrorMessage = $"Path: {jsonExOuter.Path}, Line: {jsonExOuter.LineNumber}, Pos: {jsonExOuter.BytePositionInLine}. Details: {jsonExOuter.Message}";
                if (jsonExOuter.Path == "$.error")
                {
                    TestResultTextBlock.Text += "Test failed. The LLM returned an error, but its format could not be understood by the client library. " +
                                                "This can happen with custom API Base URLs if the LLM's error structure differs from the standard OpenAI format. " +
                                                $"Technical details: {detailedErrorMessage}";
                }
                else
                {
                    TestResultTextBlock.Text += $"Test failed. Could not parse the entire LLM response. {detailedErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                TestResultTextBlock.Text += $"Test failed. An unexpected exception occurred: {ex.ToString()}";
                // Consider logging ex.ToString() for more details if a logging mechanism exists
            }
            finally
            {
                TestLlmConnectionButton.IsEnabled = true;
            }
        }
    }
}
