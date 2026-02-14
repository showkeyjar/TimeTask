using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TimeTask
{
    public class SuggestionEventRecord
    {
        public string ActionId { get; set; }
        public string EventType { get; set; } // shown/accepted/deferred/rejected
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class UserProfileMetrics
    {
        public int WindowDays { get; set; }
        public int SuggestionsShown { get; set; }
        public int SuggestionsAccepted { get; set; }
        public int SuggestionsDeferred { get; set; }
        public int SuggestionsRejected { get; set; }
        public double HitRate { get; set; }
        public double InterruptionIndex { get; set; }
        public string TopEffectiveActionId { get; set; }
    }

    public class AdaptiveNudgeRecommendation
    {
        public int RecommendedStuckThresholdMinutes { get; set; }
        public int RecommendedDailyNudgeLimit { get; set; }
    }

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
        public Dictionary<string, int> SuggestionAcceptedHistogram { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> SuggestionDeferredHistogram { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> SuggestionRejectedHistogram { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<SuggestionEventRecord> SuggestionEvents { get; set; } = new List<SuggestionEventRecord>();
    }

    public class UserProfileManager
    {
        private readonly object _sync = new object();
        private readonly string _profilePath;
        private UserProfileSnapshot _profile;
        private static readonly char[] KeywordSeparators = new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '，', '。', '；', '：', '、', '-', '_', '/', '\\', '|', '(', ')', '[', ']', '{', '}' };

        public class StuckActionSuggestion
        {
            public string Id { get; set; }
            public string Text { get; set; }
            public int Score { get; set; }
        }

        public void RecordSuggestionShown(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            lock (_sync)
            {
                AddSuggestionEvent(actionId.Trim(), "shown");
                _profile.LastUpdatedAt = DateTime.Now;
                Save();
            }
        }

        public void RecordSuggestionFeedback(string actionId, string feedbackType)
        {
            if (string.IsNullOrWhiteSpace(actionId) || string.IsNullOrWhiteSpace(feedbackType))
            {
                return;
            }

            lock (_sync)
            {
                string id = actionId.Trim();
                string type = feedbackType.Trim().ToLowerInvariant();

                if (type == "accepted")
                {
                    Increment(_profile.SuggestionAcceptedHistogram, id);
                    AddSuggestionEvent(id, "accepted");
                }
                else if (type == "deferred")
                {
                    Increment(_profile.SuggestionDeferredHistogram, id);
                    AddSuggestionEvent(id, "deferred");
                }
                else if (type == "rejected")
                {
                    Increment(_profile.SuggestionRejectedHistogram, id);
                    AddSuggestionEvent(id, "rejected");
                }

                _profile.LastUpdatedAt = DateTime.Now;
                Save();
            }
        }

        public UserProfileMetrics GetDashboardMetrics(int windowDays = 7)
        {
            lock (_sync)
            {
                DateTime cutoff = DateTime.Now.AddDays(-Math.Max(1, windowDays));
                var events = (_profile.SuggestionEvents ?? new List<SuggestionEventRecord>())
                    .Where(e => e != null && e.CreatedAt >= cutoff)
                    .ToList();

                int shown = events.Count(e => string.Equals(e.EventType, "shown", StringComparison.OrdinalIgnoreCase));
                int accepted = events.Count(e => string.Equals(e.EventType, "accepted", StringComparison.OrdinalIgnoreCase));
                int deferred = events.Count(e => string.Equals(e.EventType, "deferred", StringComparison.OrdinalIgnoreCase));
                int rejected = events.Count(e => string.Equals(e.EventType, "rejected", StringComparison.OrdinalIgnoreCase));

                double hitRate = shown > 0 ? (double)accepted / shown : 0.0;
                double interruptionIndex = shown > 0 ? (double)(deferred + rejected) / shown : 0.0;

                string topAction = events
                    .Where(e => string.Equals(e.EventType, "accepted", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.ActionId))
                    .GroupBy(e => e.ActionId)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();

                return new UserProfileMetrics
                {
                    WindowDays = Math.Max(1, windowDays),
                    SuggestionsShown = shown,
                    SuggestionsAccepted = accepted,
                    SuggestionsDeferred = deferred,
                    SuggestionsRejected = rejected,
                    HitRate = hitRate,
                    InterruptionIndex = interruptionIndex,
                    TopEffectiveActionId = topAction ?? "N/A"
                };
            }
        }

        public AdaptiveNudgeRecommendation GetAdaptiveNudgeRecommendation(int windowDays = 7)
        {
            var metrics = GetDashboardMetrics(windowDays);
            int thresholdMinutes = 90;
            int dailyLimit = 2;

            if (metrics.SuggestionsShown < 5)
            {
                thresholdMinutes = 90;
                dailyLimit = 2;
            }
            else if (metrics.InterruptionIndex >= 0.65)
            {
                thresholdMinutes = 120;
                dailyLimit = 1;
            }
            else if (metrics.InterruptionIndex >= 0.50)
            {
                thresholdMinutes = 105;
                dailyLimit = 1;
            }
            else if (metrics.HitRate >= 0.45 && metrics.InterruptionIndex <= 0.35)
            {
                thresholdMinutes = 75;
                dailyLimit = 3;
            }
            else if (metrics.HitRate >= 0.30 && metrics.InterruptionIndex <= 0.40)
            {
                thresholdMinutes = 80;
                dailyLimit = 2;
            }

            thresholdMinutes = Math.Max(60, Math.Min(180, thresholdMinutes));
            dailyLimit = Math.Max(1, Math.Min(3, dailyLimit));

            return new AdaptiveNudgeRecommendation
            {
                RecommendedStuckThresholdMinutes = thresholdMinutes,
                RecommendedDailyNudgeLimit = dailyLimit
            };
        }

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

        public List<StuckActionSuggestion> GetRankedStuckSuggestions(ItemGrid task, TimeSpan noProgressDuration, string lastSuggestedActionId = null)
        {
            lock (_sync)
            {
                int hours = Math.Max(1, (int)Math.Round(noProgressDuration.TotalHours));
                bool highImportance = string.Equals(task?.Importance, "High", StringComparison.OrdinalIgnoreCase);
                bool highUrgency = string.Equals(task?.Urgency, "High", StringComparison.OrdinalIgnoreCase);

                bool avoidanceHeavy = _profile.ReminderSnoozedCount + _profile.ReminderDismissedCount >
                                     _profile.ReminderCompletedCount + _profile.ReminderUpdatedCount;

                var suggestions = new List<StuckActionSuggestion>
                {
                    new StuckActionSuggestion { Id = "start_10_min", Text = $"已卡住约 {hours} 小时，先做 10 分钟最小动作，完成后再扩展。", Score = 0 },
                    new StuckActionSuggestion { Id = "split_20_min", Text = $"已卡住约 {hours} 小时，拆成一个 20 分钟子任务并安排到今天。", Score = 0 },
                    new StuckActionSuggestion { Id = "delegate_or_drop", Text = $"已卡住约 {hours} 小时，建议先确认是否委托或降低优先级。", Score = 0 },
                    new StuckActionSuggestion { Id = "pause_and_switch", Text = $"已卡住约 {hours} 小时，建议先暂停此任务，转到更关键事项。", Score = 0 },
                    new StuckActionSuggestion { Id = "decision_now", Text = $"已卡住约 {hours} 小时，只做一个决定：继续、延期或放弃。", Score = 0 }
                };

                foreach (var s in suggestions)
                {
                    if (highImportance && highUrgency)
                    {
                        if (s.Id == "start_10_min") s.Score += 4;
                        if (s.Id == "split_20_min") s.Score += 2;
                    }
                    else if (highImportance && !highUrgency)
                    {
                        if (s.Id == "split_20_min") s.Score += 4;
                        if (s.Id == "decision_now") s.Score += 2;
                    }
                    else if (!highImportance && highUrgency)
                    {
                        if (s.Id == "delegate_or_drop") s.Score += 4;
                        if (s.Id == "decision_now") s.Score += 2;
                    }
                    else
                    {
                        if (s.Id == "pause_and_switch") s.Score += 4;
                        if (s.Id == "decision_now") s.Score += 2;
                    }

                    if (avoidanceHeavy)
                    {
                        if (s.Id == "start_10_min") s.Score += 2;
                        if (s.Id == "decision_now") s.Score += 2;
                        if (s.Id == "split_20_min") s.Score -= 1;
                    }
                    else
                    {
                        if (s.Id == "split_20_min") s.Score += 2;
                    }

                    if (!string.IsNullOrWhiteSpace(lastSuggestedActionId) &&
                        string.Equals(lastSuggestedActionId, s.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        s.Score -= 5;
                    }

                    int accepted = GetHistogramValue(_profile.SuggestionAcceptedHistogram, s.Id);
                    int deferred = GetHistogramValue(_profile.SuggestionDeferredHistogram, s.Id);
                    int rejected = GetHistogramValue(_profile.SuggestionRejectedHistogram, s.Id);
                    s.Score += (accepted * 2) - deferred - (rejected * 2);
                }

                return suggestions
                    .OrderByDescending(s => s.Score)
                    .ThenBy(s => s.Id, StringComparer.Ordinal)
                    .ToList();
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
                        loaded.SuggestionAcceptedHistogram ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        loaded.SuggestionDeferredHistogram ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        loaded.SuggestionRejectedHistogram ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        loaded.SuggestionEvents ??= new List<SuggestionEventRecord>();
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

        private static int GetHistogramValue(Dictionary<string, int> histogram, string key)
        {
            if (histogram == null || string.IsNullOrWhiteSpace(key))
            {
                return 0;
            }

            return histogram.TryGetValue(key, out int value) ? value : 0;
        }

        private void AddSuggestionEvent(string actionId, string eventType)
        {
            if (string.IsNullOrWhiteSpace(actionId) || string.IsNullOrWhiteSpace(eventType))
            {
                return;
            }

            _profile.SuggestionEvents ??= new List<SuggestionEventRecord>();
            _profile.SuggestionEvents.Add(new SuggestionEventRecord
            {
                ActionId = actionId,
                EventType = eventType,
                CreatedAt = DateTime.Now
            });

            DateTime cutoff = DateTime.Now.AddDays(-30);
            var filtered = _profile.SuggestionEvents
                .Where(e => e.CreatedAt >= cutoff)
                .ToList();
            if (filtered.Count > 500)
            {
                filtered = filtered.Skip(filtered.Count - 500).ToList();
            }
            _profile.SuggestionEvents = filtered;
        }
    }
}
