using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using TimeTask.Services;

namespace TimeTask
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider? ServiceProvider { get; private set; }
        
        private StickyNotesManager? _stickyNotesManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Initialize sticky notes manager
            _stickyNotesManager = new StickyNotesManager();
            
            // Set up dependency injection
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            ServiceProvider = serviceCollection.BuildServiceProvider();
            
            // Create main window and show it
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Clean up resources
            if (_stickyNotesManager != null)
            {
                _stickyNotesManager = null;
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Add logging
            services.AddLogging(configure => 
                configure.AddDebug()
                        .SetMinimumLevel(LogLevel.Debug));
            
            // Add services
            services.AddSingleton<Services.ILLMService>(provider => 
                new Services.ZhipuAIService(
                    provider.GetRequiredService<ILogger<Services.ZhipuAIService>>()));
            
            // Register AddTaskWindow
            services.AddTransient<AddTaskWindow>(provider => 
            {
                var llmService = provider.GetRequiredService<Services.ILLMService>();
                var logger = provider.GetRequiredService<ILogger<AddTaskWindow>>();
                return new AddTaskWindow(llmService, logger);
            });
            
            // Add main window with required services
            services.AddSingleton<MainWindow>(provider => 
            {
                var llmService = provider.GetRequiredService<Services.ILLMService>();
                var logger = provider.GetRequiredService<ILogger<MainWindow>>();
                var window = new MainWindow(llmService, logger);
                window.Closed += (s, e) => Current.Shutdown();
                return window;
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Clean up services
            (ServiceProvider as IDisposable)?.Dispose();
            base.OnExit(e);
        }
    }
}
