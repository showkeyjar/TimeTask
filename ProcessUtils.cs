using System;
using System.Diagnostics;

namespace TimeTask
{
    /// <summary>
    /// 进程树终止工具。
    /// .NET Framework 4.7.2 的 Process.Kill() 只终止进程本身：
    /// python（pip / torch / funasr）派生出的孙进程会变成孤儿继续占 CPU/显存；
    /// 而对仍在运行的进程只 Dispose（例如等待任务被取消时）则什么都不会杀，进程直接脱管。
    /// 这里统一用 taskkill /T /F 递归终止整棵进程树，taskkill 不可用时退回普通 Kill。
    /// </summary>
    internal static class ProcessUtils
    {
        public static void KillTree(Process process, string reason = null)
        {
            if (process == null)
            {
                return;
            }

            int pid;
            string name;
            try
            {
                if (process.HasExited)
                {
                    return;
                }
                pid = process.Id;
                name = process.ProcessName;
            }
            catch
            {
                // 进程已退出或句柄失效：无事可做
                return;
            }

            VoiceRuntimeLog.Info(
                $"KillTree: pid={pid}, name={name}" + (string.IsNullOrEmpty(reason) ? "" : $", reason={reason}"));

            try
            {
                var psi = new ProcessStartInfo("taskkill", $"/PID {pid} /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var killer = Process.Start(psi))
                {
                    killer?.WaitForExit(5000);
                }
            }
            catch
            {
                try { process.Kill(); } catch { }
            }
            finally
            {
                try { process.WaitForExit(3000); } catch { }
            }
        }
    }
}
