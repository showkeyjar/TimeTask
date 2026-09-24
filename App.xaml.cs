using System;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace TimeTask
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 当前 App 单例（供 MainWindow 等访问全局服务，如交流/会议采集服务）。
        /// </summary>
        public static App Instance { get; private set; }

        /// <summary>
        /// 交流/会议采集服务（录制/停止/状态查询）。初始化失败时可能为 null。
        /// </summary>
        public ConversationCaptureService CaptureService => _captureService;

        // 增强型语音监听服务（常驻快捷口述，受 VoiceAutoStartOnLaunch 控制）
        private EnhancedAudioCaptureService _enhancedAudioService;

        // 原有的录音服务（保留备用）
        private AudioCaptureService _legacyAudioService;

        // 任务草稿管理器
        private TaskDraftManager _draftManager;

        // 通知管理器
        private NotificationManager _notificationManager;

        // 自动更新服务
        private AutoUpdateService _autoUpdateService;

        // 单实例互斥量（Local 作用域：同一用户会话内互斥）
        private System.Threading.Mutex _singleInstanceMutex;

        // 新增：交流/会议采集服务 + 系统托盘（单一图标，由 NotificationManager 托管）
        private ConversationCaptureService _captureService;
        private GlobalHotkeyService _hotkey;
        private string _hotkeyHint = "Ctrl+Alt+R";
        private ConversationCaptureService.ConversationCaptureResult _lastCaptureResult;

        protected override void OnStartup(StartupEventArgs e)
        {
            Instance = this;

            // 单实例：双开会导致托盘图标重复、CSV/JSON 互相覆盖写（后写者赢，丢对方改动）。
            bool createdNew;
            _singleInstanceMutex = new System.Threading.Mutex(true, @"Local\TimeTask_SingleInstance", out createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show(
                    "TimeTask 已在运行（请查看系统托盘）。同时运行两个实例会导致数据互相覆盖。",
                    "TimeTask",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // 全局异常兜底：后台线程/任务异常不再无声闪退，统一记录到 VoiceRuntimeLog。
            DispatcherUnhandledException += (s, args) =>
            {
                VoiceRuntimeLog.Error("未处理 UI 异常。", args.Exception);
                MessageBox.Show(
                    $"发生了一个未处理的错误，已写入日志：\n{VoiceRuntimeLog.LogFilePath}\n\n{args.Exception.Message}",
                    "TimeTask 错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };
            System.AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                VoiceRuntimeLog.Error("未处理非 UI 异常（进程可能退出）。", args.ExceptionObject as Exception);
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                VoiceRuntimeLog.Error("未观察任务异常。", args.Exception);
                args.SetObserved();
            };

            I18n.InitializeFromSettings();
            base.OnStartup(e); // Call base implementation
            VoiceRuntimeLog.Info("App startup.");
            VoiceRuntimeLog.Info($"ProcessBitness: {(Environment.Is64BitProcess ? "x64" : "x86")}, OS: {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}");
            VoiceRuntimeLog.Info($"BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");
            VoiceRuntimeLog.Info($"Config: VoiceAsrProvider={ConfigurationManager.AppSettings["VoiceAsrProvider"]}, FunAsrAutoBootstrap={ConfigurationManager.AppSettings["FunAsrAutoBootstrap"]}");
            // 控制台输出并入日志：全仓 315 处 Console.WriteLine 在 WPF（无控制台）下原本全部丢失，
            // 排查语音/LLM 问题时这些诊断最关键；带控制台启动调试时原样回显，不影响。
            VoiceRuntimeLog.TeeConsoleToLog();
            VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, I18n.T("Voice_StatusUnavailable"));
            FunAsrRuntimeManager.KickoffIfNeeded();

            // 启动自动更新检查（后台执行）
            try
            {
                _autoUpdateService = new AutoUpdateService(Dispatcher);
                _autoUpdateService.StartBackgroundCheck();
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("AutoUpdateService startup failed.", ex);
            }

            // Check for API Key configuration（加密存储优先，配置里可能只剩迁移标记）
            const string PlaceholderApiKey = "YOUR_API_KEY_GOES_HERE";
            string apiKey = SecureApiKeyStore.Load();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = System.Configuration.ConfigurationManager.AppSettings["OpenAIApiKey"];
            }

            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == PlaceholderApiKey || apiKey == "(migrated-to-secure-store)")
            {
                // 只在首次提醒一次：用户明确关闭后不再每次启动都弹窗打扰。
                // （若之后配置了 Key，会清掉“已提醒”标记——将来 Key 再丢失还能再提醒一次）
                if (!IsApiKeyWarningDismissed())
                {
                    System.Windows.MessageBox.Show(
                        I18n.T("App_ApiKeyWarningText"),
                        I18n.T("App_ApiKeyWarningTitle"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning
                    );
                    MarkApiKeyWarningDismissed();
                }
            }
            else
            {
                ClearApiKeyWarningDismissed();
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

            // 初始化交流/会议采集服务（常驻待命，不自动录音）
            try
            {
                _captureService = new ConversationCaptureService();
                _captureService.ConversationCaptured += OnConversationCaptured;

                var hk = ParseHotkeyConfig();
                _hotkeyHint = hk.Hint;

                // 单一托盘图标：草稿提醒 + 录音状态 + 收件箱入口，全部并入 NotificationManager
                _notificationManager = new NotificationManager(_draftManager, _captureService, OpenActionInbox, _hotkeyHint);
                Console.WriteLine("[App] NotificationManager initialized (single tray icon).");

                // 全局快捷键：一键开始/停止记录（默认 Ctrl+Alt+R，可在 App.config 改；不依赖点击托盘）
                _hotkey = new GlobalHotkeyService(ToggleCapture);
                bool hotkeyOk = _hotkey.Register(hk.Key, hk.Mods);
                VoiceRuntimeLog.Info($"全局快捷键注册{(hotkeyOk ? "成功" : "失败")}：{_hotkeyHint} 切换记录。");

                VoiceRuntimeLog.Info("Conversation capture + tray initialized (standby).");

                // 录音保留策略：启动时后台清一次过期录音（录音占磁盘，无限堆积是隐患）
                System.Threading.Tasks.Task.Run(() =>
                    RecordingRetention.Apply(AppPaths.RecordingsDir, RecordingRetention.GetRetentionDays()));
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Tray / 采集服务初始化失败。", ex);
            }

            // 是否开机即自动开始“常驻监听”（默认关闭，遵循“常驻待命而非常驻录音”的边界）
            bool autoStart = ReadBoolSetting("VoiceAutoStartOnLaunch", false);
            if (autoStart)
            {
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
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, I18n.T("Voice_StatusReady"));
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"[App] Legacy AudioCaptureService also failed: {ex2.Message}");
                        VoiceRuntimeLog.Error("Legacy AudioCaptureService start failed.", ex2);
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, I18n.T("Voice_StatusUnavailable"));
                        MessageBox.Show(
                            I18n.Tf("App_VoiceInitFailedTextFormat", VoiceRuntimeLog.LogFilePath),
                            I18n.T("App_VoiceInitFailedTitle"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            }
            else
            {
                VoiceListenerStatusCenter.Publish(VoiceListenerState.Ready, $"已待命：按 {_hotkeyHint} 开始记录");
                Console.WriteLine("[App] Voice auto-start disabled (standby mode). Use global hotkey to start capture.");
            }
        }

        private void ToggleCapture()
        {
            if (_captureService == null) return;
            try
            {
                if (_captureService.IsRecording)
                {
                    _captureService.Stop();
                    _notificationManager?.ShowBalloon("记录已停止", "正在生成行动收件箱…");
                }
                else
                {
                    var mode = _captureService.DefaultMode;
                    _captureService.Start(mode);
                    string label = mode == ConversationCaptureService.CaptureMode.Meeting ? "会议"
                                 : mode == ConversationCaptureService.CaptureMode.Quick ? "快捷口述" : "交流";
                    _notificationManager?.ShowBalloon("开始记录", $"{label}模式 · 按 {_hotkeyHint} 停止");
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("切换采集状态失败。", ex);
            }
        }

        private void OnConversationCaptured(ConversationCaptureService.ConversationCaptureResult result)
        {
            _lastCaptureResult = result;

            // 每场录音结束后顺带清一轮过期录音（应用常驻多日也保持磁盘可控；
            // 刚结束的会话不可能满足过期条件，无并发风险）
            System.Threading.Tasks.Task.Run(() =>
                RecordingRetention.Apply(AppPaths.RecordingsDir, RecordingRetention.GetRetentionDays()));

            // 用 BeginInvoke 异步切回 UI，避免 Dispatcher.Invoke 同步调用可能引发的死锁。
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    new ActionInboxWindow(result).Show();
                }
                catch (Exception ex)
                {
                    VoiceRuntimeLog.Error("打开行动收件箱失败。", ex);
                }
            }));
        }

        private void OpenMainWindow()
        {
            Dispatcher.Invoke(() =>
            {
                var mw = Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (mw != null)
                {
                    if (!mw.IsVisible) mw.Show();
                    mw.WindowState = WindowState.Normal;
                    mw.Activate();
                }
            });
        }

        private void OpenActionInbox()
        {
            Dispatcher.Invoke(() =>
            {
                if (_lastCaptureResult != null)
                {
                    try { new ActionInboxWindow(_lastCaptureResult).Show(); return; }
                    catch (Exception ex) { VoiceRuntimeLog.Error("打开行动收件箱失败。", ex); }
                }
                System.Windows.MessageBox.Show(
                    $"还没有已结束的交流记录。按 {_hotkeyHint} 开始记录，结束时将自动弹出行动收件箱。",
                    "行动收件箱",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            });
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

                _captureService?.Dispose();
                _captureService = null;

                _hotkey?.Dispose();
                _hotkey = null;

                _draftManager?.Dispose();
                _draftManager = null;

                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
            }
            catch { }
            base.OnExit(e);
        }

        private static bool ReadBoolSetting(string key, bool fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return bool.TryParse(value, out bool parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// “API Key 未配置”提醒是否已被用户知悉（弹出过一次即标记，不再重复打扰）。
        /// 标记写在 exe 同侧的 App.config（ApiKeyWarningDismissed），与其它配置同一存放处。
        /// </summary>
        private static bool IsApiKeyWarningDismissed()
        {
            try
            {
                return string.Equals(
                    ConfigurationManager.AppSettings["ApiKeyWarningDismissed"],
                    "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void MarkApiKeyWarningDismissed()
        {
            WriteAppSetting("ApiKeyWarningDismissed", "true");
        }

        private static void ClearApiKeyWarningDismissed()
        {
            // 已配置了 Key：清掉标记，将来 Key 再丢失时还能重新提醒一次
            if (IsApiKeyWarningDismissed())
            {
                WriteAppSetting("ApiKeyWarningDismissed", null);
            }
        }

        /// <summary>
        /// 向 exe 同侧 App.config 写入一个 appSettings 键；value 为 null 时删除该键。
        /// 写入失败只记日志，不影响启动流程。
        /// </summary>
        private static void WriteAppSetting(string key, string value)
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                if (value == null)
                {
                    config.AppSettings.Settings.Remove(key);
                }
                else if (config.AppSettings.Settings[key] == null)
                {
                    config.AppSettings.Settings.Add(key, value);
                }
                else
                {
                    config.AppSettings.Settings[key].Value = value;
                }
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Warn($"写入配置项 {key} 失败。", ex);
            }
        }

        /// <summary>
        /// 从 App.config 解析全局快捷键（ConversationCaptureHotkeyModifiers / ConversationCaptureHotkeyKey）。
        /// 默认 Ctrl+Alt+R。修饰键支持 Ctrl/Alt/Shift/Win（逗号分隔），Key 为 System.Windows.Forms.Keys 枚举名。
        /// </summary>
        private static (System.Windows.Forms.Keys Key, uint Mods, string Hint) ParseHotkeyConfig()
        {
            uint mods = 0;
            var label = new StringBuilder();

            string modStr = ConfigurationManager.AppSettings["ConversationCaptureHotkeyModifiers"] ?? "Ctrl,Alt";
            foreach (var part in modStr.Split(','))
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        mods |= GlobalHotkeyService.MOD_CONTROL; label.Append("Ctrl+"); break;
                    case "alt":
                        mods |= GlobalHotkeyService.MOD_ALT; label.Append("Alt+"); break;
                    case "shift":
                        mods |= GlobalHotkeyService.MOD_SHIFT; label.Append("Shift+"); break;
                    case "win":
                    case "windows":
                        mods |= GlobalHotkeyService.MOD_WIN; label.Append("Win+"); break;
                }
            }

            string keyStr = (ConfigurationManager.AppSettings["ConversationCaptureHotkeyKey"] ?? "R").Trim();
            if (!Enum.TryParse<System.Windows.Forms.Keys>(keyStr, true, out var key))
            {
                key = System.Windows.Forms.Keys.R;
                keyStr = "R";
            }
            label.Append(keyStr.Length == 1 ? keyStr.ToUpperInvariant() : keyStr);

            // 至少带一个修饰键，避免裸键（如单按 R）误触发
            if (mods == 0) { mods = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT; label = new StringBuilder("Ctrl+Alt+R"); }

            return (key, mods | GlobalHotkeyService.MOD_NOREPEAT, label.ToString());
        }
    }
}
