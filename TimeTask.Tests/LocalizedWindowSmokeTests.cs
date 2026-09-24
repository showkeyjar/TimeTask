using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using TimeTask;

namespace TimeTask.Tests
{
    /// <summary>
    /// i18n 迁移窗口的实例化冒烟：在 STA 线程真实构造窗口，
    /// 验证 BAML 加载 + {loc:Loc} 标记扩展 + 构造器数据装载全链路不抛异常。
    /// （编译期只校验类型解析，标记扩展的实际 ProvideValue 在窗口实例化时才执行。）
    /// 注意：窗口归 STA 线程所有，标题等属性必须在 STA 线程内读取后带出，
    /// 不能在测试线程跨线程访问 DispatcherObject。
    /// </summary>
    [TestClass]
    public class LocalizedWindowSmokeTests
    {
        private static System.Collections.Generic.KeyValuePair<string, Exception> CreateOnSta<TWindow>(
            Func<TWindow, string> probe) where TWindow : Window, new()
        {
            return CreateOnSta(() => new TWindow(), probe);
        }

        private static System.Collections.Generic.KeyValuePair<string, Exception> CreateOnSta<TWindow>(
            Func<TWindow> factory, Func<TWindow, string> probe) where TWindow : Window
        {
            string result = null;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = factory();
                    result = probe(window);
                    window.Close();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(30));
            return new System.Collections.Generic.KeyValuePair<string, Exception>(result, error);
        }

        [TestMethod]
        public void SetLearningPlanWindow_Instantiates_WithLocBindings()
        {
            var r = CreateOnSta<SetLearningPlanWindow>(w => w.Title);
            Assert.IsNull(r.Value, $"窗口实例化失败: {r.Value}");
            // 标题应来自资源（默认 zh-CN）：非空且等于资源值，而不是裸键名回退
            StringAssert.Contains(r.Key, "学习计划");
        }

        [TestMethod]
        public void TaskStatisticsWindow_Instantiates_WithLocBindings()
        {
            var r = CreateOnSta<TaskStatisticsWindow>(w => w.Title);
            Assert.IsNull(r.Value, $"窗口实例化失败: {r.Value}");
            StringAssert.Contains(r.Key, "任务统计");
        }

        [TestMethod]
        public void ActionInboxWindow_Instantiates_WithLocBindings()
        {
            // 伪造一场会议结果：无音频目录（回放区折叠）、有摘要、带 1 条行动项
            var result = new ConversationCaptureService.ConversationCaptureResult
            {
                Type = ConversationType.Meeting,
                StartTime = DateTime.Now.AddMinutes(-10),
                EndTime = DateTime.Now,
                AsrAvailable = true,
                Summary = "测试摘要",
                AudioFolderPath = null,
                Actions = new List<TimeTask.TaskDraft>
                {
                    new TaskDraft { RawText = "测试行动项", CleanedText = "测试行动项", EstimatedQuadrant = "重要不紧急" }
                }
            };
            string title = null; string meta = null; Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var w = new ActionInboxWindow(result);
                    title = w.Title;
                    meta = w.GetType().GetField("_items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.GetValue(w) is System.Collections.ICollection c ? c.Count.ToString() : null;
                    w.Close();
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(30));
            Assert.IsNull(error, $"窗口实例化失败: {error}");
            StringAssert.Contains(title, "录音整理");
            Assert.AreEqual("1", meta, "行动项应加载 1 条到 VM 集合");
        }

        [TestMethod]
        public void LearningPlanManagerWindow_Instantiates_WithLocBindings()
        {
            var plan = new LongTermGoal
            {
                Subject = "英语",
                Description = "提升口语",
                TotalDuration = "30",
                IsLearningPlan = true,
                TotalStages = 3,
                CompletedStages = 1
            };
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tt-lpm-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(tempDir);
            try
            {
                var r = CreateOnSta(() => new LearningPlanManagerWindow(plan, tempDir), w => w.Title);
                Assert.IsNull(r.Value, $"窗口实例化失败: {r.Value}");
                StringAssert.Contains(r.Key, "学习计划管理");
            }
            finally
            {
                try { System.IO.Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
