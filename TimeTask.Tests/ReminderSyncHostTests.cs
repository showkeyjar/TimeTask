using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>
    /// 提醒域契约：到期评估口径（IsActive 且 ReminderTime ≤ now、象限顺序即优先级）
    /// 与模态弹窗互斥（占用失败必须可降级、finally 必须可释放）。
    /// 这些规则此前埋在 6600 行 MainWindow 的 ReminderTimer_Tick 里，无法测试。
    /// </summary>
    [TestClass]
    public class ReminderServiceTests
    {
        private static ItemGrid NewTask(string title, bool active = true, DateTime? reminder = null)
        {
            return new ItemGrid
            {
                Task = title,
                IsActive = active,
                IsActiveInQuadrant = true,
                CreatedDate = DateTime.Now,
                ReminderTime = reminder
            };
        }

        private static List<ItemGrid>[] Quadrants(params List<ItemGrid>[] lists)
        {
            return lists;
        }

        [TestMethod]
        public void Evaluate_NoDueTasks_ReturnsNull()
        {
            var now = new DateTime(2026, 9, 22, 12, 0, 0);
            var quadrants = Quadrants(
                new List<ItemGrid> { NewTask("未来提醒", reminder: now.AddHours(1)) },
                new List<ItemGrid> { NewTask("无提醒") },
                new List<ItemGrid> { NewTask("已完成任务", active: false, reminder: now.AddHours(-1)) },
                new List<ItemGrid>());

            Assert.IsNull(ReminderEvaluator.Evaluate(quadrants, now), "未到期/已完成/无提醒都不算到期");
        }

        [TestMethod]
        public void Evaluate_PastAndExactNow_AreDue()
        {
            var now = new DateTime(2026, 9, 22, 12, 0, 0);
            var past = NewTask("过期任务", reminder: now.AddMinutes(-1));
            var exact = NewTask("恰好到期", reminder: now);
            var quadrants = Quadrants(
                new List<ItemGrid> { past, exact },
                null, // 象限未加载（ItemsSource 为 null）必须容忍
                null,
                null);

            var batch = ReminderEvaluator.Evaluate(quadrants, now);

            Assert.IsNotNull(batch);
            Assert.AreSame(past, batch.First, "象限内列表顺序决定先提醒谁");
            Assert.AreEqual(1, batch.FirstQuadrant);
            Assert.AreEqual(2, batch.DueCount);
        }

        [TestMethod]
        public void Evaluate_MultipleQuadrants_FirstIsLowestQuadrantInListOrder()
        {
            var now = new DateTime(2026, 9, 22, 12, 0, 0);
            var q2First = NewTask("象限2-首个", reminder: now.AddMinutes(-5));
            var q2Second = NewTask("象限2-次个", reminder: now.AddMinutes(-10));
            var q4 = NewTask("象限4", reminder: now.AddMinutes(-30));

            var batch = ReminderEvaluator.Evaluate(
                Quadrants(
                    new List<ItemGrid>(),
                    new List<ItemGrid> { q2Second, q2First },
                    null,
                    new List<ItemGrid> { q4 }),
                now);

            Assert.IsNotNull(batch);
            Assert.AreSame(q2Second, batch.First, "象限 2 整体优先于象限 4，与提醒时间早晚无关（与旧行为一致）");
            Assert.AreEqual(2, batch.FirstQuadrant);
            Assert.AreEqual(3, batch.DueCount);
        }

        [TestMethod]
        public void Evaluate_NullQuadrantsArray_ReturnsNull()
        {
            Assert.IsNull(ReminderEvaluator.Evaluate(null, DateTime.Now));
        }

        [TestMethod]
        public void Evaluate_NullTaskEntries_AreSkipped()
        {
            var now = DateTime.Now;
            var due = NewTask("唯一有效", reminder: now.AddMinutes(-1));
            var list = new List<ItemGrid> { null, due, null };

            var batch = ReminderEvaluator.Evaluate(Quadrants(list, null, null, null), now);

            Assert.IsNotNull(batch);
            Assert.AreSame(due, batch.First);
            Assert.AreEqual(1, batch.DueCount);
        }

        [TestMethod]
        public void DialogMutex_BeginBlocksSecondBegin_AndEndReleases()
        {
            var service = new ReminderService(TimeSpan.FromSeconds(60), () => new List<ItemGrid>[4]);

            Assert.IsFalse(service.IsDialogActive, "初始未占用");
            Assert.IsTrue(service.TryBeginDialog(), "首次占用必须成功");
            Assert.IsTrue(service.IsDialogActive);
            Assert.IsFalse(service.TryBeginDialog(), "占用期间第二个模态窗必须被拒绝（降级为气泡）");

            service.EndDialog();
            Assert.IsFalse(service.IsDialogActive, "finally 释放后必须回到未占用");
            Assert.IsTrue(service.TryBeginDialog(), "释放后可再次占用");

            service.Dispose();
        }

        [TestMethod]
        public void EvaluateAndRaise_NoDue_DoesNotRaise_AndNeverThrows()
        {
            var raised = 0;
            ReminderBatch received = null;
            // provider 故意抛错：单轮扫描失败不允许炸出 UI（只记日志）
            Func<List<ItemGrid>[]> throwing = () => { throw new InvalidOperationException("boom"); };
            var service = new ReminderService(TimeSpan.FromSeconds(60), throwing);
            service.DueRemindersRaised += (s, b) => { raised++; received = b; };

            service.EvaluateAndRaise();
            service.Dispose();

            Assert.AreEqual(0, raised);
            Assert.IsNull(received);
        }

        [TestMethod]
        public void EvaluateAndRaise_WithDue_RaisesBatchOnUiSemantics()
        {
            var now = DateTime.Now;
            var due = NewTask("到期任务", reminder: now.AddMinutes(-2));
            var quadrants = new List<ItemGrid>[]
            {
                new List<ItemGrid> { due },
                null, null, null
            };

            ReminderBatch received = null;
            var service = new ReminderService(TimeSpan.FromSeconds(60), () => quadrants);
            service.DueRemindersRaised += (s, b) => received = b;

            service.EvaluateAndRaise();
            service.Dispose();

            Assert.IsNotNull(received);
            Assert.AreSame(due, received.First);
            Assert.AreEqual(1, received.FirstQuadrant);
            Assert.AreEqual(1, received.DueCount);
        }

        [TestMethod]
        public void Ctor_NullProvider_Throws()
        {
            try
            {
                new ReminderService(TimeSpan.FromSeconds(1), null);
                Assert.Fail("必须拒绝 null 数据提供者");
            }
            catch (ArgumentNullException)
            {
            }
        }
    }

    /// <summary>
    /// 同步域宿主契约：区间规范化兜底（非正值回退默认）与未配置路径的安全空操作。
    /// （定时器触发本身依赖 WPF 消息泵，留给应用实测；这里锁定的是配置语义。）
    /// </summary>
    [TestClass]
    public class SyncSchedulerTests
    {
        [TestMethod]
        public void NormalizeIntervalMinutes_NonPositiveFallsBack()
        {
            Assert.AreEqual(30, SyncScheduler.NormalizeIntervalMinutes(0));
            Assert.AreEqual(30, SyncScheduler.NormalizeIntervalMinutes(-5));
            Assert.AreEqual(45, SyncScheduler.NormalizeIntervalMinutes(45));
            Assert.AreEqual(15, SyncScheduler.NormalizeIntervalMinutes(0, 15), "显式兜底值必须生效");
        }

        [TestMethod]
        public void NormalizeDebounceSeconds_NonPositiveFallsBack()
        {
            Assert.AreEqual(5, SyncScheduler.NormalizeDebounceSeconds(0));
            Assert.AreEqual(5, SyncScheduler.NormalizeDebounceSeconds(-1));
            Assert.AreEqual(12, SyncScheduler.NormalizeDebounceSeconds(12));
        }

        [TestMethod]
        public void TriggerKnowledgeDebounce_WithoutConfigure_IsSafeNoOp()
        {
            using (var scheduler = new SyncScheduler())
            {
                scheduler.TriggerKnowledgeDebounce(); // 不得抛 NRE
            }
        }

        [TestMethod]
        public void ConfigureTeamSync_Disabled_IsSafeWithoutTimer()
        {
            using (var scheduler = new SyncScheduler())
            {
                scheduler.ConfigureTeamSync(false, 30, () => { });
                scheduler.StopTeamSync(); // 幂等
            }
        }

        [TestMethod]
        public void Configure_AfterDispose_Throws()
        {
            var scheduler = new SyncScheduler();
            scheduler.Dispose();
            try
            {
                scheduler.ConfigureTeamSync(true, 30, () => { });
                Assert.Fail("释放后再配置必须抛 ObjectDisposedException");
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
