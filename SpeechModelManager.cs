using System;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TimeTask
{
    /// <summary>
    /// 语音模型管理：
    /// - 自动下载/解压模型压缩包
    /// - 通过 SHA256 做可选完整性校验
    /// - 启动时做轻量预热，减少首次访问延迟
    /// </summary>
    public sealed class SpeechModelManager
    {
        private static readonly HttpClient SharedClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private readonly SemaphoreSlim _bootstrapLock = new SemaphoreSlim(1, 1);
        private Task<SpeechModelBootstrapResult> _bootstrapTask;

        public string ModelRootPath { get; }
        public string ModelName { get; }
        public bool AutoDownloadEnabled { get; }

        public SpeechModelManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            ModelRootPath = Path.Combine(appData, "TimeTask", "speech-models");
            ModelName = ReadAppSetting("SpeechModelName", "vosk-model-cn-0.22");
            AutoDownloadEnabled = ReadBoolAppSetting("SpeechModelAutoDownload", true);
        }

        public Task<SpeechModelBootstrapResult> EnsureReadyAsync(CancellationToken cancellationToken = default)
        {
            lock (this)
            {
                if (_bootstrapTask == null)
                {
                    _bootstrapTask = EnsureReadyInternalAsync(cancellationToken);
                }

                return _bootstrapTask;
            }
        }

        private async Task<SpeechModelBootstrapResult> EnsureReadyInternalAsync(CancellationToken cancellationToken)
        {
            await _bootstrapLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                VoiceRuntimeLog.Info($"Speech model bootstrap started. root={ModelRootPath}, model={ModelName}, autoDownload={AutoDownloadEnabled}");
                Directory.CreateDirectory(ModelRootPath);

                string modelDir = GetModelDirectory();
                if (IsModelReady(modelDir))
                {
                    VoiceRuntimeLog.Info($"Speech model already exists: {modelDir}");
                    await WarmupModelAsync(modelDir, cancellationToken).ConfigureAwait(false);
                    return SpeechModelBootstrapResult.Ready(modelDir, "local-cache");
                }

                if (!AutoDownloadEnabled)
                {
                    VoiceRuntimeLog.Info("Speech model missing and auto download disabled.");
                    return SpeechModelBootstrapResult.NotReady("模型不存在，且自动下载已关闭。");
                }

                string modelUrl = ReadAppSetting("SpeechModelUrl", GetDefaultModelUrl(ModelName));
                if (string.IsNullOrWhiteSpace(modelUrl))
                {
                    VoiceRuntimeLog.Info("SpeechModelUrl is empty.");
                    return SpeechModelBootstrapResult.NotReady("未配置 SpeechModelUrl，无法自动下载模型。");
                }

                string expectedSha256 = ReadAppSetting("SpeechModelSha256", string.Empty);
                VoiceRuntimeLog.Info($"Downloading speech model from {modelUrl}");
                string zipPath = await DownloadModelZipAsync(modelUrl, cancellationToken).ConfigureAwait(false);
                VoiceRuntimeLog.Info($"Speech model downloaded: {zipPath}");

                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    ValidateSha256(zipPath, expectedSha256);
                    VoiceRuntimeLog.Info("Speech model SHA256 validated.");
                }

                string extractedPath = ExtractModelZip(zipPath);
                VoiceRuntimeLog.Info($"Speech model extracted: {extractedPath}");
                EnsureDefaultPhrases(extractedPath);
                await WarmupModelAsync(extractedPath, cancellationToken).ConfigureAwait(false);
                VoiceRuntimeLog.Info("Speech model warmup completed.");
                return SpeechModelBootstrapResult.Ready(extractedPath, "downloaded");
            }
            catch (OperationCanceledException)
            {
                VoiceRuntimeLog.Info("Speech model bootstrap canceled.");
                throw;
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Speech model bootstrap failed.", ex);
                return SpeechModelBootstrapResult.NotReady($"模型准备失败: {ex.Message}");
            }
            finally
            {
                _bootstrapLock.Release();
            }
        }

        public string GetModelDirectory()
        {
            return Path.Combine(ModelRootPath, ModelName);
        }

        public string GetHintsFilePath()
        {
            return Path.Combine(GetModelDirectory(), "phrases.txt");
        }

        private async Task<string> DownloadModelZipAsync(string modelUrl, CancellationToken cancellationToken)
        {
            string fileName = $"{ModelName}.zip";
            string zipPath = Path.Combine(ModelRootPath, fileName);

            using (var response = await SharedClient.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                long? contentLength = response.Content.Headers.ContentLength;
                string tmpPath = zipPath + ".tmp";

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int lastPercent = -1;

                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        if (read <= 0) break;
                        await fs.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);

                        totalRead += read;
                        if (contentLength.HasValue && contentLength.Value > 0)
                        {
                            int percent = (int)(totalRead * 100 / contentLength.Value);
                            if (percent >= lastPercent + 5)
                            {
                                lastPercent = percent;
                                VoiceRuntimeLog.Info($"Speech model download progress: {percent}% ({FormatBytes(totalRead)}/{FormatBytes(contentLength.Value)})");
                            }
                        }
                    }
                }

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                File.Move(tmpPath, zipPath);
            }

            return zipPath;
        }

        private string ExtractModelZip(string zipPath)
        {
            string targetDir = GetModelDirectory();
            string tempDir = $"{targetDir}.tmp_{Guid.NewGuid():N}";

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            ZipFile.ExtractToDirectory(zipPath, tempDir);

            string extractedRoot = FindExtractedRoot(tempDir);
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }

            if (!string.Equals(extractedRoot, tempDir, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(extractedRoot, targetDir);
                Directory.Delete(tempDir, true);
            }
            else
            {
                Directory.Move(tempDir, targetDir);
            }

            return targetDir;
        }

        private static string FindExtractedRoot(string dir)
        {
            var directChildren = Directory.GetDirectories(dir);
            if (directChildren.Length == 1 &&
                Directory.GetFiles(dir).Length == 0 &&
                Directory.GetDirectories(directChildren[0]).Length > 0)
            {
                return directChildren[0];
            }

            return dir;
        }

        private static void ValidateSha256(string filePath, string expectedSha256)
        {
            string normalized = expectedSha256.Trim().ToLowerInvariant();

            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha.ComputeHash(stream);
                string actual = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                if (!string.Equals(actual, normalized, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("模型文件 SHA256 校验失败。");
                }
            }
        }

        private static bool IsModelReady(string modelDir)
        {
            if (!Directory.Exists(modelDir))
            {
                return false;
            }

            // Vosk 模型最关键文件
            string voskMarker = Path.Combine(modelDir, "am", "final.mdl");
            if (File.Exists(voskMarker))
            {
                return true;
            }

            bool hasAnyFiles = Directory.EnumerateFiles(modelDir, "*", SearchOption.AllDirectories).Any();
            return hasAnyFiles;
        }

        private void EnsureDefaultPhrases(string modelDir)
        {
            try
            {
                string hintsPath = GetHintsFilePath();
                if (File.Exists(hintsPath))
                {
                    MergeUserLexicon(hintsPath);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(hintsPath));
                var lines = new[]
                {
                    "请提醒我明天与市场部门的会议",
                    "提醒我明天的会议",
                    "明天与市场部门的会议",
                    "会议",
                    "市场部门",
                    "明天",
                    "提醒我"
                };
                File.WriteAllLines(hintsPath, lines);
                VoiceRuntimeLog.Info($"Default phrases.txt created: {hintsPath}");
                MergeUserLexicon(hintsPath);
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Failed to create default phrases.txt.", ex);
            }
        }

        private void MergeUserLexicon(string hintsPath)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string lexiconPath = Path.Combine(appData, "TimeTask", "voice_lexicon.txt");
                if (!File.Exists(lexiconPath))
                    return;

                var userPhrases = File.ReadAllLines(lexiconPath)
                    .Select(l => l?.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                if (userPhrases.Count == 0)
                    return;

                var existing = File.Exists(hintsPath)
                    ? File.ReadAllLines(hintsPath).Select(l => l?.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList()
                    : new List<string>();

                var merged = existing
                    .Concat(userPhrases)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(500)
                    .ToList();

                File.WriteAllLines(hintsPath, merged);
                VoiceRuntimeLog.Info($"Merged user lexicon into phrases.txt, count={merged.Count}");
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Failed to merge user lexicon into phrases.txt.", ex);
            }
        }

        private static async Task WarmupModelAsync(string modelDir, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(modelDir))
            {
                return;
            }

            var files = Directory.EnumerateFiles(modelDir, "*", SearchOption.AllDirectories)
                .OrderBy(f => new FileInfo(f).Length)
                .Take(16)
                .ToArray();

            var buffer = new byte[8192];
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await fs.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static string ReadAppSetting(string key, string fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool ReadBoolAppSetting(string key, bool fallback)
        {
            var value = ReadAppSetting(key, fallback.ToString());
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }

        private static string GetDefaultModelUrl(string modelName)
        {
            if (string.Equals(modelName, "vosk-model-small-cn-0.22", StringComparison.OrdinalIgnoreCase))
            {
                return "https://alphacephei.com/vosk/models/vosk-model-small-cn-0.22.zip";
            }

            if (string.Equals(modelName, "vosk-model-cn-0.22", StringComparison.OrdinalIgnoreCase))
            {
                return "https://alphacephei.com/vosk/models/vosk-model-cn-0.22.zip";
            }

            if (string.Equals(modelName, "vosk-model-small-en-us-0.15", StringComparison.OrdinalIgnoreCase))
            {
                return "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip";
            }

            return string.Empty;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F1} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F1} MB";
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }
    }

    public sealed class SpeechModelBootstrapResult
    {
        public bool IsReady { get; private set; }
        public string ModelDirectory { get; private set; }
        public string Source { get; private set; }
        public string Message { get; private set; }

        public static SpeechModelBootstrapResult Ready(string modelDirectory, string source)
        {
            return new SpeechModelBootstrapResult
            {
                IsReady = true,
                ModelDirectory = modelDirectory,
                Source = source,
                Message = "OK"
            };
        }

        public static SpeechModelBootstrapResult NotReady(string message)
        {
            return new SpeechModelBootstrapResult
            {
                IsReady = false,
                ModelDirectory = string.Empty,
                Source = "none",
                Message = message ?? "Unknown"
            };
        }
    }
}
