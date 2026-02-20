using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace TimeTask
{
    public sealed class KnowledgeSyncOptions
    {
        public bool Enabled { get; set; } = false;
        public int SyncIntervalMinutes { get; set; } = 60;
        public string ObsidianVaultPath { get; set; } = string.Empty;
        public bool ObsidianVaultAutoDiscovered { get; set; } = false;
        public bool ObsidianIncludeSubfolders { get; set; } = true;
        public int ObsidianMaxFilesPerSync { get; set; } = 200;
        public bool RealtimeWatchEnabled { get; set; } = true;
        public int SyncDebounceSeconds { get; set; } = 8;
        public bool AutoImportEnabled { get; set; } = true;
        public double AutoImportMinConfidence { get; set; } = 0.9;
        public int AutoImportMaxPerRun { get; set; } = 6;
        public bool SmartNotifyEnabled { get; set; } = true;
        public string RulesFilePath { get; set; } = string.Empty;
        public string PromptTemplatePath { get; set; } = string.Empty;
        public string StateFilePath { get; set; } = string.Empty;
    }

    public sealed class KnowledgeSyncService
    {
        private readonly KnowledgeSyncOptions _options;
        private readonly ObsidianKnowledgeConnector _connector;
        private readonly KnowledgeTaskExtractor _extractor;
        private readonly string _promptTemplate;

        private KnowledgeSyncService(
            KnowledgeSyncOptions options,
            ObsidianKnowledgeConnector connector,
            KnowledgeTaskExtractor extractor,
            string promptTemplate)
        {
            _options = options;
            _connector = connector;
            _extractor = extractor;
            _promptTemplate = promptTemplate ?? string.Empty;
        }

        public KnowledgeSyncOptions Options => _options;
        public bool IsEnabled => _options != null && _options.Enabled;
        public bool IsConnectorReady => _connector != null && _connector.IsConfigured();

        public static KnowledgeSyncService CreateFromAppSettings(string appRootPath)
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TimeTask");
            Directory.CreateDirectory(appDataPath);

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

            int ReadInt(string key, int defaultValue, int min, int max)
            {
                string raw = ReadRaw(key, defaultValue.ToString());
                if (!int.TryParse(raw, out int value))
                {
                    return defaultValue;
                }
                return Math.Max(min, Math.Min(max, value));
            }

            double ReadDouble(string key, double defaultValue, double min, double max)
            {
                string raw = ReadRaw(key, defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!double.TryParse(raw, out double value) &&
                    !double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    return defaultValue;
                }
                return Math.Max(min, Math.Min(max, value));
            }

            string Resolve(string configuredPath, string fallbackRelativePath)
            {
                string path = configuredPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = fallbackRelativePath;
                }
                if (Path.IsPathRooted(path))
                {
                    return path;
                }
                return Path.Combine(appRootPath, path);
            }

            var options = new KnowledgeSyncOptions
            {
                Enabled = ReadBool("KnowledgeSyncEnabled", false),
                SyncIntervalMinutes = ReadInt("KnowledgeSyncIntervalMinutes", 60, 5, 720),
                ObsidianVaultPath = ReadRaw("ObsidianVaultPath", string.Empty),
                ObsidianIncludeSubfolders = ReadBool("ObsidianIncludeSubfolders", true),
                ObsidianMaxFilesPerSync = ReadInt("ObsidianMaxFilesPerSync", 200, 10, 5000),
                RealtimeWatchEnabled = ReadBool("KnowledgeRealtimeWatchEnabled", true),
                SyncDebounceSeconds = ReadInt("KnowledgeSyncDebounceSeconds", 8, 2, 120),
                AutoImportEnabled = ReadBool("KnowledgeAutoImportEnabled", true),
                AutoImportMinConfidence = ReadDouble("KnowledgeAutoImportMinConfidence", 0.9, 0.5, 1.0),
                AutoImportMaxPerRun = ReadInt("KnowledgeAutoImportMaxPerRun", 6, 1, 50),
                SmartNotifyEnabled = ReadBool("KnowledgeSmartNotifyEnabled", true),
                RulesFilePath = Resolve(ReadRaw("KnowledgeRulesPath"), @"configs\knowledge_rules.yaml"),
                PromptTemplatePath = Resolve(ReadRaw("KnowledgePromptTemplatePath"), @"configs\task_extract_prompt.txt"),
                StateFilePath = Resolve(ReadRaw("KnowledgeSyncStatePath"), Path.Combine(appDataPath, "knowledge_sync_state.json"))
            };

            if (string.IsNullOrWhiteSpace(options.ObsidianVaultPath))
            {
                options.ObsidianVaultPath = TryDiscoverVaultPath();
                options.ObsidianVaultAutoDiscovered = !string.IsNullOrWhiteSpace(options.ObsidianVaultPath);
            }

            var rules = KnowledgeRuleSet.Load(options.RulesFilePath);
            var extractor = new KnowledgeTaskExtractor(rules);
            var connector = new ObsidianKnowledgeConnector(
                options.ObsidianVaultPath,
                options.ObsidianIncludeSubfolders,
                options.ObsidianMaxFilesPerSync);

            string promptTemplate = string.Empty;
            if (File.Exists(options.PromptTemplatePath))
            {
                promptTemplate = File.ReadAllText(options.PromptTemplatePath);
            }

            return new KnowledgeSyncService(options, connector, extractor, promptTemplate);
        }

        public KnowledgeSyncResult RunOnce(TaskDraftManager draftManager)
        {
            var result = new KnowledgeSyncResult();
            if (!IsEnabled)
            {
                result.Errors.Add("Knowledge sync is disabled.");
                return result;
            }

            if (draftManager == null)
            {
                result.Errors.Add("TaskDraftManager is not available.");
                return result;
            }

            if (!IsConnectorReady)
            {
                result.Errors.Add("Obsidian vault path is not configured or does not exist.");
                return result;
            }

            var state = LoadState();
            var changedNotes = _connector.GetChangedNotes(state.NoteSignatures, out Dictionary<string, string> latestSignatures);
            result.NotesScanned = changedNotes.Count;

            var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in changedNotes)
            {
                try
                {
                    var candidates = _extractor.Extract(note);
                    result.TaskCandidates += candidates.Count;

                    foreach (var candidate in candidates)
                    {
                        string dedupeKey = $"{candidate.SourcePath}|{candidate.Title}";
                        if (!importedKeys.Add(dedupeKey))
                        {
                            result.DuplicatesSkipped++;
                            continue;
                        }

                        var draft = BuildDraft(candidate);
                        draftManager.AddDraft(draft);
                        result.NewDrafts.Add(draft);
                        result.DraftsAdded++;
                    }
                }
                catch (Exception ex)
                {
                    result.FailedNotes++;
                    result.Errors.Add($"{note.RelativePath}: {ex.Message}");
                }
            }

            state.LastRunUtc = DateTime.UtcNow;
            state.NoteSignatures = latestSignatures ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SaveState(state);

            return result;
        }

        private static TaskDraft BuildDraft(ExtractedTaskCandidate candidate)
        {
            string reminderHint = candidate.DueAt.HasValue ? $"截止 {candidate.DueAt.Value:yyyy-MM-dd}" : null;
            return new TaskDraft
            {
                RawText = $"{candidate.Title} [{candidate.SourcePath}]",
                CleanedText = candidate.Title,
                SourceNotePath = candidate.SourcePath,
                Confidence = candidate.Confidence,
                ReminderTime = candidate.DueAt,
                ReminderHintText = reminderHint,
                EstimatedQuadrant = MapQuadrant(candidate.Priority),
                Importance = candidate.Priority == "low" ? "Low" : "High",
                Urgency = candidate.Priority == "high" ? "High" : "Low",
                Source = "obsidian",
                IsProcessed = false,
                LastDetected = DateTime.Now,
                CreatedAt = DateTime.Now,
                DetectionCount = 1
            };
        }

        private static string TryDiscoverVaultPath()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string configPath = Path.Combine(appData, "obsidian", "obsidian.json");
                if (!File.Exists(configPath))
                {
                    return string.Empty;
                }

                string json = File.ReadAllText(configPath);
                var openMatch = Regex.Match(
                    json,
                    "\"path\"\\s*:\\s*\"(?<path>[^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)\"\\s*,\\s*\"ts\"\\s*:\\s*\\d+\\s*,\\s*\"open\"\\s*:\\s*true",
                    RegexOptions.IgnoreCase);

                if (openMatch.Success)
                {
                    string decoded = DecodeJsonString(openMatch.Groups["path"].Value);
                    if (Directory.Exists(decoded))
                    {
                        return decoded;
                    }
                }

                var firstPathMatch = Regex.Match(json, "\"path\"\\s*:\\s*\"(?<path>[^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)\"", RegexOptions.IgnoreCase);
                if (firstPathMatch.Success)
                {
                    string decoded = DecodeJsonString(firstPathMatch.Groups["path"].Value);
                    if (Directory.Exists(decoded))
                    {
                        return decoded;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string DecodeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value
                .Replace("\\\\", "\\")
                .Replace("\\/", "/")
                .Replace("\\\"", "\"");
        }

        private static string MapQuadrant(string priority)
        {
            if (string.Equals(priority, "high", StringComparison.OrdinalIgnoreCase))
            {
                return "important_urgent";
            }
            if (string.Equals(priority, "low", StringComparison.OrdinalIgnoreCase))
            {
                return "not_important_not_urgent";
            }
            return "important_not_urgent";
        }

        private KnowledgeSyncState LoadState()
        {
            try
            {
                if (!File.Exists(_options.StateFilePath))
                {
                    return new KnowledgeSyncState();
                }

                string json = File.ReadAllText(_options.StateFilePath);
                var state = JsonSerializer.Deserialize<KnowledgeSyncState>(json);
                return state ?? new KnowledgeSyncState();
            }
            catch
            {
                return new KnowledgeSyncState();
            }
        }

        private void SaveState(KnowledgeSyncState state)
        {
            try
            {
                string dir = Path.GetDirectoryName(_options.StateFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_options.StateFilePath, json);
            }
            catch
            {
            }
        }
    }
}
