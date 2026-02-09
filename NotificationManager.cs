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
    /// 通知管理器
    /// - 静默通知：托盘图标闪烁
    /// - Toast 通知：Windows 系统通知
    /// - 恰到好处的触发策略
    /// </summary>
    public class NotificationManager : IDisposable
    {
        private readonly TaskDraftManager _draftManager;
        private notifyIcon _notifyIcon;
        private DispatcherTimer _checkTimer;
        private int _blinkCount = 0;
        private bool _isBlinking = false;
        private const int MaxBlinkCount = 6; // 闪烁6次后停止

        // 通知阈值
        private const int DraftNotificationThreshold = 3; // 草稿累积到3个时通知
        private const int WorkReminderIntervalMinutes = 120; // 每2小时提醒休息

        // 状态
        private DateTime _lastWorkReminder = DateTime.MinValue;
        private int _consecutiveDraftCounts = 0;

        public NotificationManager(TaskDraftManager draftManager)
        {
            _draftManager = draftManager ?? throw new ArgumentNullException(nameof(draftManager));

            InitializeNotifyIcon();
            StartMonitoring();
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
                _notifyIcon.ContextMenuStrip.Items.Add("查看任务草稿", null, (s, e) => ShowDraftsWindow());
                _notifyIcon.ContextMenuStrip.Items.Add("-");
                _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (s, e) => ExitApplication());

                // 双击打开主窗口
                _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

                Console.WriteLine("[NotificationManager] NotifyIcon initialized.");
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
                // 连续3次检查都超过阈值才通知，避免瞬间波动
                _consecutiveDraftCounts++;

                if (_consecutiveDraftCounts >= 3)
                {
                    ShowDraftNotification(unprocessedCount);
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

        private void ShowDraftNotification(int count)
        {
            try
            {
                // 使用 Toast 通知（可忽略，不打断工作）
                _notifyIcon.ShowBalloonTip(
                    5000, // 显示5秒
                    "TimeTask - 任务提醒",
                    $"检测到 {count} 个潜在任务。点击查看并添加到四象限。",
                    ToolTipIcon.Info
                );

                // 托盘图标闪烁
                StartBlinking();

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

        private void UpdateTooltip(int draftCount)
        {
            if (_notifyIcon == null) return;

            string status = draftCount > 0 ? $"({draftCount} 个草稿)" : "运行中";
            _notifyIcon.Text = $"TimeTask - {status}";
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

        private void ShowDraftsWindow()
        {
            // TODO: 创建草稿查看窗口
            // 目前只显示提示
            int count = _draftManager?.UnprocessedCount ?? 0;
            System.Windows.MessageBox.Show(
                $"当前有 {count} 个未处理的任务草稿。\n\n请在主窗口中查看并添加任务。\n\n(草稿查看功能即将推出)",
                "任务草稿",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void ExitApplication()
        {
            System.Windows.Application.Current.Shutdown();
        }

        public void Dispose()
        {
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
