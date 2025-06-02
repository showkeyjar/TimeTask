using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace TimeTask
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); // Call base implementation

            // Check for API Key configuration
            string apiKey = System.Configuration.ConfigurationManager.AppSettings["OpenAIApiKey"];
            const string PlaceholderApiKey = "YOUR_API_KEY_GOES_HERE";

            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == PlaceholderApiKey)
            {
                System.Windows.MessageBox.Show(
                    "The OpenAI API key is not configured or is using the placeholder value. " +
                    "LLM-powered features like smart suggestions and automatic task classification will use dummy responses or may not function correctly. " +
                    "Please refer to the readme.md file for instructions on how to set up your API key in the App.config file.",
                    "API Key Configuration Warning",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning
                );
            }
        }
    }
}
