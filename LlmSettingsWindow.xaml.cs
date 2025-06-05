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
        }

        private void LoadSettings()
        {
            try
            {
                ApiKeyTextBox.Text = ConfigurationManager.AppSettings["OpenAIApiKey"] ?? string.Empty;
                ApiBaseUrlTextBox.Text = ConfigurationManager.AppSettings["LlmApiBaseUrl"] ?? string.Empty;
                ModelNameTextBox.Text = ConfigurationManager.AppSettings["LlmModelName"] ?? "gpt-3.5-turbo";
            }
            catch (ConfigurationErrorsException ex)
            {
                MessageBox.Show(this, "Error loading configuration: " + ex.Message, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                AppSettingsSection appSettings = config.AppSettings;

                // Add or update settings
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

                // Ensure LlmProvider key exists, defaulting to "zhipu" or empty if user clears it
                // This key is not directly used by LlmService's core logic for Betalgo library but is in App.config
                // For consistency with the existing App.config, we manage it.
                // A more advanced settings UI might have a dropdown for known providers that then sets URL/model.
                string providerValue = string.Empty; // Default to empty if not "zhipu" or similar
                if (!string.IsNullOrWhiteSpace(ApiBaseUrlTextBox.Text) && ApiBaseUrlTextBox.Text.Contains("bigmodel.cn"))
                {
                    providerValue = "zhipu";
                }
                else if (string.IsNullOrWhiteSpace(ApiBaseUrlTextBox.Text) || ApiBaseUrlTextBox.Text.Contains("api.openai.com"))
                {
                    providerValue = "openai";
                }
                // If other known providers are added, their conditions can be here.

                if (appSettings.Settings["LlmProvider"] == null)
                    appSettings.Settings.Add("LlmProvider", providerValue);
                else
                    appSettings.Settings["LlmProvider"].Value = providerValue;


                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");

                SettingsSaved = true;
                MessageBox.Show(this, "Settings saved successfully. The application may need to re-initialize services to use new settings.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true; // Automatically closes the window and signals success
                this.Close();
            }
            catch (ConfigurationErrorsException ex)
            {
                MessageBox.Show(this, "Error saving configuration: " + ex.Message, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
