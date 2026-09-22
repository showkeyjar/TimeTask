using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace TimeTask
{
    /// <summary>一轮到期扫描的结论：首个到期任务所在的象限（1..4）与本轮到期总数。</summary>
    public sealed class ReminderBatch
    {
        public ReminderBatch(ItemGrid first, int firstQuadrant, int dueCount)
        {
            First = first;
            FirstQuadrant = firstQuadrant;
            DueCount = dueCount;
        }

        /// <summary>本轮应最先提醒的任务（按象限 1..4、象限内列表顺序）。</summary>
        public ItemGrid First { get; private set; }

        /// <summary>首个到期任务所在象限，1..4；无到期任务时为 0。</summary>
        public int FirstQuadrant { get; private set; }

        /// <summary>本轮到期任务总数（用于「还有 N-1 个」的气泡文案）。</summary>
        public int DueCount { get; private set; }
    }

    /// <summary>
    /// 到期提醒的纯评估逻辑：给定四个象限的当前任务列表和基准时间，算出
    /// 「哪些任务到期了、先提醒谁」。从 MainWindow 的 ReminderTimer_Tick 中
    /// 剥离出来做成纯函数，才可能被单元测试覆盖（此前这段选择逻辑埋在
    /// 6600 行的窗口类里，只能靠手点验证）。
    /// </summary>
    public static class ReminderEvaluator
    {
        /// <summary>
        /// 扫描四个象限，返回到期批次；无到期任务时返回 null。
        /// 判定口径与旧 ReminderTimer_Tick 完全一致：IsActive 且 ReminderTime ≤ now。
        /// quadrants 允许出现 null 元素（象限尚未加载数据时 ItemsSource 为 null）。
        /// </summary>
        public static ReminderBatch Evaluate(IReadOnlyList<List<ItemGrid>> quadrants, DateTime now)
        {
            if (quadrants == null)
            {
                return null;
            }

            ItemGrid firstDue = null;
            int firstQuadrant = 0;
            int dueCount = 0;

            for (int i = 0; i < quadrants.Count; i++)
            {
                var tasks = quadrants[i];
                if (tasks == null)
                {
                    continue;
                }
                foreach (var task in tasks)
                {
                    if (task != null && task.IsActive && task.ReminderTime.HasValue && task.ReminderTime.Value <= now)
                    {
                        dueCount++;
                        if (firstDue == null)
                        {
                            firstDue = task;
                            firstQuadrant = i + 1; // 象限编号 1..4 与 data\N.csv 对齐
                        }
                    }
                }
            }

            if (firstDue == null)
            {
                return null;
            }
            return new ReminderBatch(firstDue, firstQuadrant, dueCount);
        }
    }

    /// <summary>
    /// 提醒域定时器的宿主（MainWindow 拆分第二步，见 docs/DESIGN_REVIEW.md 路线图）。
    /// 拥有到期检查的 DispatcherTimer 与「模态提醒弹窗互斥」状态；窗口只提供
    /// 当前象限数据、订阅到期事件并负责真正的弹窗/气泡展示。
    ///
    /// 互斥语义（自提醒弹窗风暴治理沿用）：TaskReminderWindow 是模态的且
    /// ShowDialog 会泵消息——弹窗打开期间定时器照样触发，N 个到期提醒会堆叠
    /// N 个模态窗互相卡死。置位期间新的到期提醒由窗口降级为被动气泡。
    ///
    /// 必须在 UI 线程构造（DispatcherTimer 绑定当前 Dispatcher）。
    /// </summary>
    public sealed class ReminderService : IDisposable
    {
        private readonly Func<List<ItemGrid>[]> _quadrantsProvider;
        private readonly DispatcherTimer _timer;
        private bool _dialogActive;
        private bool _disposed;

        /// <summary>到期批次产生时触发（无到期任务的轮次不触发）；在 UI 线程回调。</summary>
        public event EventHandler<ReminderBatch> DueRemindersRaised;

        /// <summary>dialog 互斥是否处于占用中（弹窗展示期间为 true）。</summary>
        public bool IsDialogActive
        {
            get { return _dialogActive; }
        }

        /// <param name="interval">检查间隔（旧值来自 Settings.ReminderCheckIntervalSeconds）。</param>
        /// <param name="quadrantsProvider">返回四个象限当前任务列表（元素可为 null）。</param>
        public ReminderService(TimeSpan interval, Func<List<ItemGrid>[]> quadrantsProvider)
        {
            if (quadrantsProvider == null)
            {
                throw new ArgumentNullException(nameof(quadrantsProvider));
            }
            _quadrantsProvider = quadrantsProvider;
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (s, e) => EvaluateAndRaise();
        }

        public void Start()
        {
            if (_disposed)
            {
                return;
            }
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        /// <summary>
        /// 执行一轮到期扫描并触发事件。定时器每 tick 调用；也允许直接调用以便测试/立即检查。
        /// 扫描异常只记日志不抛出——单轮失败不应杀死后续轮次（防弹窗再次静默失效）。
        /// </summary>
        public void EvaluateAndRaise()
        {
            try
            {
                var batch = ReminderEvaluator.Evaluate(_quadrantsProvider(), DateTime.Now);
                if (batch != null)
                {
                    DueRemindersRaised?.Invoke(this, batch);
                }
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("ReminderService 到期扫描失败。", ex);
            }
        }

        /// <summary>请求占用模态弹窗通道；弹窗展示期间返回 false，调用方应降级为气泡。</summary>
        public bool TryBeginDialog()
        {
            if (_dialogActive)
            {
                return false;
            }
            _dialogActive = true;
            return true;
        }

        /// <summary>释放模态弹窗通道（必须在弹窗 finally 中调用，否则提醒会被永久压制）。</summary>
        public void EndDialog()
        {
            _dialogActive = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _timer.Stop();
        }
    }
}
