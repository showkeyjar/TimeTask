using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TimeTask
{
    /// <summary>
    /// 智能引导域定时器的宿主（MainWindow 拆分路线图收尾）。
    /// 此前 _taskReminderTimer 与 _smartSystemTimer 两个 DispatcherTimer 散落在
    /// 初始化与关闭清理两条路径上；且 TaskReminderTimer_Tick 是 async void 且无异常隔离，
    /// 一次 tick 异常会直冲 Dispatcher（崩溃面）。收口后：窗口只提供 tick 委托，
    /// 生命周期与异常隔离由本类统一负责——单次 tick 失败只记日志，定时器继续存活。
    ///
    /// 必须在 UI 线程构造（DispatcherTimer 绑定当前 Dispatcher）。
    /// </summary>
    public sealed class GuidanceScheduler : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private DispatcherTimer _smartSystemTimer;
        private DispatcherTimer _taskReminderTimer;
        private Action _smartSystemTick;
        private Func<Task> _taskReminderTick;
        private bool _disposed;

        public GuidanceScheduler()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        /// <summary>检查间隔（分钟）规范化：非正值回退默认 5 分钟（与旧硬编码一致）。</summary>
        public static int NormalizeIntervalMinutes(int value, int fallback = 5)
        {
            return value > 0 ? value : fallback;
        }

        /// <summary>
        /// tick 的异常隔离：单次失败不外抛（async void 场景异常直冲 Dispatcher 是崩溃面），
        /// 记日志后继续。公开为 internal 以便契约测试直接验证。
        /// </summary>
        internal static void SafeTick(string timerName, Action tick)
        {
            try
            {
                tick?.Invoke();
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error($"{timerName} 定时任务执行失败。", ex);
            }
        }

        /// <summary>SafeTick 的异步版：tick 内部的异常（含 await 之后）同样不得外抛。</summary>
        internal static async Task SafeTickAsync(string timerName, Func<Task> tick)
        {
            try
            {
                if (tick != null)
                {
                    await tick().ConfigureAwait(true); // 保持 DispatcherTimer 的 UI 线程上下文
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error($"{timerName} 定时任务执行失败。", ex);
            }
        }

        /// <summary>配置/重配置智能系统定时器（场景触发 + 目标调适 + 战略导航）。</summary>
        public void ConfigureSmartSystem(int intervalMinutes, Action tick)
        {
            ThrowIfDisposed();
            _smartSystemTick = tick;
            if (_smartSystemTimer == null)
            {
                _smartSystemTimer = new DispatcherTimer();
                _smartSystemTimer.Tick += (s, e) => SafeTick("SmartSystem", _smartSystemTick);
            }
            _smartSystemTimer.Interval = TimeSpan.FromMinutes(NormalizeIntervalMinutes(intervalMinutes));
            _smartSystemTimer.Start();
        }

        /// <summary>配置/重配置任务提醒定时器（陈旧任务提醒 + 卡住检测 + 自适应调参）。</summary>
        public void ConfigureTaskReminder(int intervalMinutes, Func<Task> tick)
        {
            ThrowIfDisposed();
            _taskReminderTick = tick;
            if (_taskReminderTimer == null)
            {
                _taskReminderTimer = new DispatcherTimer();
                _taskReminderTimer.Tick += (s, e) =>
                {
                    var _ = SafeTickAsync("TaskReminder", _taskReminderTick);
                };
            }
            _taskReminderTimer.Interval = TimeSpan.FromMinutes(NormalizeIntervalMinutes(intervalMinutes));
            _taskReminderTimer.Start();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_smartSystemTimer != null)
            {
                _smartSystemTimer.Stop();
                _smartSystemTimer = null;
            }
            if (_taskReminderTimer != null)
            {
                _taskReminderTimer.Stop();
                _taskReminderTimer = null;
            }
            _smartSystemTick = null;
            _taskReminderTick = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GuidanceScheduler));
            }
        }
    }
}
