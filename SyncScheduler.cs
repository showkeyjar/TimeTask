using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TimeTask
{
    /// <summary>
    /// 同步域定时器的宿主（MainWindow 拆分第二步，见 docs/DESIGN_REVIEW.md 路线图）。
    /// 此前团队同步（DatabaseService）与知识同步（Obsidian）的三个 DispatcherTimer
    /// （_syncTimer / _knowledgeSyncTimer / _knowledgeSyncDebounceTimer）散落在
    /// MainWindow 的初始化、重初始化、关闭清理三条路径上，各自为政：
    /// 关闭清理漏一个就泄漏一个窗口引用，重初始化路径要记得先 Stop 再建。
    /// 收口到这里之后：窗口只提供 tick 委托与配置，生命周期由本类统一负责。
    ///
    /// 必须在 UI 线程构造（DispatcherTimer 绑定当前 Dispatcher）。
    /// </summary>
    public sealed class SyncScheduler : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private DispatcherTimer _teamTimer;
        private DispatcherTimer _knowledgeTimer;
        private DispatcherTimer _knowledgeDebounceTimer;
        private Action _teamTick;
        private Func<Task> _knowledgeTick;
        private bool _disposed;

        public SyncScheduler()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        /// <summary>同步间隔（分钟）规范化：非正值回退默认值（与旧行 SaveIntervalMinutes 兜底一致）。</summary>
        public static int NormalizeIntervalMinutes(int value, int fallback = 30)
        {
            return value > 0 ? value : fallback;
        }

        /// <summary>防抖窗口（秒）规范化：非正值回退默认值。</summary>
        public static int NormalizeDebounceSeconds(int value, int fallback = 5)
        {
            return value > 0 ? value : fallback;
        }

        /// <summary>
        /// 配置/重配置团队同步定时器：enabled=false 只停不拆（与旧 InitializeSyncService
        /// 的禁用分支一致）；enabled=true 时更新间隔并启动，tick 委托以最近一次为准。
        /// </summary>
        public void ConfigureTeamSync(bool enabled, int intervalMinutes, Action tick)
        {
            ThrowIfDisposed();
            if (!enabled)
            {
                StopTeamSync();
                return;
            }

            _teamTick = tick;
            if (_teamTimer == null)
            {
                _teamTimer = new DispatcherTimer();
                _teamTimer.Tick += (s, e) => _teamTick?.Invoke();
            }
            _teamTimer.Interval = TimeSpan.FromMinutes(NormalizeIntervalMinutes(intervalMinutes));
            _teamTimer.Start();
        }

        /// <summary>停用团队同步（tick 内发现功能被关闭时自停也走这里，语义与旧代码一致）。</summary>
        public void StopTeamSync()
        {
            _teamTimer?.Stop();
        }

        /// <summary>
        /// 配置知识同步：周期定时器 + Obsidian 变更防抖定时器。
        /// debounceSeconds 是防抖窗口：仓库文件连续变更时只在最后一次变更后静默
        /// debounceSeconds 才真正同步一次（TriggerKnowledgeDebounce 负责重置计时）。
        /// </summary>
        public void ConfigureKnowledgeSync(int intervalMinutes, int debounceSeconds, Func<Task> tick)
        {
            ThrowIfDisposed();
            _knowledgeTick = tick;

            if (_knowledgeTimer == null)
            {
                _knowledgeTimer = new DispatcherTimer();
                _knowledgeTimer.Tick += async (s, e) =>
                {
                    if (_knowledgeTick != null)
                    {
                        await _knowledgeTick();
                    }
                };
            }
            _knowledgeTimer.Interval = TimeSpan.FromMinutes(NormalizeIntervalMinutes(intervalMinutes));
            _knowledgeTimer.Start();

            if (_knowledgeDebounceTimer == null)
            {
                _knowledgeDebounceTimer = new DispatcherTimer();
                _knowledgeDebounceTimer.Tick += async (s, e) =>
                {
                    // 防抖到期：先自停（单发语义），再执行一次同步
                    _knowledgeDebounceTimer.Stop();
                    if (_knowledgeTick != null)
                    {
                        await _knowledgeTick();
                    }
                };
            }
            _knowledgeDebounceTimer.Interval = TimeSpan.FromSeconds(NormalizeDebounceSeconds(debounceSeconds));
        }

        /// <summary>
        /// 触发一次知识同步防抖（重置计时）。可从任意线程调用（内部 marshal 回 UI 线程）；
        /// 未配置知识同步时是安全空操作。
        /// </summary>
        public void TriggerKnowledgeDebounce()
        {
            if (_disposed || _knowledgeDebounceTimer == null)
            {
                return;
            }
            Action restart = () =>
            {
                _knowledgeDebounceTimer.Stop();
                _knowledgeDebounceTimer.Start();
            };
            if (_dispatcher.CheckAccess())
            {
                restart();
            }
            else
            {
                _dispatcher.BeginInvoke(restart);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_teamTimer != null)
            {
                _teamTimer.Stop();
                _teamTimer = null;
            }
            if (_knowledgeTimer != null)
            {
                _knowledgeTimer.Stop();
                _knowledgeTimer = null;
            }
            if (_knowledgeDebounceTimer != null)
            {
                _knowledgeDebounceTimer.Stop();
                _knowledgeDebounceTimer = null;
            }
            _teamTick = null;
            _knowledgeTick = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SyncScheduler));
            }
        }
    }
}
