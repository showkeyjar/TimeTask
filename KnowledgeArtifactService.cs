using System;
using System.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TimeTask
{
    public sealed class KnowledgeArtifactService
    {
        private readonly string _appRootPath;
        private readonly string _obsidianVaultPath;
        private readonly string _localArtifactsPath;
        private readonly string _obsidianInboxNote;
        private readonly bool _enabled;
        private readonly bool _writebackEnabled;
        private readonly bool _writebackToSourceNote;

        private KnowledgeArtifactService(
            string appRootPath,
            string obsidianVaultPath,
            string localArtifactsPath,
            string obsidianInboxNote,
            bool enabled,
            bool writebackEnabled,
            bool writebackToSourceNote)
        {
            _appRootPath = appRootPath;
            _obsidianVaultPath = obsidianVaultPath;
            _localArtifactsPath = localArtifactsPath;
            _obsidianInboxNote = obsidianInboxNote;
            _enabled = enabled;
            _writebackEnabled = writebackEnabled;
            _writebackToSourceNote = writebackToSourceNote;
        }

        public bool IsEnabled => _enabled;

        public static KnowledgeArtifactService CreateFromAppSettings(string appRootPath)
        {
            string ReadRaw(string key, string defaultValue = "")
            {
                try
                {
                    string value = ConfigurationManager.AppSettings[key];
                    return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
                }
                catch
                {
                    return defaultValue;
                }
            }

            bool ReadBool(string key, bool defaultValue)
            {
                string raw = ReadRaw(key, defaultValue.ToString());
                return bool.TryParse(raw, out bool value) ? value : defaultValue;
            }

            string ResolvePath(string configuredPath, string fallbackRelativePath)
            {
                string path = string.IsNullOrWhiteSpace(configuredPath) ? fallbackRelativePath : configuredPath;
                if (Path.IsPathRooted(path))
                {
                    return path;
                }
                return Path.Combine(appRootPath, path);
            }

            bool enabled = ReadBool("KnowledgeCaptureEnabled", true);
            string obsidianVaultPath = ReadRaw("ObsidianVaultPath", string.Empty);
            string localArtifactsPath = ResolvePath(ReadRaw("KnowledgeArtifactsPath"), @"data\knowledge");
            string obsidianInboxNote = ReadRaw("KnowledgeObsidianInboxNote", "_TimeTask/Knowledge Inbox.md");
            bool writebackEnabled = ReadBool("KnowledgeWritebackEnabled", true);
            bool writebackToSource = ReadBool("KnowledgeWritebackToSourceNote", true);

            return new KnowledgeArtifactService(
                appRootPath,
                obsidianVaultPath,
                localArtifactsPath,
                obsidianInboxNote.Replace('\\', '/'),
                enabled,
                writebackEnabled,
                writebackToSource);
        }

        public void CaptureCompletion(ItemGrid task)
        {
            if (!_enabled || task == null || string.IsNullOrWhiteSpace(task.Task))
            {
                return;
            }

            string artifactId = BuildArtifactId(task);
            string markdown = BuildArtifactMarkdown(task, artifactId);
            SaveLocalArtifact(markdown);

            if (_writebackEnabled)
            {
                TryWritebackToObsidian(task, artifactId, markdown);
            }
        }

        private static string BuildArtifactId(ItemGrid task)
        {
            string raw = $"{task.Task}|{task.CompletionTime?.ToUniversalTime().Ticks ?? DateTime.UtcNow.Ticks}|{task.SourceTaskID}";
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 12).ToLowerInvariant();
            }
        }

        private static string BuildArtifactMarkdown(ItemGrid task, string artifactId)
        {
            DateTime completedAt = task.CompletionTime ?? DateTime.Now;
            string source = string.IsNullOrWhiteSpace(task.SourceTaskID) ? "manual" : task.SourceTaskID;
            string quadrant = $"{task.Importance}/{task.Urgency}";
            var sb = new StringBuilder();
            sb.AppendLine($"## Task Recap - {task.Task}");
            sb.AppendLine();
            sb.AppendLine($"- artifact_id: {artifactId}");
            sb.AppendLine($"- completed_at: {completedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- source: {source}");
            sb.AppendLine($"- quadrant: {quadrant}");
            sb.AppendLine();
            sb.AppendLine("### Summary");
            sb.AppendLine("- Outcome: Completed");
            sb.AppendLine("- Notes: Add key learnings or pitfalls here.");
            sb.AppendLine();
            return sb.ToString();
        }

        private void SaveLocalArtifact(string markdown)
        {
            string monthFile = $"{DateTime.Now:yyyy-MM}.md";
            Directory.CreateDirectory(_localArtifactsPath);
            string path = Path.Combine(_localArtifactsPath, monthFile);
            File.AppendAllText(path, markdown + Environment.NewLine, Encoding.UTF8);
        }

        private void TryWritebackToObsidian(ItemGrid task, string artifactId, string markdown)
        {
            if (string.IsNullOrWhiteSpace(_obsidianVaultPath) || !Directory.Exists(_obsidianVaultPath))
            {
                return;
            }

            string marker = $"<!-- TIMETASK:ARTIFACT:{artifactId} -->";
            string content = marker + Environment.NewLine + markdown;
            string completedLine = BuildCompletedTaskLine(task);

            string inboxPath = Path.Combine(_obsidianVaultPath, _obsidianInboxNote.Replace('/', Path.DirectorySeparatorChar));
            AppendIfNotExists(inboxPath, marker, content);
            UpsertManagedTaskLine(inboxPath, completedLine);

            if (_writebackToSourceNote && TryResolveSourceNotePath(task.SourceTaskID, out string sourceNoteRelativePath))
            {
                string sourcePath = Path.Combine(_obsidianVaultPath, sourceNoteRelativePath.Replace('/', Path.DirectorySeparatorChar));
                AppendIfNotExists(sourcePath, marker, content);
                UpsertManagedTaskLine(sourcePath, completedLine);
            }
        }

        private static string BuildCompletedTaskLine(ItemGrid task)
        {
            DateTime completedAt = task.CompletionTime ?? DateTime.Now;
            return $"- [x] {task.Task} (completed: {completedAt:yyyy-MM-dd})";
        }

        private static bool TryResolveSourceNotePath(string sourceTaskId, out string relativePath)
        {
            relativePath = null;
            if (string.IsNullOrWhiteSpace(sourceTaskId))
            {
                return false;
            }

            const string prefix = "obsidian:";
            if (!sourceTaskId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            relativePath = sourceTaskId.Substring(prefix.Length).Trim();
            return !string.IsNullOrWhiteSpace(relativePath);
        }

        private static void AppendIfNotExists(string filePath, string marker, string content)
        {
            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string existing = File.Exists(filePath) ? File.ReadAllText(filePath, Encoding.UTF8) : string.Empty;
                if (existing.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }

                string block = Environment.NewLine + content + Environment.NewLine;
                File.AppendAllText(filePath, block, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void UpsertManagedTaskLine(string filePath, string line)
        {
            const string begin = "<!-- TIMETASK:BEGIN -->";
            const string end = "<!-- TIMETASK:END -->";

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string existing = File.Exists(filePath) ? File.ReadAllText(filePath, Encoding.UTF8) : string.Empty;
                int beginIdx = existing.IndexOf(begin, StringComparison.Ordinal);
                int endIdx = existing.IndexOf(end, StringComparison.Ordinal);

                if (beginIdx >= 0 && endIdx > beginIdx)
                {
                    int contentStart = beginIdx + begin.Length;
                    string block = existing.Substring(contentStart, endIdx - contentStart);
                    if (block.IndexOf(line, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return;
                    }

                    string updatedBlock = block.TrimEnd() + Environment.NewLine + line + Environment.NewLine;
                    string updated = existing.Substring(0, contentStart) + Environment.NewLine + updatedBlock + existing.Substring(endIdx);
                    File.WriteAllText(filePath, updated, Encoding.UTF8);
                    return;
                }

                string managedBlock = Environment.NewLine + begin + Environment.NewLine + line + Environment.NewLine + end + Environment.NewLine;
                File.AppendAllText(filePath, managedBlock, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
