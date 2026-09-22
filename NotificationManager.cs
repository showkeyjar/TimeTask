using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;

using notifyIcon = System.Windows.Forms.NotifyIcon;

namespace TimeTask
{
    /// <summary>
    /// 通知管理器（唯一托盘图标）
    /// - 静默通知：托盘图标闪烁
    /// - Toast 通知：Windows 系统通知
    /// - 交流/会议录音状态实时展示（与草稿提醒共用同一个图标）
    /// - 恰到好处的触发策略
    /// </summary>
    public class NotificationManager : IDisposable
    {
        private readonly TaskDraftManager _draftManager;
        private readonly ConversationCaptureService _capture;
        private readonly Action _openInbox;
        private readonly string _hotkeyHint;
        private notifyIcon _notifyIcon;
        private DispatcherTimer _checkTimer;
        private int _blinkCount = 0;
        private bool _isBlinking = false;
        private const int MaxBlinkCount = 6; // 闪烁6次后停止

        // 通知阈值
        private const int DraftNotificationThreshold = 1; // 有草稿即提示
        private const int WorkReminderIntervalMinutes = 240; // 每4小时提醒一次
        private static readonly TimeSpan DraftNotificationCooldown = TimeSpan.FromMinutes(30);

        // 状态
        private DateTime _lastWorkReminder = DateTime.MinValue;
        private int _consecutiveDraftCounts = 0;
        private DateTime _lastDraftNotificationTime = DateTime.MinValue;
        private int _lastNotifiedDraftCount = 0;

        public NotificationManager(TaskDraftManager draftManager, ConversationCaptureService capture = null, Action openInbox = null, string hotkeyHint = "Ctrl+Alt+R")
        {
            _draftManager = draftManager ?? throw new ArgumentNullException(nameof(draftManager));
            _capture = capture;
            _openInbox = openInbox;
            _hotkeyHint = hotkeyHint;

            InitializeNotifyIcon();
            StartMonitoring();
            UpdateTooltip(_draftManager?.UnprocessedCount ?? 0); // 立即反映初始 ASR 状态（如“模型加载中…”）

            if (_capture != null)
            {
                _capture.StatusChanged += OnCaptureStatusChanged;
            }
        }

        private void InitializeNotifyIcon()
        {
            try
            {
                _notifyIcon = new notifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                        System.Reflection.Assembly.GetExecutingAssembly().Location
                    ),
                    Text = "TimeTask - 任务管理助手",
                    Visible = true
                };

                _notifyIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
                _notifyIcon.ContextMenuStrip.Items.Add("显示主窗口", null, (s, e) => ShowMainWindow());
                if (_capture != null)
                {
                    _notifyIcon.ContextMenuStrip.Items.Add($"开始/停止记录 ({_hotkeyHint})", null, (s, e) => _capture.Toggle());
                    _notifyIcon.ContextMenuStrip.Items.Add("查看行动收件箱", null, (s, e) => _openInbox?.Invoke());
                }
                _notifyIcon.ContextMenuStrip.Items.Add("-");
                _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (s, e) => ExitApplication());

                // 双击打开主窗口
                _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

                Console.WriteLine("[NotificationManager] NotifyIcon initialized (single tray icon).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationManager] Failed to initialize NotifyIcon: {ex.Message}");
            }
        }

        private void StartMonitoring()
        {
            _checkTimer = new DispatcherTimer();
            _checkTimer.Interval = TimeSpan.FromMinutes(1); // 每分钟检查一次
            _checkTimer.Tick += OnCheckTick;
            _checkTimer.Start();
            Console.WriteLine("[NotificationManager] Monitoring started.");
        }

        private void OnCheckTick(object sender, EventArgs e)
        {
            if (_draftManager == null) return;

            int unprocessedCount = _draftManager.UnprocessedCount;

            // 1. 检查草稿累积
            if (unprocessedCount >= DraftNotificationThreshold)
            {
                // 连续2次检查都超过阈值才通知，避免瞬间波动
                _consecutiveDraftCounts++;

                if (_consecutiveDraftCounts >= 2)
                {
                    TryShowDraftNotification(unprocessedCount);
                    _consecutiveDraftCounts = 0; // 重置
                }
            }
            else
            {
                _consecutiveDraftCounts = 0;
            }

            // 2. 工作休息提醒（基于时间）
            CheckWorkReminder();

            // 3. 更新托盘图标提示
            UpdateTooltip(unprocessedCount);
        }

        private void CheckWorkReminder()
        {
            var now = DateTime.Now;

            // 只在工作时间提醒 (9:00 - 18:00)
            if (now.Hour < 9 || now.Hour >= 18)
                return;

            // 检查是否过了提醒间隔
            if (now - _lastWorkReminder > TimeSpan.FromMinutes(WorkReminderIntervalMinutes))
            {
                // 检查用户是否处于"工作状态"（通过检查草稿活跃度判断）
                var recentDrafts = _draftManager.GetUnprocessedDrafts()
                    .Where(d => (now - d.LastDetected) < TimeSpan.FromHours(2))
                    .ToList();

                if (recentDrafts.Count > 0)
                {
                    ShowWorkReminder();
                    _lastWorkReminder = now;
                }
            }
        }

        private void TryShowDraftNotification(int count)
        {
            try
            {
                var now = DateTime.Now;
                if (now - _lastDraftNotificationTime < DraftNotificationCooldown && count <= _lastNotifiedDraftCount)
                {
                    return;
                }

                // 使用 Toast 通知（可忽略，不打断工作）
                _notifyIcon.ShowBalloonTip(
                    5000, // 显示5秒
                    "TimeTask - 任务提醒",
                    $"检测到 {count} 个潜在任务。点击查看并添加到四象限。",
                    ToolTipIcon.Info
                );

                // 托盘图标闪烁
                StartBlinking();

                _lastDraftNotificationTime = now;
                _lastNotifiedDraftCount = count;

                Console.WriteLine($"[NotificationManager] Draft notification shown: {count} drafts.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationManager] Failed to show notification: {ex.Message}");
            }
        }

        private void ShowWorkReminder()
        {
            try
            {
                _notifyIcon.ShowBalloonTip(
                    5000,
                    "TimeTask - 休息提醒",
                    "你已经持续工作一段时间了。建议休息一下，或者查看任务草稿。",
                    ToolTipIcon.Info
                );

                Console.WriteLine("[NotificationManager] Work reminder shown.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationManager] Failed to show work reminder: {ex.Message}");
            }
        }

        private void StartBlinking()
        {
            if (_isBlinking || _notifyIcon == null) return;

            _isBlinking = true;
            _blinkCount = 0;

            var blinkTimer = new DispatcherTimer();
            blinkTimer.Interval = TimeSpan.FromMilliseconds(500);
            blinkTimer.Tick += (s, e) =>
            {
                _blinkCount++;
                _notifyIcon.Visible = (_blinkCount % 2 == 0);

                if (_blinkCount >= MaxBlinkCount)
                {
                    blinkTimer.Stop();
                    _isBlinking = false;
                    _notifyIcon.Visible = true; // 确保图标可见
                }
            };
            blinkTimer.Start();
        }

        private void OnCaptureStatusChanged()
        {
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                try { app.Dispatcher.BeginInvoke(new Action(RefreshTooltip)); return; }
                catch { }
            }
            RefreshTooltip();
        }

        private void RefreshTooltip()
        {
            int draftCount = _draftManager?.UnprocessedCount ?? 0;
            UpdateTooltip(draftCount);
        }

        private void UpdateTooltip(int draftCount)
        {
            if (_notifyIcon == null) return;

            string text;
            if (_capture != null && _capture.IsRecording)
            {
                string mode = _capture.CurrentMode == ConversationCaptureService.CaptureMode.Meeting ? "会议"
                            : _capture.CurrentMode == ConversationCaptureService.CaptureMode.Quick ? "口述" : "交流";
                string mmss = _capture.Elapsed.ToString(@"mm\:ss");
                string mic = _capture.MicActive ? "麦克风✓" : "麦克风·";
                string sys = _capture.SystemActive ? "系统✓" : "系统·";
                text = $"● 录音中 {mmss} · {mode} · {mic} · {sys}";
            }
            else
            {
                if (draftCount > 0)
                    text = $"TimeTask - ({draftCount} 个草稿)";
                else if (_capture != null && !_capture.AsrAvailable)
                    text = $"TimeTask - {_capture.AsrStatusText}";
                else
                    text = "TimeTask - 运行中";
            }

            try
            {
                _notifyIcon.Text = text.Length > 127 ? text.Substring(0, 127) : text;
            }
            catch { }
        }

        private void ShowMainWindow()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current.Windows.OfType<Window>()
                    .FirstOrDefault(w => w.GetType().Name == "MainWindow");

                if (mainWindow != null)
                {
                    mainWindow.Show();
                    mainWindow.Activate();
                    mainWindow.WindowState = WindowState.Normal;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationManager] Failed to show main window: {ex.Message}");
            }
        }

        private void ExitApplication()
        {
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// 弹出托盘气泡，用于全局快捷键切录音状态时的轻量反馈。
        /// </summary>
        public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (_notifyIcon == null) return;
            try { _notifyIcon.ShowBalloonTip(3000, title, text, icon); }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.StatusChanged -= OnCaptureStatusChanged;
                }
            }
            catch { }

            try
            {
                _checkTimer?.Stop();
                _checkTimer = null;

                _notifyIcon?.Dispose();
                _notifyIcon = null;
            }
            catch { }
        }
    }
}
