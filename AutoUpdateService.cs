using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TimeTask
{
    internal sealed class AutoUpdateService
    {
        private const string PlaceholderManifestUrl = "YOUR_UPDATE_MANIFEST_URL";
        private const string PlaceholderGithubOwner = "YOUR_GITHUB_OWNER";
        private const string PlaceholderGithubRepo = "YOUR_GITHUB_REPO";
        private readonly Dispatcher _dispatcher;

        public AutoUpdateService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void StartBackgroundCheck()
        {
            _ = Task.Run(CheckForUpdatesAsync);
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                if (!GetBool("AutoUpdateEnabled", true))
                {
                    return;
                }

                UpdatePackageInfo updateInfo;
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(GetInt("AutoUpdateCheckTimeoutSeconds", 10));
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("TimeTask-Updater/1.0");
                    updateInfo = await TryResolveUpdatePackageAsync(http).ConfigureAwait(false);
                }

                if (updateInfo == null)
                {
                    VoiceRuntimeLog.Info("Auto update skipped: no valid update source configured.");
                    return;
                }

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
                if (updateInfo.Version <= currentVersion)
                {
                    VoiceRuntimeLog.Info($"Auto update: no update. Current={currentVersion}, Latest={updateInfo.Version}");
                    return;
                }

                bool shouldInstall = await _dispatcher.InvokeAsync(() =>
                {
                    var result = MessageBox.Show(
                        $"检测到新版本：{updateInfo.Version}（当前：{currentVersion}）。\n\n是否立即下载并在重启后完成更新？",
                        "发现新版本",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                    return result == MessageBoxResult.Yes;
                });

                if (!shouldInstall)
                {
                    return;
                }

                await DownloadAndApplyAsync(updateInfo).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Auto update check failed.", ex);
            }
        }

        private async Task<UpdatePackageInfo> TryResolveUpdatePackageAsync(HttpClient http)
        {
            UpdatePackageInfo fromGithub = await TryGetFromGithubReleasesAsync(http).ConfigureAwait(false);
            if (fromGithub != null)
            {
                return fromGithub;
            }

            return await TryGetFromManifestAsync(http).ConfigureAwait(false);
        }

        private async Task<UpdatePackageInfo> TryGetFromGithubReleasesAsync(HttpClient http)
        {
            string owner = (ConfigurationManager.AppSettings["AutoUpdateGithubOwner"] ?? string.Empty).Trim();
            string repo = (ConfigurationManager.AppSettings["AutoUpdateGithubRepo"] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return null;
            }

            if (string.Equals(owner, PlaceholderGithubOwner, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(repo, PlaceholderGithubRepo, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            bool includePrerelease = GetBool("AutoUpdateGithubIncludePrerelease", false);
            string apiUrl = includePrerelease
                ? $"https://api.github.com/repos/{owner}/{repo}/releases"
                : $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            string json = await http.GetStringAsync(apiUrl).ConfigureAwait(false);

            GithubRelease release = includePrerelease
                ? SelectReleaseFromArray(json)
                : JsonSerializer.Deserialize<GithubRelease>(json, JsonOptions);

            if (release == null || release.Draft)
            {
                return null;
            }

            string assetHint = (ConfigurationManager.AppSettings["AutoUpdateGithubAssetNameContains"] ?? string.Empty).Trim();
            GithubAsset asset = SelectAsset(release, assetHint);
            if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            {
                VoiceRuntimeLog.Info("Auto update: GitHub release found but no matching zip asset.");
                return null;
            }

            Version version = ParseVersion(release.TagName);
            if (version == null)
            {
                version = ParseVersion(release.Name);
            }

            if (version == null)
            {
                throw new InvalidOperationException("无法从 GitHub Release 的 tag/name 解析版本号。");
            }

            return new UpdatePackageInfo
            {
                Version = version,
                DownloadUrl = asset.BrowserDownloadUrl,
                Sha256 = null
            };
        }

        private async Task<UpdatePackageInfo> TryGetFromManifestAsync(HttpClient http)
        {
            string manifestUrl = (ConfigurationManager.AppSettings["AutoUpdateManifestUrl"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(manifestUrl) ||
                string.Equals(manifestUrl, PlaceholderManifestUrl, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string manifestJson = await http.GetStringAsync(manifestUrl).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, JsonOptions);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                return null;
            }

            Version version = ParseVersion(manifest.Version);
            if (version == null)
            {
                throw new InvalidOperationException($"无法解析版本号：{manifest.Version}");
            }

            return new UpdatePackageInfo
            {
                Version = version,
                DownloadUrl = manifest.DownloadUrl,
                Sha256 = manifest.Sha256
            };
        }

        private static GithubRelease SelectReleaseFromArray(string json)
        {
            var releases = JsonSerializer.Deserialize<GithubRelease[]>(json, JsonOptions);
            if (releases == null || releases.Length == 0)
            {
                return null;
            }

            return releases.FirstOrDefault(r => r != null && !r.Draft);
        }

        private static GithubAsset SelectAsset(GithubRelease release, string assetHint)
        {
            if (release?.Assets == null || release.Assets.Length == 0)
            {
                return null;
            }

            var zipAssets = release.Assets
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Name) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (zipAssets.Length == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(assetHint))
            {
                GithubAsset hinted = zipAssets.FirstOrDefault(a => a.Name.IndexOf(assetHint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hinted != null)
                {
                    return hinted;
                }
            }

            return zipAssets[0];
        }

        private async Task DownloadAndApplyAsync(UpdatePackageInfo info)
        {
            try
            {
                string appBaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string currentExePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? Path.Combine(appBaseDir, "TimeTask.exe");

                string updateRoot = Path.Combine(Path.GetTempPath(), "TimeTask", "updates", info.Version.ToString());
                string zipPath = Path.Combine(updateRoot, "update.zip");
                string extractPath = Path.Combine(updateRoot, "payload");
                string batPath = Path.Combine(updateRoot, "apply_update.bat");

                Directory.CreateDirectory(updateRoot);
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(GetInt("AutoUpdateDownloadTimeoutSeconds", 300));
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("TimeTask-Updater/1.0");
                    using (var response = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await source.CopyToAsync(target).ConfigureAwait(false);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(info.Sha256))
                {
                    string actual = ComputeSha256(zipPath);
                    if (!string.Equals(actual, info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("更新包校验失败（SHA256 不匹配）。");
                    }
                }

                ZipFile.ExtractToDirectory(zipPath, extractPath);
                string payloadRoot = ResolvePayloadRoot(extractPath);

                WriteUpdaterScript(batPath, payloadRoot, appBaseDir, currentExePath);

                await _dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        "更新包已下载完成。点击“确定”后将关闭应用并自动完成更新，然后重新启动。",
                        "准备更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                await _dispatcher.InvokeAsync(() => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Auto update apply failed.", ex);
                await _dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        $"自动更新失败：{ex.Message}\n请稍后重试或手动更新。",
                        "更新失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
            }
        }

        private static void WriteUpdaterScript(string batPath, string sourceDir, string appDir, string exePath)
        {
            string escapedSource = EscapeForBatch(sourceDir);
            string escapedApp = EscapeForBatch(appDir);
            string escapedExe = EscapeForBatch(exePath);

            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal");
            script.AppendLine("set \"PROCESS_NAME=TimeTask.exe\"");
            script.AppendLine($"set \"SOURCE_DIR={escapedSource}\"");
            script.AppendLine($"set \"APP_DIR={escapedApp}\"");
            script.AppendLine($"set \"TARGET_EXE={escapedExe}\"");
            script.AppendLine();
            script.AppendLine(":wait_loop");
            script.AppendLine("tasklist /FI \"IMAGENAME eq %PROCESS_NAME%\" | find /I \"%PROCESS_NAME%\" >nul");
            script.AppendLine("if not errorlevel 1 (");
            script.AppendLine("  timeout /t 1 /nobreak >nul");
            script.AppendLine("  goto wait_loop");
            script.AppendLine(")");
            script.AppendLine();
            script.AppendLine("robocopy \"%SOURCE_DIR%\" \"%APP_DIR%\" /E /R:2 /W:1 /NFL /NDL /NP >nul");
            script.AppendLine("if %ERRORLEVEL% GEQ 8 goto copy_failed");
            script.AppendLine();
            script.AppendLine("start \"\" \"%TARGET_EXE%\"");
            script.AppendLine("exit /b 0");
            script.AppendLine();
            script.AppendLine(":copy_failed");
            script.AppendLine("echo Update failed. Please run as administrator and try again.");
            script.AppendLine("pause");
            script.AppendLine("exit /b 1");

            File.WriteAllText(batPath, script.ToString(), Encoding.ASCII);
        }

        private static string ResolvePayloadRoot(string extractPath)
        {
            string rootExe = Path.Combine(extractPath, "TimeTask.exe");
            if (File.Exists(rootExe))
            {
                return extractPath;
            }

            var subDirs = Directory.GetDirectories(extractPath);
            if (subDirs.Length == 1)
            {
                string candidateExe = Path.Combine(subDirs[0], "TimeTask.exe");
                if (File.Exists(candidateExe))
                {
                    return subDirs[0];
                }
            }

            throw new InvalidOperationException("更新包中未找到 TimeTask.exe。");
        }

        private static string EscapeForBatch(string value)
        {
            return (value ?? string.Empty).Replace("\"", "\"\"");
        }

        private static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        private static Version ParseVersion(string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText))
            {
                return null;
            }

            string normalized = versionText.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }

            Version version;
            return Version.TryParse(normalized, out version) ? version : null;
        }

        private static bool GetBool(string key, bool defaultValue)
        {
            string raw = ConfigurationManager.AppSettings[key];
            bool value;
            return bool.TryParse(raw, out value) ? value : defaultValue;
        }

        private static int GetInt(string key, int defaultValue)
        {
            string raw = ConfigurationManager.AppSettings[key];
            int value;
            return int.TryParse(raw, out value) && value > 0 ? value : defaultValue;
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private sealed class UpdatePackageInfo
        {
            public Version Version { get; set; }
            public string DownloadUrl { get; set; }
            public string Sha256 { get; set; }
        }

        private sealed class UpdateManifest
        {
            public string Version { get; set; }
            public string DownloadUrl { get; set; }
            public string Sha256 { get; set; }
        }

        private sealed class GithubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("draft")]
            public bool Draft { get; set; }

            [JsonPropertyName("assets")]
            public GithubAsset[] Assets { get; set; }
        }

        private sealed class GithubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }
    }
}
