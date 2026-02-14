using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TimeTask
{
    public class UserProfileSnapshot
    {
        public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
        public int TotalReminderShown { get; set; } = 0;
        public int ReminderCompletedCount { get; set; } = 0;
        public int ReminderUpdatedCount { get; set; } = 0;
        public int ReminderSnoozedCount { get; set; } = 0;
        public int ReminderDismissedCount { get; set; } = 0;
        public int QuadrantMoveCount { get; set; } = 0;
        public Dictionary<int, int> ActiveHourHistogram { get; set; } = new Dictionary<int, int>();
        public Dictionary<string, int> TaskKeywordHistogram { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ProgressSourceHistogram { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    public class UserProfileManager
    {
        private readonly object _sync = new object();
        private readonly string _profilePath;
        private UserProfileSnapshot _profile;
        private static readonly char[] KeywordSeparators = new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '，', '。', '；', '：', '、', '-', '_', '/', '\\', '|', '(', ')', '[', ']', '{', '}' };

        public UserProfileManager()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeTask");
            Directory.CreateDirectory(appDataPath);
            _profilePath = Path.Combine(appDataPath, "user-profile.json");
            _profile = LoadOrCreate();
        }

        public void RecordReminderShown(ItemGrid task)
        {
            lock (_sync)
            {
                _profile.TotalReminderShown++;
                TouchActivity(task);
                AddTaskKeywords(task?.Task);
                _profile.LastUpdatedAt = DateTime.Now;
                Save();
            }
        }

        public void RecordReminderResult(ItemGrid task, TaskReminderResult result)
        {
            lock (_sync)
            {
                switch (result)
                {
                    case TaskReminderResult.Completed:
                        _profile.ReminderCompletedCount++;
                        break;
                    case TaskReminderResult.Updated:
                        _profile.ReminderUpdatedCount++;
                        break;
                    case TaskReminderResult.Snoozed:
                        _profile.ReminderSnoozedCount++;
                        break;
                    case TaskReminderResult.Dismissed:
                    default:
                        _profile.ReminderDismissedCount++;
                        break;
                }

                TouchActivity(task);
                AddTaskKeywords(task?.Task);
                _profile.LastUpdatedAt = DateTime.Now;
                Save();
            }
        }

        public void RecordTaskProgress(ItemGrid task, string source)
        {
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(source))
                {
                    Increment(_profile.ProgressSourceHistogram, source.Trim());
                }
                TouchActivity(task);
                AddTaskKeywords(task?.Task);
                _profile.LastUpdatedAt = DateTime.Now;
                Save();
            }
        }

        public void RecordQuadrantMove(ItemGrid task, string fromQuadrant, string toQuadrant)
        {
            lock (_sync)
            {
                if (!string.Equals(fromQuadrant, toQuadrant, StringComparison.OrdinalIgnoreCase))
                {
                    _profile.QuadrantMoveCount++;
                }
                TouchActivity(task);
                AddTaskKeywords(task?.Task);
                _profile.LastUpdatedAt = DateTime.Now;
                Save();
            }
        }

        public string BuildReminderContext(ItemGrid task, TimeSpan inactiveDuration)
        {
            lock (_sync)
            {
                var sb = new StringBuilder();
                sb.AppendLine("User profile hints:");
                sb.AppendLine($"- Inactive duration: {Math.Max(1, (int)Math.Round(inactiveDuration.TotalHours))} hours");
                sb.AppendLine($"- Reminder outcomes: done={_profile.ReminderCompletedCount}, updated={_profile.ReminderUpdatedCount}, snoozed={_profile.ReminderSnoozedCount}, dismissed={_profile.ReminderDismissedCount}");

                string tone = GetPreferredTone();
                sb.AppendLine($"- Preferred tone: {tone}");

                int focusHour = GetMostActiveHour();
                if (focusHour >= 0)
                {
                    sb.AppendLine($"- Typical active hour: {focusHour:D2}:00");
                }

                string topKeywords = string.Join(", ", _profile.TaskKeywordHistogram
                    .OrderByDescending(kv => kv.Value)
                    .Take(5)
                    .Select(kv => kv.Key));
                if (!string.IsNullOrWhiteSpace(topKeywords))
                {
                    sb.AppendLine($"- Frequent task topics: {topKeywords}");
                }

                if (task != null)
                {
                    sb.AppendLine("- Current task quadrant:");
                    sb.AppendLine($"  importance={task.Importance ?? "Unknown"}, urgency={task.Urgency ?? "Unknown"}");
                }

                sb.AppendLine("- Keep suggestions concise and low-interruption.");
                return sb.ToString();
            }
        }

        private string GetPreferredTone()
        {
            int totalResponses = _profile.ReminderCompletedCount + _profile.ReminderUpdatedCount + _profile.ReminderSnoozedCount + _profile.ReminderDismissedCount;
            if (totalResponses < 4)
            {
                return "gentle";
            }

            if (_profile.ReminderDismissedCount + _profile.ReminderSnoozedCount > _profile.ReminderCompletedCount + _profile.ReminderUpdatedCount)
            {
                return "very gentle, ask one small next step";
            }

            return "direct but friendly";
        }

        private int GetMostActiveHour()
        {
            if (_profile.ActiveHourHistogram == null || _profile.ActiveHourHistogram.Count == 0)
            {
                return -1;
            }

            return _profile.ActiveHourHistogram
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .First().Key;
        }

        private void TouchActivity(ItemGrid task)
        {
            int hour = DateTime.Now.Hour;
            if (!_profile.ActiveHourHistogram.ContainsKey(hour))
            {
                _profile.ActiveHourHistogram[hour] = 0;
            }
            _profile.ActiveHourHistogram[hour]++;

            if (task != null)
            {
                Increment(_profile.TaskKeywordHistogram, task.Importance ?? "Unknown");
                Increment(_profile.TaskKeywordHistogram, task.Urgency ?? "Unknown");
            }
        }

        private void AddTaskKeywords(string taskText)
        {
            if (string.IsNullOrWhiteSpace(taskText))
            {
                return;
            }

            foreach (string raw in taskText.Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.Trim();
                if (token.Length < 2)
                {
                    continue;
                }
                if (token.All(char.IsDigit))
                {
                    continue;
                }
                Increment(_profile.TaskKeywordHistogram, token);
            }
        }

        private static void Increment(Dictionary<string, int> histogram, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!histogram.ContainsKey(key))
            {
                histogram[key] = 0;
            }
            histogram[key]++;
        }

        private UserProfileSnapshot LoadOrCreate()
        {
            try
            {
                if (File.Exists(_profilePath))
                {
                    string json = File.ReadAllText(_profilePath);
                    var loaded = JsonSerializer.Deserialize<UserProfileSnapshot>(json);
                    if (loaded != null)
                    {
                        loaded.ActiveHourHistogram ??= new Dictionary<int, int>();
                        loaded.TaskKeywordHistogram ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        loaded.ProgressSourceHistogram ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UserProfileManager: failed to load profile: {ex.Message}");
            }

            return new UserProfileSnapshot();
        }

        private void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_profile, options);
                File.WriteAllText(_profilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UserProfileManager: failed to save profile: {ex.Message}");
            }
        }
    }
}
