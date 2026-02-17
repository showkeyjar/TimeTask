using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TimeTask
{
    internal sealed class PythonVersionInfo
    {
        public string Executable { get; set; }
        public int Major { get; set; }
        public int Minor { get; set; }

        public override string ToString()
        {
            return $"{Major}.{Minor}";
        }
    }

    internal sealed class FunAsrRuntimeBootstrapResult
    {
        public bool IsReady { get; private set; }
        public string PythonExe { get; private set; }
        public string Message { get; private set; }

        public static FunAsrRuntimeBootstrapResult Ready(string pythonExe, string message)
        {
            return new FunAsrRuntimeBootstrapResult
            {
                IsReady = true,
                PythonExe = pythonExe ?? string.Empty,
                Message = message ?? "ok"
            };
        }

        public static FunAsrRuntimeBootstrapResult NotReady(string pythonExe, string message)
        {
            return new FunAsrRuntimeBootstrapResult
            {
                IsReady = false,
                PythonExe = pythonExe ?? string.Empty,
                Message = message ?? "not-ready"
            };
        }
    }

    internal static class FunAsrRuntimeManager
    {
        private static readonly object Sync = new object();
        private static Task<FunAsrRuntimeBootstrapResult> _bootstrapTask;
        private static readonly object LogSync = new object();
        private static DateTime _lastPipProgressLogUtc = DateTime.MinValue;

        public static Task<FunAsrRuntimeBootstrapResult> EnsureReadyAsync(CancellationToken cancellationToken = default)
        {
            lock (Sync)
            {
                if (_bootstrapTask == null)
                {
                    _bootstrapTask = EnsureReadyInternalAsync(cancellationToken);
                }
                return _bootstrapTask;
            }
        }

        public static Task<FunAsrRuntimeBootstrapResult> ForceRebootstrapAsync(CancellationToken cancellationToken = default)
        {
            lock (Sync)
            {
                _bootstrapTask = EnsureReadyInternalAsync(cancellationToken);
                return _bootstrapTask;
            }
        }

        public static void KickoffIfNeeded()
        {
            try
            {
                bool autoBootstrap = ReadBool("FunAsrAutoBootstrap", true);
                string provider = ReadString("VoiceAsrProvider", "hybrid");
                VoiceRuntimeLog.Info($"FunASR kickoff check: autoBootstrap={autoBootstrap}, provider={provider}");

                if (!autoBootstrap)
                {
                    VoiceRuntimeLog.Info("FunASR kickoff skipped: auto bootstrap disabled.");
                    return;
                }

                if (provider.IndexOf("funasr", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    VoiceRuntimeLog.Info("FunASR kickoff skipped: provider does not include funasr.");
                    return;
                }

                VoiceListenerStatusCenter.Publish(VoiceListenerState.Loading, "正在准备 FunASR 运行环境");
                VoiceRuntimeLog.Info("FunASR kickoff started.");
                _ = EnsureReadyAsync();
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR runtime kickoff failed.", ex);
            }
        }

        private static async Task<FunAsrRuntimeBootstrapResult> EnsureReadyInternalAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!ReadBool("FunAsrAutoBootstrap", true))
                {
                    string fallback = ReadString("FunAsrPythonExe", "python");
                    VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "FunASR 自动准备已关闭");
                    return FunAsrRuntimeBootstrapResult.NotReady(fallback, "auto-bootstrap-disabled");
                }

                string configuredPython = ReadString("FunAsrPythonExe", "python");
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string runtimeRoot = Path.Combine(appData, "TimeTask", "funasr-runtime");
                bool preferPrebuiltRuntime = ReadBool("FunAsrPreferPrebuiltRuntime", true);
                bool allowOnlineInstallFallback = ReadBool("FunAsrAllowOnlineInstallFallback", false);
                string bundlePath = ReadString("FunAsrRuntimeBundlePath", "funasr-runtime-bundle.zip");

                Directory.CreateDirectory(runtimeRoot);
                VoiceRuntimeLog.Info($"FunASR runtime strategy: preferPrebuiltRuntime={preferPrebuiltRuntime}, allowOnlineInstallFallback={allowOnlineInstallFallback}, bundlePath={bundlePath}");

                if (preferPrebuiltRuntime)
                {
                    PublishProgress(1, 7, "检查预置语音运行包", VoiceListenerState.Loading);
                    var prebuilt = TryUsePrebuiltRuntime(runtimeRoot, bundlePath);
                    if (prebuilt.IsReady)
                    {
                        VoiceRuntimeLog.Info($"FunASR bootstrap: using prebuilt runtime. python={prebuilt.PythonExe}, source={prebuilt.Message}");
                        PublishProgress(6, 7, "预置语音环境就绪，初始化监听", VoiceListenerState.Loading);
                        PublishProgress(7, 7, "语音监听可用", VoiceListenerState.Ready);
                        return prebuilt;
                    }

                    if (!allowOnlineInstallFallback)
                    {
                        string msg = $"未找到可用预置语音包（{prebuilt.Message}）";
                        VoiceRuntimeLog.Info($"FunASR bootstrap stop: {msg}");
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, msg);
                        return FunAsrRuntimeBootstrapResult.NotReady(configuredPython, $"prebuilt-runtime-not-ready:{prebuilt.Message}");
                    }

                    VoiceRuntimeLog.Info($"FunASR bootstrap: prebuilt runtime unavailable ({prebuilt.Message}), fallback to online bootstrap.");
                }

                PublishProgress(1, 7, "检测 Python 运行环境", VoiceListenerState.Loading);
                string pythonExe = await ResolvePythonExecutableAsync(configuredPython, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pythonExe))
                {
                    VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "未检测到可用 Python，语音监听暂不可用");
                    return FunAsrRuntimeBootstrapResult.NotReady(configuredPython, "python-not-found");
                }

                int maxSupportedMinor = ReadInt("FunAsrMaxPythonMinor", 12);
                PublishProgress(2, 7, "检查 Python 版本", VoiceListenerState.Loading);
                var basePythonVersion = await GetPythonVersionAsync(pythonExe, cancellationToken).ConfigureAwait(false);
                if (!IsSupportedPythonVersion(basePythonVersion, maxSupportedMinor))
                {
                    string appDataForProvision = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string runtimeRootForProvision = Path.Combine(appDataForProvision, "TimeTask", "funasr-runtime");
                    Directory.CreateDirectory(runtimeRootForProvision);

                    var provisioned = await TryProvisionCompatiblePythonAsync(runtimeRootForProvision, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(provisioned))
                    {
                        var provisionedVersion = await GetPythonVersionAsync(provisioned, cancellationToken).ConfigureAwait(false);
                        if (IsSupportedPythonVersion(provisionedVersion, maxSupportedMinor))
                        {
                            pythonExe = provisioned;
                            basePythonVersion = provisionedVersion;
                            VoiceRuntimeLog.Info($"FunASR bootstrap: switched to provisioned Python {basePythonVersion} ({pythonExe})");
                        }
                    }
                }

                if (!IsSupportedPythonVersion(basePythonVersion, maxSupportedMinor))
                {
                    string versionText = basePythonVersion == null ? "unknown" : basePythonVersion.ToString();
                    string msg = $"FunASR 暂不支持当前 Python {versionText}，请安装 Python 3.10/3.11/3.12。";
                    VoiceRuntimeLog.Info(msg);
                    VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, msg);
                    return FunAsrRuntimeBootstrapResult.NotReady(pythonExe, $"python-version-unsupported:{versionText}");
                }

                string venvPath = Path.Combine(runtimeRoot, "venv");
                string venvPython = Path.Combine(venvPath, "Scripts", "python.exe");
                string markerPath = Path.Combine(runtimeRoot, "install.ok");
                string healthPath = Path.Combine(runtimeRoot, "health.json");
                string packageSpec = ReadString("FunAsrPipPackages", "funasr modelscope torch torchaudio");
                string baseDependencySpec = ReadString("FunAsrBaseDependencies", "modelscope torch torchaudio");
                string failCachePath = Path.Combine(runtimeRoot, "install.fail");
                bool useNoDepsInstallStrategy = ReadBool("FunAsrInstallUseNoDepsStrategy", true);
                VoiceRuntimeLog.Info($"FunASR bootstrap config: runtimeRoot={runtimeRoot}, useNoDepsInstallStrategy={useNoDepsInstallStrategy}, packageSpec={packageSpec}, baseDeps={baseDependencySpec}");

                if (!File.Exists(venvPython))
                {
                    PublishProgress(3, 7, "创建语音运行环境", VoiceListenerState.Installing);
                    VoiceRuntimeLog.Info($"FunASR bootstrap: creating venv at {venvPath}");
                    var createVenv = await RunProcessAsync(
                        pythonExe,
                        $"-m venv \"{venvPath}\"",
                        Math.Max(30, ReadInt("FunAsrBootstrapTimeoutSeconds", 900)),
                        cancellationToken).ConfigureAwait(false);

                    if (createVenv.ExitCode != 0 || !File.Exists(venvPython))
                    {
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音环境创建失败");
                        return FunAsrRuntimeBootstrapResult.NotReady(configuredPython, $"venv-create-failed: {createVenv.Stderr}");
                    }
                }

                string markerKey = $"packages={packageSpec}";
                bool needsInstall = true;
                if (File.Exists(markerPath))
                {
                    string existing = File.ReadAllText(markerPath).Trim();
                    needsInstall = !string.Equals(existing, markerKey, StringComparison.Ordinal);
                }

                if (needsInstall)
                {
                    int timeoutSeconds = Math.Max(120, ReadInt("FunAsrBootstrapTimeoutSeconds", 900));
                    if (IsInstallFailureCoolingDown(
                        failCachePath,
                        markerKey,
                        venvPython,
                        ReadInt("FunAsrInstallRetryCooldownMinutes", 30),
                        out string cooldownReason,
                        out int remainingSeconds))
                    {
                        if (useNoDepsInstallStrategy && IsEditdistanceBuildFailure(cooldownReason, cooldownReason))
                        {
                            VoiceRuntimeLog.Info("FunASR bootstrap: cooldown bypassed (no-deps strategy + editdistance historical failure).");
                            ClearInstallFailureCache(failCachePath);
                        }
                        else
                        {
                        VoiceRuntimeLog.Info($"FunASR bootstrap: install skipped by cooldown. reason={cooldownReason}");
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, $"语音环境冷却中，{remainingSeconds}s 后自动重试");
                        return FunAsrRuntimeBootstrapResult.NotReady(configuredPython, $"install-cooldown:retry-after-sec={remainingSeconds};detail={cooldownReason}");
                        }
                    }

                    PublishProgress(4, 7, "安装语音识别依赖", VoiceListenerState.Installing);
                    VoiceRuntimeLog.Info($"FunASR bootstrap: installing pip packages ({packageSpec})");

                    var pipUpgrade = await RunProcessAsync(
                        venvPython,
                        "-m pip install --disable-pip-version-check --no-input --upgrade pip setuptools wheel",
                        timeoutSeconds,
                        cancellationToken).ConfigureAwait(false);

                    if (pipUpgrade.ExitCode != 0)
                    {
                        VoiceRuntimeLog.Info($"FunASR bootstrap: pip upgrade failed but continue. detail={BuildProcessFailureSummary(pipUpgrade)}");
                    }

                    (int ExitCode, string Stdout, string Stderr) pipInstall;
                    if (useNoDepsInstallStrategy)
                    {
                        bool noDepsOk = await TryInstallFunAsrNoDepsFallbackAsync(
                            venvPython,
                            baseDependencySpec,
                            timeoutSeconds,
                            cancellationToken).ConfigureAwait(false);

                        if (noDepsOk)
                        {
                            File.WriteAllText(markerPath, markerKey);
                            SaveHealthCache(healthPath, markerKey, venvPython, true);
                            ClearInstallFailureCache(failCachePath);
                            PublishProgress(6, 7, "语音环境就绪，初始化监听", VoiceListenerState.Loading);
                            PublishProgress(7, 7, "语音监听可用", VoiceListenerState.Ready);
                            return FunAsrRuntimeBootstrapResult.Ready(venvPython, "ok");
                        }

                        pipInstall = await RunProcessAsync(
                            venvPython,
                            $"-m pip install --disable-pip-version-check --no-input --prefer-binary {packageSpec}",
                            timeoutSeconds,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        pipInstall = await RunProcessAsync(
                            venvPython,
                            $"-m pip install --disable-pip-version-check --no-input --prefer-binary {packageSpec}",
                            timeoutSeconds,
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (pipInstall.ExitCode != 0)
                    {
                        if (IsEditdistanceBuildFailure(pipInstall.Stderr, pipInstall.Stdout))
                        {
                            VoiceRuntimeLog.Info("FunASR bootstrap: detected editdistance wheel build failure, trying compatibility fallback.");
                            bool fallbackOk = await TryInstallEditdistanceCompatibilityAsync(venvPython, timeoutSeconds, cancellationToken).ConfigureAwait(false);
                            if (fallbackOk)
                            {
                                var retryInstall = await RunProcessAsync(
                                    venvPython,
                                    $"-m pip install --disable-pip-version-check --no-input --prefer-binary {packageSpec}",
                                    timeoutSeconds,
                                    cancellationToken).ConfigureAwait(false);

                                if (retryInstall.ExitCode == 0)
                                {
                                    File.WriteAllText(markerPath, markerKey);
                                    SaveHealthCache(healthPath, markerKey, venvPython, true);
                                    ClearInstallFailureCache(failCachePath);
                                    PublishProgress(6, 7, "语音环境就绪，初始化监听", VoiceListenerState.Loading);
                                    PublishProgress(7, 7, "语音监听可用", VoiceListenerState.Ready);
                                    return FunAsrRuntimeBootstrapResult.Ready(venvPython, "ok");
                                }

                                pipInstall = retryInstall;
                            }

                            if (pipInstall.ExitCode != 0)
                            {
                                VoiceRuntimeLog.Info("FunASR bootstrap: trying no-deps fallback install for funasr.");
                                bool noDepsOk = await TryInstallFunAsrNoDepsFallbackAsync(
                                    venvPython,
                                    baseDependencySpec,
                                    timeoutSeconds,
                                    cancellationToken).ConfigureAwait(false);
                                if (noDepsOk)
                                {
                                    File.WriteAllText(markerPath, markerKey);
                                    SaveHealthCache(healthPath, markerKey, venvPython, true);
                                    ClearInstallFailureCache(failCachePath);
                                    PublishProgress(6, 7, "语音环境就绪，初始化监听", VoiceListenerState.Loading);
                                    PublishProgress(7, 7, "语音监听可用", VoiceListenerState.Ready);
                                    return FunAsrRuntimeBootstrapResult.Ready(venvPython, "ok");
                                }
                            }
                        }

                        SaveInstallFailureCache(failCachePath, markerKey, venvPython, BuildProcessFailureSummary(pipInstall));
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音依赖安装失败");
                        return FunAsrRuntimeBootstrapResult.NotReady(configuredPython, $"pip-install-failed: {BuildProcessFailureSummary(pipInstall)}");
                    }

                    File.WriteAllText(markerPath, markerKey);
                    SaveHealthCache(healthPath, markerKey, venvPython, true);
                    ClearInstallFailureCache(failCachePath);
                }
                else
                {
                    bool skipHeavyHealthCheck = IsHealthCacheFresh(healthPath, markerKey, venvPython, ReadInt("FunAsrHealthCacheHours", 72));
                    bool healthy = skipHeavyHealthCheck;
                    if (!skipHeavyHealthCheck)
                    {
                        PublishProgress(5, 7, "校验语音依赖完整性", VoiceListenerState.Loading);
                        healthy = await VerifyFunAsrDependenciesAsync(
                            venvPython,
                            Math.Max(30, ReadInt("FunAsrBootstrapTimeoutSeconds", 900)),
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (!healthy)
                    {
                        int timeoutSeconds = Math.Max(120, ReadInt("FunAsrBootstrapTimeoutSeconds", 900));
                        VoiceListenerStatusCenter.Publish(VoiceListenerState.Installing, "检测到语音依赖异常，正在自动修复");
                        VoiceRuntimeLog.Info($"FunASR bootstrap: dependency health check failed, reinstalling packages ({packageSpec})");

                        var pipInstallRepair = await RunProcessAsync(
                            venvPython,
                            $"-m pip install --disable-pip-version-check --no-input --prefer-binary --upgrade {packageSpec}",
                            timeoutSeconds,
                            cancellationToken).ConfigureAwait(false);

                        if (pipInstallRepair.ExitCode != 0)
                        {
                            VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音依赖修复失败");
                            return FunAsrRuntimeBootstrapResult.NotReady(configuredPython, $"pip-repair-failed: {BuildProcessFailureSummary(pipInstallRepair)}");
                        }

                        bool healthyAfterRepair = await VerifyFunAsrDependenciesAsync(
                            venvPython,
                            Math.Max(30, timeoutSeconds),
                            cancellationToken).ConfigureAwait(false);
                        if (!healthyAfterRepair)
                        {
                            SaveInstallFailureCache(failCachePath, markerKey, venvPython, "dependency-health-check-failed-after-repair");
                            VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音依赖校验失败");
                            return FunAsrRuntimeBootstrapResult.NotReady(configuredPython, "dependency-health-check-failed");
                        }

                        File.WriteAllText(markerPath, markerKey);
                        SaveHealthCache(healthPath, markerKey, venvPython, true);
                    }
                    else
                    {
                        SaveHealthCache(healthPath, markerKey, venvPython, true);
                    }
                }

                PublishProgress(6, 7, "语音环境就绪，初始化监听", VoiceListenerState.Loading);
                PublishProgress(7, 7, "语音监听可用", VoiceListenerState.Ready);
                return FunAsrRuntimeBootstrapResult.Ready(venvPython, "ok");
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR runtime bootstrap failed.", ex);
                string fallback = ReadString("FunAsrPythonExe", "python");
                VoiceListenerStatusCenter.Publish(VoiceListenerState.Unavailable, "语音环境准备失败");
                return FunAsrRuntimeBootstrapResult.NotReady(fallback, ex.Message);
            }
        }

        private static async Task<string> ResolvePythonExecutableAsync(string configured, CancellationToken cancellationToken)
        {
            string[] preferred = new[] { configured, "python", "py" }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string command in preferred)
            {
                string resolved = await TryResolvePythonFromCommandAsync(command, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                {
                    return resolved;
                }
            }

            return string.Empty;
        }

        private static async Task<string> TryResolvePythonFromCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command))
                return string.Empty;

            if (Path.IsPathRooted(command) && File.Exists(command))
                return command;

            string args = command.Equals("py", StringComparison.OrdinalIgnoreCase)
                ? "-3 -c \"import sys;print(sys.executable)\""
                : "-c \"import sys;print(sys.executable)\"";

            var result = await RunProcessAsync(command, args, 20, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                return string.Empty;

            string line = (result.Stdout ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .LastOrDefault();
            return line ?? string.Empty;
        }

        private static async Task<PythonVersionInfo> GetPythonVersionAsync(string pythonExe, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pythonExe))
                return null;

            var result = await RunProcessAsync(
                pythonExe,
                "-c \"import sys;print(f'{sys.executable}|{sys.version_info[0]}|{sys.version_info[1]}')\"",
                20,
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                return null;

            string line = (result.Stdout ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .LastOrDefault();
            if (string.IsNullOrWhiteSpace(line))
                return null;

            string[] parts = line.Split('|');
            if (parts.Length < 3)
                return null;

            if (!int.TryParse(parts[1], out int major))
                return null;
            if (!int.TryParse(parts[2], out int minor))
                return null;

            return new PythonVersionInfo
            {
                Executable = parts[0],
                Major = major,
                Minor = minor
            };
        }

        private static async Task<string> TryProvisionCompatiblePythonAsync(string runtimeRoot, CancellationToken cancellationToken)
        {
            if (!ReadBool("FunAsrAutoProvisionCondaPython", true))
            {
                return string.Empty;
            }

            string condaExe = ResolveCondaExecutable();
            if (string.IsNullOrWhiteSpace(condaExe) || !File.Exists(condaExe))
            {
                VoiceRuntimeLog.Info("FunASR bootstrap: conda not found, skip compatible-python provisioning.");
                return string.Empty;
            }

            string pyVersion = ReadString("FunAsrCondaPythonVersion", "3.11");
            string envPath = Path.Combine(runtimeRoot, "conda-py311");
            string envPython = Path.Combine(envPath, "python.exe");

            if (File.Exists(envPython))
            {
                return envPython;
            }

            VoiceListenerStatusCenter.Publish(VoiceListenerState.Installing, $"正在准备 Python {pyVersion} 运行环境");
            VoiceRuntimeLog.Info($"FunASR bootstrap: creating conda env at {envPath} (python={pyVersion})");

            int timeoutSeconds = Math.Max(120, ReadInt("FunAsrBootstrapTimeoutSeconds", 900));
            var createEnv = await RunProcessAsync(
                condaExe,
                $"create -y -p \"{envPath}\" python={pyVersion}",
                timeoutSeconds,
                cancellationToken).ConfigureAwait(false);

            if (createEnv.ExitCode != 0 || !File.Exists(envPython))
            {
                VoiceRuntimeLog.Info($"FunASR bootstrap: conda env create failed. detail={BuildProcessFailureSummary(createEnv)}");
                return string.Empty;
            }

            return envPython;
        }

        private static string ResolveCondaExecutable()
        {
            string configured = ReadString("FunAsrCondaExe", string.Empty);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }

            string[] candidates = new[]
            {
                @"D:\tools\miniconda3\Scripts\conda.exe",
                @"C:\ProgramData\miniconda3\Scripts\conda.exe",
                @"C:\ProgramData\Anaconda3\Scripts\conda.exe"
            };

            string found = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(found))
            {
                return found;
            }

            return string.Empty;
        }

        private static bool IsSupportedPythonVersion(PythonVersionInfo version, int maxSupportedMinor)
        {
            if (version == null)
                return false;

            if (version.Major != 3)
                return false;

            return version.Minor <= maxSupportedMinor;
        }

        private static async Task<bool> VerifyFunAsrDependenciesAsync(string pythonExe, int timeoutSeconds, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pythonExe) || !File.Exists(pythonExe))
                return false;

            var probe = await RunProcessAsync(
                pythonExe,
                "-m pip show funasr modelscope torch torchaudio editdistance",
                timeoutSeconds,
                cancellationToken).ConfigureAwait(false);

            if (probe.ExitCode != 0)
            {
                VoiceRuntimeLog.Info($"FunASR bootstrap: dependency probe failed. detail={BuildProcessFailureSummary(probe)}");
                return false;
            }

            string output = (probe.Stdout ?? string.Empty).Trim();
            return output.IndexOf("Name: funasr", StringComparison.OrdinalIgnoreCase) >= 0
                && output.IndexOf("Name: modelscope", StringComparison.OrdinalIgnoreCase) >= 0
                && output.IndexOf("Name: torch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void PublishProgress(int step, int total, string detail, VoiceListenerState state)
        {
            int percent = Math.Max(0, Math.Min(100, (int)Math.Round(step * 100.0 / Math.Max(1, total))));
            VoiceListenerStatusCenter.Publish(state, $"语音准备进度 {percent}%（{step}/{total}）：{detail}");
        }

        private static bool IsHealthCacheFresh(string healthPath, string markerKey, string pythonExe, int maxAgeHours)
        {
            try
            {
                if (!File.Exists(healthPath))
                    return false;

                var map = ReadHealthMap(healthPath);
                bool ok = string.Equals(GetHealthMapValue(map, "ok"), "true", StringComparison.OrdinalIgnoreCase);
                string cachedMarker = GetHealthMapValue(map, "markerKey");
                string cachedPython = GetHealthMapValue(map, "pythonExe");
                string checkedUtcRaw = GetHealthMapValue(map, "checkedAtUtc");
                if (!ok)
                    return false;
                if (!string.Equals(cachedMarker, markerKey, StringComparison.Ordinal))
                    return false;
                if (!string.Equals(cachedPython, pythonExe, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!DateTime.TryParse(checkedUtcRaw, out DateTime checkedUtc))
                    return false;

                return (DateTime.UtcNow - checkedUtc.ToUniversalTime()) < TimeSpan.FromHours(Math.Max(1, maxAgeHours));
            }
            catch
            {
                return false;
            }
        }

        private static void SaveHealthCache(string healthPath, string markerKey, string pythonExe, bool ok)
        {
            try
            {
                var lines = new[]
                {
                    $"ok={(ok ? "true" : "false")}",
                    $"markerKey={markerKey ?? string.Empty}",
                    $"pythonExe={pythonExe ?? string.Empty}",
                    $"checkedAtUtc={DateTime.UtcNow:o}"
                };
                File.WriteAllLines(healthPath, lines, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string[] ReadHealthMap(string path)
        {
            try
            {
                return File.ReadAllLines(path, Encoding.UTF8);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string GetHealthMapValue(string[] lines, string key)
        {
            if (lines == null || lines.Length == 0 || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            string prefix = key + "=";
            string line = lines.FirstOrDefault(x => x != null && x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            return line.Substring(prefix.Length).Trim();
        }

        private static bool IsEditdistanceBuildFailure(string stderr, string stdout)
        {
            string all = ((stderr ?? string.Empty) + "\n" + (stdout ?? string.Empty)).ToLowerInvariant();
            return all.Contains("building wheel for editdistance")
                || all.Contains("no matching distribution found for editdistance")
                || all.Contains("failed building wheel for editdistance");
        }

        private static async Task<bool> TryInstallEditdistanceCompatibilityAsync(string venvPython, int timeoutSeconds, CancellationToken cancellationToken)
        {
            string[] candidates = new[]
            {
                "editdistance==0.6.2",
                "editdistance==0.7.0",
                "editdistance==0.7.1"
            };

            foreach (string candidate in candidates)
            {
                var result = await RunProcessAsync(
                    venvPython,
                    $"-m pip install --disable-pip-version-check --no-input --prefer-binary --only-binary=:all: {candidate}",
                    timeoutSeconds,
                    cancellationToken).ConfigureAwait(false);

                if (result.ExitCode == 0)
                {
                    VoiceRuntimeLog.Info($"FunASR bootstrap: compatible {candidate} installed.");
                    return true;
                }
            }

            return false;
        }

        private static async Task<bool> TryInstallFunAsrNoDepsFallbackAsync(string venvPython, string baseDependencySpec, int timeoutSeconds, CancellationToken cancellationToken)
        {
            VoiceRuntimeLog.Info($"FunASR bootstrap: no-deps strategy start. baseDeps={baseDependencySpec}");
            var depsInstall = await RunProcessAsync(
                venvPython,
                $"-m pip install --disable-pip-version-check --no-input --prefer-binary {baseDependencySpec}",
                timeoutSeconds,
                cancellationToken).ConfigureAwait(false);
            if (depsInstall.ExitCode != 0)
            {
                VoiceRuntimeLog.Info($"FunASR bootstrap: no-deps fallback deps install failed. detail={BuildProcessFailureSummary(depsInstall)}");
                return false;
            }

            var funasrNoDeps = await RunProcessAsync(
                venvPython,
                "-m pip install --disable-pip-version-check --no-input --prefer-binary --no-deps funasr",
                timeoutSeconds,
                cancellationToken).ConfigureAwait(false);
            if (funasrNoDeps.ExitCode != 0)
            {
                VoiceRuntimeLog.Info($"FunASR bootstrap: no-deps funasr install failed. detail={BuildProcessFailureSummary(funasrNoDeps)}");
                return false;
            }

            var probe = await RunProcessAsync(
                venvPython,
                "-c \"import funasr,modelscope,torch;print('ok')\"",
                Math.Max(30, timeoutSeconds / 3),
                cancellationToken).ConfigureAwait(false);
            if (probe.ExitCode != 0)
            {
                VoiceRuntimeLog.Info($"FunASR bootstrap: no-deps fallback probe failed. detail={BuildProcessFailureSummary(probe)}");
                return false;
            }

            VoiceRuntimeLog.Info("FunASR bootstrap: no-deps strategy completed successfully.");
            return (probe.Stdout ?? string.Empty).IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsInstallFailureCoolingDown(string failCachePath, string markerKey, string pythonExe, int cooldownMinutes, out string reason, out int remainingSeconds)
        {
            reason = string.Empty;
            remainingSeconds = 0;
            try
            {
                if (!File.Exists(failCachePath))
                    return false;

                var lines = File.ReadAllLines(failCachePath, Encoding.UTF8);
                string cachedMarker = GetHealthMapValue(lines, "markerKey");
                string cachedPython = GetHealthMapValue(lines, "pythonExe");
                string tsRaw = GetHealthMapValue(lines, "failedAtUtc");
                string detail = GetHealthMapValue(lines, "detail");

                if (!string.Equals(cachedMarker, markerKey, StringComparison.Ordinal))
                    return false;
                if (!string.Equals(cachedPython, pythonExe, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!DateTime.TryParse(tsRaw, out DateTime failedAtUtc))
                    return false;

                TimeSpan age = DateTime.UtcNow - failedAtUtc.ToUniversalTime();
                if (age >= TimeSpan.FromMinutes(Math.Max(1, cooldownMinutes)))
                    return false;

                remainingSeconds = Math.Max(1, (int)Math.Ceiling(TimeSpan.FromMinutes(Math.Max(1, cooldownMinutes)).Subtract(age).TotalSeconds));
                reason = string.IsNullOrWhiteSpace(detail) ? "上次安装失败，稍后重试" : detail;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SaveInstallFailureCache(string failCachePath, string markerKey, string pythonExe, string detail)
        {
            try
            {
                string dir = Path.GetDirectoryName(failCachePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var lines = new[]
                {
                    $"markerKey={markerKey ?? string.Empty}",
                    $"pythonExe={pythonExe ?? string.Empty}",
                    $"failedAtUtc={DateTime.UtcNow:o}",
                    $"detail={(detail ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')}"
                };
                File.WriteAllLines(failCachePath, lines, Encoding.UTF8);
                VoiceRuntimeLog.Info($"FunASR bootstrap: failure cache written: {failCachePath}");
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error($"FunASR bootstrap: failure cache write failed: {failCachePath}", ex);
            }
        }

        private static void ClearInstallFailureCache(string failCachePath)
        {
            try
            {
                if (File.Exists(failCachePath))
                {
                    File.Delete(failCachePath);
                    VoiceRuntimeLog.Info($"FunASR bootstrap: failure cache cleared: {failCachePath}");
                }
            }
            catch
            {
            }
        }

        private static string BuildProcessFailureSummary((int ExitCode, string Stdout, string Stderr) result)
        {
            string err = (result.Stderr ?? string.Empty).Trim();
            string output = (result.Stdout ?? string.Empty).Trim();
            if (output.Length > 180)
            {
                output = output.Substring(0, 180) + "...";
            }
            if (err.Length > 180)
            {
                err = err.Substring(0, 180) + "...";
            }

            return $"code={result.ExitCode}, stderr={err}, stdout={output}";
        }

        private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
            string fileName,
            string arguments,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            DateTime started = DateTime.UtcNow;
            VoiceRuntimeLog.Info($"RunProcess start: file={fileName}, args={CompactArgs(arguments)}, timeoutSec={timeoutSeconds}");
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = psi })
            {
                var stdoutBuilder = new StringBuilder();
                var stderrBuilder = new StringBuilder();
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (stdoutBuilder) { stdoutBuilder.AppendLine(e.Data); }
                    MaybeLogPipProgress(fileName, arguments, e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (stderrBuilder) { stderrBuilder.AppendLine(e.Data); }
                    MaybeLogPipProgress(fileName, arguments, e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                int timeoutMs = Math.Max(5, timeoutSeconds) * 1000;

                bool exited = await Task.Run(() => process.WaitForExit(timeoutMs), cancellationToken).ConfigureAwait(false);
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    string timeoutStdout;
                    string timeoutStderr;
                    lock (stdoutBuilder) { timeoutStdout = stdoutBuilder.ToString(); }
                    lock (stderrBuilder) { timeoutStderr = stderrBuilder.ToString(); }
                    VoiceRuntimeLog.Info($"RunProcess timeout: file={fileName}, args={CompactArgs(arguments)}, elapsedSec={(DateTime.UtcNow - started).TotalSeconds:F1}");
                    return (-1, timeoutStdout, string.IsNullOrWhiteSpace(timeoutStderr) ? "timeout" : timeoutStderr);
                }

                process.WaitForExit();
                string stdout;
                string stderr;
                lock (stdoutBuilder) { stdout = stdoutBuilder.ToString(); }
                lock (stderrBuilder) { stderr = stderrBuilder.ToString(); }
                VoiceRuntimeLog.Info($"RunProcess end: file={fileName}, code={process.ExitCode}, elapsedSec={(DateTime.UtcNow - started).TotalSeconds:F1}, args={CompactArgs(arguments)}");
                return (process.ExitCode, stdout, stderr);
            }
        }

        private static string CompactArgs(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
                return string.Empty;

            string compact = args.Replace("\r", " ").Replace("\n", " ").Trim();
            if (compact.Length > 220)
            {
                compact = compact.Substring(0, 220) + "...";
            }
            return compact;
        }

        private static void MaybeLogPipProgress(string fileName, string arguments, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            bool isPip = (arguments ?? string.Empty).IndexOf("-m pip", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isPip)
                return;

            string lower = line.ToLowerInvariant();
            bool interesting = lower.Contains("collecting ")
                || lower.Contains("downloading ")
                || lower.Contains("building wheel")
                || lower.Contains("installing collected packages")
                || lower.Contains("successfully installed")
                || lower.Contains("error:")
                || lower.Contains("failed");
            if (!interesting)
                return;

            lock (LogSync)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastPipProgressLogUtc) < TimeSpan.FromSeconds(2) && lower.Contains("downloading "))
                    return;

                _lastPipProgressLogUtc = now;
            }

            VoiceRuntimeLog.Info($"pip-progress: {line.Trim()}");
        }

        private static FunAsrRuntimeBootstrapResult TryUsePrebuiltRuntime(string runtimeRoot, string configuredBundlePath)
        {
            try
            {
                string[] bundleCandidates = BuildBundleCandidates(configuredBundlePath, runtimeRoot);
                string bundle = bundleCandidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrWhiteSpace(bundle))
                {
                    return FunAsrRuntimeBootstrapResult.NotReady(string.Empty, "bundle-not-found");
                }

                string prebuiltRoot = Path.Combine(runtimeRoot, "prebuilt");
                string marker = Path.Combine(prebuiltRoot, ".bundle.marker");
                string currentSignature = $"{bundle}|{new FileInfo(bundle).Length}|{File.GetLastWriteTimeUtc(bundle):o}";
                string existingSignature = File.Exists(marker) ? File.ReadAllText(marker, Encoding.UTF8).Trim() : string.Empty;

                if (!Directory.Exists(prebuiltRoot) || !string.Equals(currentSignature, existingSignature, StringComparison.Ordinal))
                {
                    if (Directory.Exists(prebuiltRoot))
                    {
                        try { Directory.Delete(prebuiltRoot, true); } catch { }
                    }
                    Directory.CreateDirectory(prebuiltRoot);
                    ZipFile.ExtractToDirectory(bundle, prebuiltRoot);
                    File.WriteAllText(marker, currentSignature, Encoding.UTF8);
                    VoiceRuntimeLog.Info($"FunASR prebuilt runtime extracted: bundle={bundle}, target={prebuiltRoot}");
                }

                string pythonExe = FindPythonInPrebuilt(prebuiltRoot);
                if (string.IsNullOrWhiteSpace(pythonExe) || !File.Exists(pythonExe))
                {
                    return FunAsrRuntimeBootstrapResult.NotReady(string.Empty, "prebuilt-python-not-found");
                }

                return FunAsrRuntimeBootstrapResult.Ready(pythonExe, $"prebuilt:{bundle}");
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("FunASR prebuilt runtime prepare failed.", ex);
                return FunAsrRuntimeBootstrapResult.NotReady(string.Empty, "prebuilt-prepare-failed");
            }
        }

        private static string[] BuildBundleCandidates(string configuredBundlePath, string runtimeRoot)
        {
            var list = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(configuredBundlePath))
            {
                if (Path.IsPathRooted(configuredBundlePath))
                {
                    list.Add(configuredBundlePath);
                }
                else
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    list.Add(Path.Combine(baseDir, configuredBundlePath));
                    // Visual Studio 常见启动目录：bin\Debug 或 bin\Release，补充回溯到项目根目录候选。
                    list.Add(Path.GetFullPath(Path.Combine(baseDir, "..", configuredBundlePath)));
                    list.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "..", configuredBundlePath)));
                    list.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", configuredBundlePath)));
                    list.Add(Path.Combine(Environment.CurrentDirectory, configuredBundlePath));
                    list.Add(Path.Combine(runtimeRoot, configuredBundlePath));
                }
            }
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string FindPythonInPrebuilt(string prebuiltRoot)
        {
            string[] candidates = new[]
            {
                Path.Combine(prebuiltRoot, "python.exe"),
                Path.Combine(prebuiltRoot, "Scripts", "python.exe"),
                Path.Combine(prebuiltRoot, "venv", "Scripts", "python.exe")
            };

            string found = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(found))
                return found;

            try
            {
                return Directory.EnumerateFiles(prebuiltRoot, "python.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ReadString(string key, string fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            }
            catch
            {
                return fallback;
            }
        }

        private static bool ReadBool(string key, bool fallback)
        {
            string value = ReadString(key, fallback.ToString());
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }

        private static int ReadInt(string key, int fallback)
        {
            string value = ReadString(key, fallback.ToString());
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }
    }
}
