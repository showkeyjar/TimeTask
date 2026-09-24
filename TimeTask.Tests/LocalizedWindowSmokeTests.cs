using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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
            string result = null;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new TWindow();
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
    }
}
