using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace TimeTask
{
    /// <summary>
    /// 无 UI 自检模式（启动参数 --diagnostics 或 --selfcheck）：
    /// 一条命令收集「常见安装/配置问题」的全部现场证据，替代用户口述排障。
    ///
    /// 覆盖：数据/录音目录解析与可写性、四象限 CSV 完整性（坏行检测）、
    /// JSON 存储明显损坏（截断/U+FFFD）、磁盘剩余空间、关键 App.config 键合法性、
    /// FunASR 环境（预置包/本机 python）、日志位置。
    ///
    /// 退出码：0 = 无 FAIL（可有 WARN）；2 = 存在 FAIL；报告全文写入
    /// %AppData%\TimeTask\logs\diagnostics-*.txt，加 --quiet 可跳过弹窗（供脚本调用）。
    ///
    /// 设计约束：检查逻辑全部为可注入路径的纯函数（测试友好），
    /// 进程探测（python）等慢操作可通过参数跳过；绝不弹 UI、绝不写用户数据。
    /// </summary>
    public static class DiagnosticsSelfCheck
    {
        public enum CheckStatus { Ok, Warn, Fail }

        public sealed class CheckResult
        {
            public string Name;
            public CheckStatus Status;
            public string Detail;
        }

        private const string ReportPrefix = "[TimeTask Diagnostics] ";

        /// <summary>启动参数是否请求了自检模式（--diagnostics / --selfcheck / /diagnostics，大小写不敏感）。</summary>
        public static bool IsRequested(string[] args)
        {
            if (args == null) return false;
            foreach (var a in args)
            {
                var t = (a ?? string.Empty).Trim();
                if (string.Equals(t, "--diagnostics", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t, "--selfcheck", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t, "/diagnostics", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasQuiet(string[] args)
        {
            if (args == null) return false;
            return args.Any(a => string.Equals((a ?? string.Empty).Trim(), "--quiet", StringComparison.OrdinalIgnoreCase));
        }

        // ---------- 各项检查（纯逻辑，路径可注入） ----------

        /// <summary>目录存在且可写（写-读-删一个探针文件验证）。</summary>
        public static CheckResult CheckDirectoryWritable(string label, string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new CheckResult { Name = label, Status = CheckStatus.Fail, Detail = "路径为空" };
                }
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                string probe = Path.Combine(path, ".diag_probe_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                File.WriteAllText(probe, "ok", new UTF8Encoding(false));
                string readBack = File.ReadAllText(probe);
                File.Delete(probe);
                if (readBack != "ok")
                {
                    return new CheckResult { Name = label, Status = CheckStatus.Fail, Detail = path + "（探针读回不一致）" };
                }
                return new CheckResult { Name = label, Status = CheckStatus.Ok, Detail = path };
            }
            catch (Exception ex)
            {
                return new CheckResult { Name = label, Status = CheckStatus.Fail, Detail = path + "（" + ex.GetType().Name + ": " + ex.Message + "）" };
            }
        }

        /// <summary>
        /// 四象限 CSV 完整性：每象限统计任务行数与「坏行」（逗号字段数 &lt; 4，
        /// 与 HelperClass.ReadCsvCore 的判定口径一致）。坏行会被运行时跳过，
        /// 因此这里只报 WARN（数据仍可加载），但提示用户备份修复。
        /// </summary>
        public static CheckResult CheckQuadrantCsvs(string dataDir)
        {
            try
            {
                var sb = new StringBuilder();
                int totalBad = 0;
                for (int q = 1; q <= 4; q++)
                {
                    string path = QuadrantStore.PathFor(q, dataDir);
                    if (!File.Exists(path))
                    {
                        sb.Append($"Q{q}=无文件; ");
                        continue;
                    }
                    string[] lines = File.ReadAllLines(path);
                    int dataRows = 0, bad = 0;
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        dataRows++;
                        if (line.Split(',').Length < 4) bad++;
                    }
                    // 减去表头（首条非空行是表头，不按坏行计）
                    if (dataRows > 0 && lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.StartsWith("task,") == true)
                    {
                        dataRows--;
                    }
                    totalBad += bad;
                    sb.Append($"Q{q}={dataRows}行" + (bad > 0 ? $"(坏行x{bad})" : "") + "; ");
                }
                return new CheckResult
                {
                    Name = "四象限 CSV",
                    Status = totalBad > 0 ? CheckStatus.Warn : CheckStatus.Ok,
                    Detail = sb.ToString().TrimEnd(' ', ';') + (totalBad > 0 ? " —— 坏行运行时会跳过，建议检查是否手工编辑损坏" : "")
                };
            }
            catch (Exception ex)
            {
                return new CheckResult { Name = "四象限 CSV", Status = CheckStatus.Fail, Detail = ex.GetType().Name + ": " + ex.Message };
            }
        }

        /// <summary>
        /// JSON 存储明显损坏检测（轻量，不依赖具体序列化器）：
        /// 空文件 / 首尾大括号不配对（典型截断）/ 含 U+FFFD（编码损坏）→ WARN。
        /// JsonStore 运行时会自动回退 .bak，这里提示的是「主文件已损坏」这一事实。
        /// </summary>
        public static CheckResult CheckJsonStores(string dataDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
                {
                    return new CheckResult { Name = "JSON 存储", Status = CheckStatus.Ok, Detail = "数据目录不存在（首次启动属正常）" };
                }
                var files = Directory.EnumerateFiles(dataDir, "*.json", SearchOption.AllDirectories).Take(100).ToList();
                if (files.Count == 0)
                {
                    return new CheckResult { Name = "JSON 存储", Status = CheckStatus.Ok, Detail = "无 JSON 文件" };
                }
                var bad = new List<string>();
                int checkedCount = 0;
                foreach (var f in files)
                {
                    checkedCount++;
                    try
                    {
                        string text = File.ReadAllText(f, Encoding.UTF8);
                        string trimmed = text.Trim();
                        if (trimmed.Length == 0) { bad.Add(Path.GetFileName(f) + "(空)"); continue; }
                        if (trimmed.IndexOf('\uFFFD') >= 0) { bad.Add(Path.GetFileName(f) + "(乱码)"); continue; }
                        char first = trimmed[0], last = trimmed[trimmed.Length - 1];
                        bool startsOk = first == '{' || first == '[';
                        bool endsOk = last == '}' || last == ']';
                        if (!startsOk || !endsOk) { bad.Add(Path.GetFileName(f) + "(疑似截断)"); continue; }
                    }
                    catch (Exception ex)
                    {
                        bad.Add(Path.GetFileName(f) + "(" + ex.GetType().Name + ")");
                    }
                }
                var status = bad.Count > 0 ? CheckStatus.Warn : CheckStatus.Ok;
                return new CheckResult
                {
                    Name = "JSON 存储",
                    Status = status,
                    Detail = $"检查 {checkedCount} 个" + (bad.Count > 0 ? "；可疑: " + string.Join(", ", bad) + "（运行时会尝试 .bak 回退）" : "，全部正常")
                };
            }
            catch (Exception ex)
            {
                return new CheckResult { Name = "JSON 存储", Status = CheckStatus.Fail, Detail = ex.GetType().Name + ": " + ex.Message };
            }
        }

        /// <summary>数据盘剩余空间：&lt;500MB 警告，&lt;50MB 失败（录音/模型都会写不进）。</summary>
        public static CheckResult CheckDiskSpace(string pathOnDrive)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(pathOnDrive));
                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    return new CheckResult { Name = "磁盘空间", Status = CheckStatus.Warn, Detail = root + " 未就绪" };
                }
                double mb = drive.AvailableFreeSpace / 1024.0 / 1024.0;
                if (mb < 50)
                {
                    return new CheckResult { Name = "磁盘空间", Status = CheckStatus.Fail, Detail = $"{root} 仅剩 {mb:F0} MB" };
                }
                if (mb < 500)
                {
                    return new CheckResult { Name = "磁盘空间", Status = CheckStatus.Warn, Detail = $"{root} 仅剩 {mb:F0} MB（录音/模型缓存可能失败）" };
                }
                return new CheckResult { Name = "磁盘空间", Status = CheckStatus.Ok, Detail = $"{root} 剩余 {mb / 1024.0:F1} GB" };
            }
            catch (Exception ex)
            {
                return new CheckResult { Name = "磁盘空间", Status = CheckStatus.Warn, Detail = ex.GetType().Name + ": " + ex.Message };
            }
        }

        /// <summary>关键 App.config 键合法性（非法值不致命——运行时有兜底——但值得提示）。</summary>
        public static CheckResult CheckConfigKeys()
        {
            try
            {
                var problems = new List<string>();
                var asrEngine = ConfigurationManager.AppSettings["ConversationCaptureAsrEngine"];
                if (!string.IsNullOrWhiteSpace(asrEngine) &&
                    !string.Equals(asrEngine, "auto", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(asrEngine, "funasr", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(asrEngine, "vosk", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"ConversationCaptureAsrEngine={asrEngine}（应为 auto/funasr/vosk）");
                }
                var retention = ConfigurationManager.AppSettings["ConversationCaptureRetentionDays"];
                if (!string.IsNullOrWhiteSpace(retention) && (!int.TryParse(retention, out int days) || days < 0))
                {
                    problems.Add($"ConversationCaptureRetentionDays={retention}（应为 ≥0 的整数）");
                }
                var retryMax = ConfigurationManager.AppSettings["LlmRetryMaxAttempts"];
                if (!string.IsNullOrWhiteSpace(retryMax) && (!int.TryParse(retryMax, out int attempts) || attempts < 1 || attempts > 6))
                {
                    problems.Add($"LlmRetryMaxAttempts={retryMax}（应为 1..6）");
                }
                return new CheckResult
                {
                    Name = "关键配置",
                    Status = problems.Count > 0 ? CheckStatus.Warn : CheckStatus.Ok,
                    Detail = problems.Count > 0 ? string.Join("; ", problems) : "关键键值合法"
                };
            }
            catch (Exception ex)
            {
                return new CheckResult { Name = "关键配置", Status = CheckStatus.Warn, Detail = ex.GetType().Name + ": " + ex.Message };
            }
        }

        /// <summary>FunASR 环境：预置包 zip 是否存在、本机 python 是否可用（--server 依赖）。</summary>
        public static CheckResult CheckFunAsrEnvironment(string dataDir, bool skipProcessProbe)
        {
            try
            {
                var notes = new List<string>();
                string bundle = string.IsNullOrWhiteSpace(dataDir) ? null : Path.Combine(dataDir, "funasr-runtime-bundle.zip");
                if (bundle != null && File.Exists(bundle))
                {
                    notes.Add($"预置包存在（{new FileInfo(bundle).Length / 1024.0 / 1024.0:F0} MB）");
                }
                else
                {
                    notes.Add("无预置包（将使用本机 python 环境）");
                }

                bool pythonFound = false;
                if (!skipProcessProbe)
                {
                    foreach (var exe in new[] { "python", "py" })
                    {
                        string version = ProbeProcessOutput(exe, "--version", 3000);
                        if (version != null)
                        {
                            notes.Add($"{exe} 可用: {version.Trim()}");
                            pythonFound = true;
                            break;
                        }
                    }
                    if (!pythonFound) notes.Add("python/py 均不可用（FunASR 高精度识别将不可用，Vosk 回落不受影响）");
                }

                return new CheckResult
                {
                    Name = "FunASR 环境",
                    Status = CheckStatus.Ok,
                    Detail = string.Join("; ", notes)
                };
            }
            catch (Exception ex)
            {
                return new CheckResult { Name = "FunASR 环境", Status = CheckStatus.Warn, Detail = ex.GetType().Name + ": " + ex.Message };
            }
        }

        private static string ProbeProcessOutput(string exe, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return null;
                    }
                    return p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
                }
            }
            catch
            {
                return null;
            }
        }

        // ---------- 报告组装 ----------

        /// <summary>生成完整诊断报告文本（纯函数：路径可注入、慢探测可跳过）。</summary>
        public static string BuildReport(string dataDir, string recordingsDir, bool skipProcessProbes)
        {
            var results = new List<CheckResult>
            {
                new CheckResult
                {
                    Name = "运行环境",
                    Status = CheckStatus.Ok,
                    Detail = $"v={typeof(DiagnosticsSelfCheck).Assembly.GetName().Version}, " +
                             $"OS={(Environment.Is64BitOperatingSystem ? "x64" : "x86")}, " +
                             $"Process={(Environment.Is64BitProcess ? "x64" : "x86")}, " +
                             $"BaseDir={AppDomain.CurrentDomain.BaseDirectory}"
                },
                CheckDirectoryWritable("数据目录", dataDir),
                CheckDirectoryWritable("录音目录", recordingsDir),
                CheckQuadrantCsvs(dataDir),
                CheckJsonStores(dataDir),
                CheckDiskSpace(dataDir),
                CheckConfigKeys(),
                CheckFunAsrEnvironment(dataDir, skipProcessProbes)
            };

            int fail = results.Count(r => r.Status == CheckStatus.Fail);
            int warn = results.Count(r => r.Status == CheckStatus.Warn);

            var sb = new StringBuilder();
            sb.AppendLine("=====================================================");
            sb.AppendLine($" TimeTask 诊断报告  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($" 结果: {(fail > 0 ? "发现问题(FAIL)" : warn > 0 ? "有提示(WARN)" : "全部正常(OK)")}   FAIL={fail}  WARN={warn}");
            sb.AppendLine("=====================================================");
            foreach (var r in results)
            {
                string tag = r.Status == CheckStatus.Ok ? "OK" : r.Status == CheckStatus.Warn ? "WARN" : "FAIL";
                sb.AppendLine($" [{tag}] {r.Name}: {r.Detail}");
            }
            sb.AppendLine("=====================================================");
            sb.AppendLine(" 提示: 常见问题处理");
            sb.AppendLine("   - 数据目录不可写: 若安装在 Program Files，请以管理员运行或改用 %AppData% 模式");
            sb.AppendLine("   - CSV 坏行/JSON 截断: 运行时自动跳过或回退 .bak，可从 data 目录的 .bak 恢复");
            sb.AppendLine("   - FunASR 不可用: 首次需下载约 230MB 模型；或放置 data/funasr-runtime-bundle.zip 预置包");
            sb.AppendLine("   - 完整运行日志: 见本报告同目录 voice-runtime.log");
            sb.AppendLine("=====================================================");
            return sb.ToString();
        }

        /// <summary>
        /// 真实环境入口：生成报告 → 写入日志目录 diagnostics-*.txt → 输出到控制台 →
        /// 交互模式下弹窗提示报告路径（--quiet 跳过）→ Environment.Exit（0=无FAIL / 2=有FAIL）。
        /// </summary>
        public static void RunAndExit(string[] args)
        {
            int exitCode;
            string reportPath = null;
            try
            {
                string report = BuildReport(AppPaths.DataDir, AppPaths.RecordingsDir, skipProcessProbes: false);
                Console.WriteLine(report);

                string dir = null;
                try { dir = Path.GetDirectoryName(VoiceRuntimeLog.LogFilePath); } catch { }
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeTask", "logs");
                    Directory.CreateDirectory(dir);
                }
                reportPath = Path.Combine(dir, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.WriteAllText(reportPath, report, new UTF8Encoding(true));

                bool hasFail = report.Contains("[FAIL]");
                exitCode = hasFail ? 2 : 0;

                if (!HasQuiet(args) && Environment.UserInteractive)
                {
                    System.Windows.MessageBox.Show(
                        $"诊断完成（{(exitCode == 0 ? "未发现严重问题" : "发现问题，详见报告")}）。\n\n报告已保存到：\n{reportPath}",
                        "TimeTask 诊断", System.Windows.MessageBoxButton.OK,
                        exitCode == 0 ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ReportPrefix + "自检自身失败: " + ex);
                exitCode = 2;
            }
            Environment.Exit(exitCode);
        }
    }
}
