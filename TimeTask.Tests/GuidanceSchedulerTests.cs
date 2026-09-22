using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>
    /// GuidanceScheduler 契约测试（智能引导域定时器宿主）。
    /// 语义与 SyncSchedulerTests 对齐；重点补上旧实现缺失的一环：
    /// tick 异常隔离（旧 TaskReminderTimer_Tick 是 async void，异常直冲 Dispatcher 是崩溃面）。
    /// </summary>
    [TestClass]
    public class GuidanceSchedulerTests
    {
        [TestMethod]
        public void NormalizeIntervalMinutes_NonPositiveFallsBack()
        {
            Assert.AreEqual(5, GuidanceScheduler.NormalizeIntervalMinutes(0));
            Assert.AreEqual(5, GuidanceScheduler.NormalizeIntervalMinutes(-3));
            Assert.AreEqual(10, GuidanceScheduler.NormalizeIntervalMinutes(10));
            Assert.AreEqual(15, GuidanceScheduler.NormalizeIntervalMinutes(0, 15), "显式兜底值必须生效");
        }

        [TestMethod]
        public void SafeTick_ExceptionInsideTick_DoesNotPropagate()
        {
            bool ran = false;
            GuidanceScheduler.SafeTick("测试定时器", () =>
            {
                ran = true;
                throw new InvalidOperationException("boom");
            });

            Assert.IsTrue(ran, "tick 应被真实执行");
            // 不外抛即通过（外抛会直接让本测试失败）
        }

        [TestMethod]
        public void SafeTick_NullTick_IsSafeNoOp()
        {
            GuidanceScheduler.SafeTick("测试定时器", null);
        }

        [TestMethod]
        public void SafeTickAsync_ExceptionBeforeAndAfterAwait_DoesNotPropagate()
        {
            bool before = GuidanceScheduler.SafeTickAsync("测试", (Func<Task>)(() => throw new InvalidOperationException("sync boom"))).IsCompleted;
            Assert.IsTrue(before, "同步抛出的异常也应被隔离（不应在返回 Task 前外抛）");

            GuidanceScheduler.SafeTickAsync("测试", async () =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("async boom");
            }).Wait(TimeSpan.FromSeconds(5));
            // await 之后的异常同样不得外抛（Wait 不应抛 AggregateException）
        }

        [TestMethod]
        public async Task SafeTickAsync_SuccessPath_InvokesTick()
        {
            bool invoked = false;
            await GuidanceScheduler.SafeTickAsync("测试", () => { invoked = true; return Task.CompletedTask; });
            Assert.IsTrue(invoked);
        }

        [TestMethod]
        public void Configure_AfterDispose_Throws()
        {
            var scheduler = new GuidanceScheduler();
            scheduler.Dispose();
            try
            {
                scheduler.ConfigureSmartSystem(5, () => { });
                Assert.Fail("释放后再配置必须抛 ObjectDisposedException");
            }
            catch (ObjectDisposedException) { }
        }

        [TestMethod]
        public void Configure_Dispose_ConfigureLifecycle_IsSafe()
        {
            // 建立即释放（不真正等定时器触发）：验证生命周期收口无泄漏路径
            using (var scheduler = new GuidanceScheduler())
            {
                scheduler.ConfigureSmartSystem(5, () => { });
                scheduler.ConfigureTaskReminder(5, () => Task.CompletedTask);
                scheduler.Dispose();
                scheduler.Dispose(); // 幂等
            }
        }
    }
}
