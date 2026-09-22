using System;
using System.Threading.Tasks;
using System.Windows;

namespace TimeTask
{
    /// <summary>
    /// UI 事件处理器（async void）的统一异常防护。
    /// async void 的异常会跳到 DispatcherUnhandledException——全局兜底虽在
    /// （App 里 Handled=true，不会崩），但用户看到的是无上下文的吓人弹窗；
    /// 这里按「操作名」就地记录并给出友好反馈，让每个入口自带语义。
    /// 用法：事件处理器只留一行 await UiSafe.RunAsync("操作名", CoreAsync)，
    /// 原方法体改名为 XxxCoreAsync（签名 async Task，正文不动）。
    /// </summary>
    internal static class UiSafe
    {
        /// <summary>
        /// 执行 body；异常时记日志（含操作名），notifyUser=true 时弹友好提示。
        /// 本方法自身绝不抛异常（调用方仍是 async void，但已无未隔离路径）。
        /// </summary>
        public static async Task RunAsync(string operationName, Func<Task> body, bool notifyUser = true)
        {
            try
            {
                await body().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error($"UI 操作失败：{operationName}。", ex);
                if (notifyUser)
                {
                    try
                    {
                        MessageBox.Show($"{operationName}失败：{ex.Message}\n\n详情已写入日志：{VoiceRuntimeLog.LogFilePath}",
                            "TimeTask", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch
                    {
                        // 无 UI 上下文（测试）时静默
                    }
                }
            }
        }
    }
}
