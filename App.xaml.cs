using System;
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
        // 增强型语音监听服务
        private EnhancedAudioCaptureService _enhancedAudioService;

        // 原有的录音服务（保留备用）
        private AudioCaptureService _legacyAudioService;

        // 任务草稿管理器
        private TaskDraftManager _draftManager;

        // 通知管理器
        private NotificationManager _notificationManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); // Call base implementation
            VoiceRuntimeLog.Info("App startup.");
            VoiceRuntimeLog.Info($"ProcessBitness: {(Environment.Is64BitProcess ? "x64" : "x86")}, OS: {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}");
            VoiceRuntimeLog.Info($"BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");

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

            // 初始化草稿管理器
            try
            {
                _draftManager = new TaskDraftManager();
                Console.WriteLine($"[App] TaskDraftManager initialized. Current drafts: {_draftManager.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Failed to initialize TaskDraftManager: {ex.Message}");
            }

            // 启动通知管理器
            try
            {
                _notificationManager = new NotificationManager(_draftManager);
                Console.WriteLine("[App] NotificationManager initialized.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Failed to initialize NotificationManager: {ex.Message}");
            }

            // 启动增强型智能语音监听服务
            try
            {
                _enhancedAudioService = new EnhancedAudioCaptureService();
                _enhancedAudioService.Start();

                var stats = _enhancedAudioService.GetStats();
                Console.WriteLine($"[App] EnhancedAudioCaptureService started. Stats: {stats.totalDetections} detections, {stats.potentialTasks} potential tasks");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] EnhancedAudioCaptureService start failed: {ex.Message}");
                VoiceRuntimeLog.Error("EnhancedAudioCaptureService start failed.", ex);

                // 回退到原有服务
                Console.WriteLine("[App] Falling back to legacy AudioCaptureService...");
                try
                {
                    _legacyAudioService = new AudioCaptureService();
                    _legacyAudioService.Start();
                    VoiceRuntimeLog.Info("Legacy AudioCaptureService started as fallback.");
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[App] Legacy AudioCaptureService also failed: {ex2.Message}");
                    VoiceRuntimeLog.Error("Legacy AudioCaptureService start failed.", ex2);
                    MessageBox.Show(
                        "语音识别初始化失败，系统未检测到可用语音识别引擎或麦克风权限异常。\n" +
                        $"请查看日志：{VoiceRuntimeLog.LogFilePath}",
                        "语音功能不可用",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _notificationManager?.Dispose();
                _notificationManager = null;

                _enhancedAudioService?.Dispose();
                _enhancedAudioService = null;

                _legacyAudioService?.Dispose();
                _legacyAudioService = null;

                _draftManager?.Dispose();
                _draftManager = null;
            }
            catch { }
            base.OnExit(e);
        }
    }
}
