using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TimeTask
{
    public class LifeProfileSnapshot
    {
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public string DecisionStyle { get; set; } = "balanced";
        public string RecommendedNudgeTone { get; set; } = "gentle";
        public List<string> Strengths { get; set; } = new List<string>();
        public List<string> RiskTriggers { get; set; } = new List<string>();
        public List<string> TopFocusTopics { get; set; } = new List<string>();
        public List<int> PeakHours { get; set; } = new List<int>();
        public double ExecutionReliability { get; set; } = 0.5;
        public double InterruptionSensitivity { get; set; } = 0.5;
        public int ActiveTaskCount { get; set; }
        public int StuckTaskCount { get; set; }
        public int GoalLinkedTaskCount { get; set; }
    }

    public class LifeProfileEngine
    {
        private readonly string _profilePath;
        private readonly object _sync = new object();

        public LifeProfileEngine(string dataPath)
        {
            string strategyPath = Path.Combine(dataPath, "strategy");
            Directory.CreateDirectory(strategyPath);
            _profilePath = Path.Combine(strategyPath, "user_life_profile.json");
        }

        public LifeProfileSnapshot BuildAndPersist(UserProfileSnapshot behaviorSnapshot, List<ItemGrid> tasks, DateTime now)
        {
            lock (_sync)
            {
                var snapshot = BuildSnapshot(behaviorSnapshot, tasks, now);
                Persist(snapshot);
                return snapshot;
            }
        }

        public LifeProfileSnapshot LoadOrDefault()
        {
            lock (_sync)
            {
                try
                {
                    if (File.Exists(_profilePath))
                    {
                        string json = File.ReadAllText(_profilePath);
                        var loaded = JsonSerializer.Deserialize<LifeProfileSnapshot>(json);
                        if (loaded != null)
                        {
                            loaded.Strengths ??= new List<string>();
                            loaded.RiskTriggers ??= new List<string>();
                            loaded.TopFocusTopics ??= new List<string>();
                            loaded.PeakHours ??= new List<int>();
                            return loaded;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LifeProfileEngine load failed: {ex.Message}");
                }

                return new LifeProfileSnapshot();
            }
        }

        private static LifeProfileSnapshot BuildSnapshot(UserProfileSnapshot behaviorSnapshot, List<ItemGrid> tasks, DateTime now)
        {
            behaviorSnapshot ??= new UserProfileSnapshot();
            tasks ??= new List<ItemGrid>();

            var activeTasks = tasks.Where(t => t != null && t.IsActive).ToList();
            int totalReminderResponses = Math.Max(1,
                behaviorSnapshot.ReminderCompletedCount +
                behaviorSnapshot.ReminderUpdatedCount +
                behaviorSnapshot.ReminderSnoozedCount +
                behaviorSnapshot.ReminderDismissedCount);

            double executionReliability = Math.Min(1.0,
                (behaviorSnapshot.ReminderCompletedCount + behaviorSnapshot.ReminderUpdatedCount) / (double)totalReminderResponses);
            double interruptionSensitivity = Math.Min(1.0,
                (behaviorSnapshot.ReminderSnoozedCount + behaviorSnapshot.ReminderDismissedCount) / (double)totalReminderResponses);

            var peakHours = (behaviorSnapshot.ActiveHourHistogram ?? new Dictionary<int, int>())
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(3)
                .Select(kv => kv.Key)
                .ToList();

            var topTopics = (behaviorSnapshot.TaskKeywordHistogram ?? new Dictionary<string, int>())
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(kv => kv.Key)
                .ToList();

            int highHighCount = activeTasks.Count(t =>
                string.Equals(t.Importance, "High", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Urgency, "High", StringComparison.OrdinalIgnoreCase));

            int stuckTaskCount = activeTasks.Count(t => t.LastProgressDate < now.AddDays(-3));
            int goalLinkedCount = activeTasks.Count(t => !string.IsNullOrWhiteSpace(t.LongTermGoalId));

            var strengths = new List<string>();
            if (executionReliability >= 0.65) strengths.Add("consistent_executor");
            if (goalLinkedCount >= Math.Max(2, activeTasks.Count / 3)) strengths.Add("goal_oriented");
            if (highHighCount <= Math.Max(1, activeTasks.Count / 4)) strengths.Add("prioritization_control");
            if (peakHours.Any()) strengths.Add("predictable_energy_rhythm");

            var risks = new List<string>();
            if (interruptionSensitivity >= 0.55) risks.Add("high_interruption_cost");
            if (stuckTaskCount >= Math.Max(2, activeTasks.Count / 3)) risks.Add("stuck_backlog");
            if (highHighCount >= Math.Max(3, activeTasks.Count / 2)) risks.Add("urgency_overload");

            string style = "balanced";
            if (executionReliability >= 0.7 && interruptionSensitivity <= 0.3)
            {
                style = "decisive";
            }
            else if (interruptionSensitivity >= 0.6)
            {
                style = "conservative";
            }

            string tone = interruptionSensitivity >= 0.55 ? "very_gentle" : "direct_friendly";

            return new LifeProfileSnapshot
            {
                GeneratedAt = now,
                DecisionStyle = style,
                RecommendedNudgeTone = tone,
                Strengths = strengths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                RiskTriggers = risks.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                TopFocusTopics = topTopics,
                PeakHours = peakHours,
                ExecutionReliability = executionReliability,
                InterruptionSensitivity = interruptionSensitivity,
                ActiveTaskCount = activeTasks.Count,
                StuckTaskCount = stuckTaskCount,
                GoalLinkedTaskCount = goalLinkedCount
            };
        }

        private void Persist(LifeProfileSnapshot snapshot)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_profilePath, JsonSerializer.Serialize(snapshot, options));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LifeProfileEngine persist failed: {ex.Message}");
            }
        }
    }
}
