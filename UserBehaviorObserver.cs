using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;

namespace TimeTask
{
    public enum InteractionType
    {
        Voice,
        Text,
        MouseClick,
        KeyboardInput,
        TaskOperation,
        GoalOperation,
        SystemEvent
    }

    public class UserInteraction
    {
        public string InteractionId { get; set; }
        public InteractionType Type { get; set; }
        public DateTime Timestamp { get; set; }
        public string Description { get; set; }
        public string Context { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public string RelatedTaskId { get; set; }
        public string RelatedGoalId { get; set; }
    }

    public class BehaviorPattern
    {
        public string PatternId { get; set; }
        public string PatternName { get; set; }
        public string Description { get; set; }
        public double Confidence { get; set; }
        public DateTime FirstObserved { get; set; }
        public DateTime LastObserved { get; set; }
        public int ObservationCount { get; set; }
        public Dictionary<string, int> FrequencyByHour { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> FrequencyByDay { get; set; } = new Dictionary<string, int>();
    }

    public class WorkHabitInsight
    {
        public string InsightId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Recommendation { get; set; }
        public DateTime GeneratedAt { get; set; }
        public List<string> RelatedPatterns { get; set; } = new List<string>();
        public double Priority { get; set; }
        public bool IsAcknowledged { get; set; }
    }

    public class UserBehaviorObserver
    {
        private readonly string _dataPath;
        private readonly string _interactionsPath;
        private readonly string _patternsPath;
        private readonly string _insightsPath;
        private readonly object _lock = new object();
        private List<UserInteraction> _interactions;
        private List<BehaviorPattern> _patterns;
        private List<WorkHabitInsight> _insights;
        private const int MaxInteractions = 10000;
        private const int InteractionRetentionDays = 90;

        public event Action<WorkHabitInsight> NewInsightGenerated;
        public event Action<BehaviorPattern> PatternDetected;

        public UserBehaviorObserver(string appDataPath)
        {
            _dataPath = appDataPath;
            _interactionsPath = Path.Combine(appDataPath, "behavior", "interactions.json");
            _patternsPath = Path.Combine(appDataPath, "behavior", "patterns.json");
            _insightsPath = Path.Combine(appDataPath, "behavior", "insights.json");
            
            Directory.CreateDirectory(Path.GetDirectoryName(_interactionsPath));
            
            _interactions = new List<UserInteraction>();
            _patterns = new List<BehaviorPattern>();
            _insights = new List<WorkHabitInsight>();
            
            LoadData();
        }

        public void RecordInteraction(InteractionType type, string description, string context = null, 
            Dictionary<string, string> metadata = null, string relatedTaskId = null, string relatedGoalId = null)
        {
            lock (_lock)
            {
                var interaction = new UserInteraction
                {
                    InteractionId = Guid.NewGuid().ToString("N"),
                    Type = type,
                    Timestamp = DateTime.Now,
                    Description = description,
                    Context = context,
                    Metadata = metadata ?? new Dictionary<string, string>(),
                    RelatedTaskId = relatedTaskId,
                    RelatedGoalId = relatedGoalId
                };

                _interactions.Add(interaction);
                
                if (_interactions.Count > MaxInteractions)
                {
                    CleanupOldInteractions();
                }

                SaveInteractions();
                AnalyzePatterns();
            }
        }

        public void RecordVoiceInteraction(string recognizedText, float confidence, string context = null)
        {
            var metadata = new Dictionary<string, string>
            {
                { "confidence", confidence.ToString("F2") },
                { "text_length", recognizedText.Length.ToString() }
            };

            RecordInteraction(InteractionType.Voice, recognizedText, context, metadata);
        }

        public void RecordTaskOperation(string operation, string taskDescription, string taskId = null)
        {
            var metadata = new Dictionary<string, string>
            {
                { "operation", operation },
                { "task_length", taskDescription?.Length.ToString() ?? "0" }
            };

            RecordInteraction(InteractionType.TaskOperation, operation, taskDescription, metadata, taskId);
        }

        public void RecordGoalOperation(string operation, string goalDescription, string goalId = null)
        {
            var metadata = new Dictionary<string, string>
            {
                { "operation", operation }
            };

            RecordInteraction(InteractionType.GoalOperation, operation, goalDescription, metadata, null, goalId);
        }

        public void RecordSystemEvent(string eventName, string details = null)
        {
            RecordInteraction(InteractionType.SystemEvent, eventName, details);
        }

        private void AnalyzePatterns()
        {
            if (_interactions.Count < 10) return;

            AnalyzeActiveHours();
            AnalyzeTaskCreationPatterns();
            AnalyzeVoiceUsagePatterns();
            AnalyzeGoalProgressPatterns();
            AnalyzeProcrastinationPatterns();
            GenerateInsights();
        }

        private void AnalyzeActiveHours()
        {
            var recentInteractions = _interactions
                .Where(i => i.Timestamp > DateTime.Now.AddDays(-7))
                .ToList();

            if (recentInteractions.Count < 5) return;

            var hourlyActivity = recentInteractions
                .GroupBy(i => i.Timestamp.Hour)
                .Select(g => new { Hour = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            if (hourlyActivity.Any())
            {
                var peakHour = hourlyActivity.First();
                UpdateOrCreatePattern(
                    "peak_active_hour",
                    "高峰活跃时段",
                    $"用户在{peakHour.Hour}:00-{peakHour.Hour + 1}:00时段最活跃",
                    peakHour.Count,
                    hourlyActivity.ToDictionary(h => h.Hour.ToString(), h => h.Count)
                );
            }
        }

        private void AnalyzeTaskCreationPatterns()
        {
            var taskInteractions = _interactions
                .Where(i => i.Type == InteractionType.TaskOperation && 
                           i.Timestamp > DateTime.Now.AddDays(-14))
                .ToList();

            if (taskInteractions.Count < 5) return;

            var dailyTaskCount = taskInteractions
                .GroupBy(i => i.Timestamp.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToList();

            var avgTasksPerDay = dailyTaskCount.Average(g => g.Count);
            
            UpdateOrCreatePattern(
                "task_creation_frequency",
                "任务创建频率",
                $"平均每天创建{avgTasksPerDay:F1}个任务",
                dailyTaskCount.Count,
                dailyTaskCount.ToDictionary(d => d.Date.ToString("yyyy-MM-dd"), d => d.Count)
            );

            var morningTasks = taskInteractions.Count(i => i.Timestamp.Hour < 12);
            var afternoonTasks = taskInteractions.Count(i => i.Timestamp.Hour >= 12 && i.Timestamp.Hour < 18);
            var eveningTasks = taskInteractions.Count(i => i.Timestamp.Hour >= 18);

            if (morningTasks > afternoonTasks && morningTasks > eveningTasks)
            {
                UpdateOrCreatePattern(
                    "morning_planner",
                    "早晨规划者",
                    "倾向于在上午规划和创建任务",
                    morningTasks
                );
            }
            else if (eveningTasks > morningTasks && eveningTasks > afternoonTasks)
            {
                UpdateOrCreatePattern(
                    "evening_planner",
                    "晚间规划者",
                    "倾向于在晚上规划和创建任务",
                    eveningTasks
                );
            }
        }

        private void AnalyzeVoiceUsagePatterns()
        {
            var voiceInteractions = _interactions
                .Where(i => i.Type == InteractionType.Voice && 
                           i.Timestamp > DateTime.Now.AddDays(-7))
                .ToList();

            if (voiceInteractions.Count < 3) return;

            var voiceUsageRate = (double)voiceInteractions.Count / _interactions.Count(i => i.Timestamp > DateTime.Now.AddDays(-7));
            
            UpdateOrCreatePattern(
                "voice_usage_preference",
                "语音使用偏好",
                $"语音交互占比{voiceUsageRate:P1}",
                voiceInteractions.Count
            );

            var taskKeywords = voiceInteractions
                .Where(i => i.Description != null)
                .SelectMany(i => Regex.Matches(i.Description, @"[\u4e00-\u9fa5A-Za-z]{2,}").Cast<Match>())
                .Select(m => m.Value)
                .GroupBy(w => w, StringComparer.Ordinal)
                .Where(g => g.Count() >= 2)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count());

            if (taskKeywords.Any())
            {
                UpdateOrCreatePattern(
                    "voice_task_topics",
                    "语音任务主题",
                    "通过语音创建的常见任务主题",
                    taskKeywords.Values.Sum(),
                    taskKeywords
                );
            }
        }

        private void AnalyzeGoalProgressPatterns()
        {
            var goalInteractions = _interactions
                .Where(i => i.Type == InteractionType.GoalOperation && 
                           i.Timestamp > DateTime.Now.AddDays(-30))
                .ToList();

            if (goalInteractions.Count < 3) return;

            var goalReviewFrequency = goalInteractions
                .GroupBy(i => i.Timestamp.Date)
                .Count();

            var avgReviewInterval = 30.0 / Math.Max(1, goalReviewFrequency);
            
            UpdateOrCreatePattern(
                "goal_review_frequency",
                "目标回顾频率",
                $"平均每{avgReviewInterval:F1}天回顾一次目标",
                goalInteractions.Count
            );

            if (avgReviewInterval > 14)
            {
                GenerateInsight(
                    "low_goal_review_frequency",
                    "目标回顾频率较低",
                    $"你平均每{avgReviewInterval:F0}天才回顾一次目标，建议每周回顾一次以保持目标进度",
                    "设置每周固定时间回顾长期目标，可以使用提醒功能",
                    0.8
                );
            }
        }

        private void AnalyzeProcrastinationPatterns()
        {
            var taskInteractions = _interactions
                .Where(i => i.Type == InteractionType.TaskOperation && 
                           i.Metadata.ContainsKey("operation") &&
                           i.Metadata["operation"] == "update" &&
                           i.Timestamp > DateTime.Now.AddDays(-14))
                .ToList();

            if (taskInteractions.Count < 5) return;

            var delayedUpdates = taskInteractions
                .Where(i => i.Context != null && 
                           (i.Context.Contains("延期") || i.Context.Contains("推迟") || i.Context.Contains("延后")))
                .ToList();

            var delayRate = (double)delayedUpdates.Count / taskInteractions.Count;

            if (delayRate > 0.3)
            {
                UpdateOrCreatePattern(
                    "procrastination_tendency",
                    "拖延倾向",
                    $"任务延期率{delayRate:P1}",
                    delayedUpdates.Count
                );

                GenerateInsight(
                    "high_procrastination_rate",
                    "任务延期率较高",
                    $"近期{delayRate:P1}的任务被延期，可能存在拖延倾向",
                    "尝试将大任务拆解成小步骤，使用专注冲刺功能，或重新评估任务优先级",
                    0.9
                );
            }
        }

        private void GenerateInsights()
        {
            var recentPatterns = _patterns
                .Where(p => p.LastObserved > DateTime.Now.AddDays(-7))
                .ToList();

            if (recentPatterns.Count < 2) return;

            CheckWorkloadBalance();
            CheckFocusPatterns();
            CheckGoalAlignment();
        }

        private void CheckWorkloadBalance()
        {
            var taskPattern = _patterns.FirstOrDefault(p => p.PatternId == "task_creation_frequency");
            if (taskPattern == null) return;

            if (taskPattern.FrequencyByDay == null || taskPattern.FrequencyByDay.Count == 0)
            {
                return;
            }

            var avgTasksPerDay = taskPattern.FrequencyByDay.Values.Average();
            
            if (avgTasksPerDay > 10)
            {
                GenerateInsight(
                    "high_workload",
                    "任务量较大",
                    $"平均每天创建{avgTasksPerDay:F1}个任务，可能存在任务过载",
                    "考虑使用任务拆解功能，将任务分类并设置优先级，或委托部分任务",
                    0.85
                );
            }
            else if (avgTasksPerDay < 2)
            {
                GenerateInsight(
                    "low_task_engagement",
                    "任务创建较少",
                    "近期任务创建量较少，可以尝试设定更具体的目标",
                    "从长期目标开始，将大目标分解为可执行的小任务",
                    0.6
                );
            }
        }

        private void CheckFocusPatterns()
        {
            var voicePattern = _patterns.FirstOrDefault(p => p.PatternId == "voice_usage_preference");
            if (voicePattern == null) return;

            int recentCount = _interactions.Count(i => i.Timestamp > DateTime.Now.AddDays(-7));
            if (recentCount == 0)
            {
                return;
            }

            var voiceUsageRate = voicePattern.ObservationCount / (double)recentCount;

            if (voiceUsageRate < 0.1)
            {
                GenerateInsight(
                    "low_voice_usage",
                    "语音功能使用较少",
                    "语音功能可以帮助你快速记录任务，减少手动输入",
                    "尝试开启语音监听，用说话的方式快速添加任务",
                    0.5
                );
            }
        }

        private void CheckGoalAlignment()
        {
            var goalPattern = _patterns.FirstOrDefault(p => p.PatternId == "goal_review_frequency");
            if (goalPattern == null) return;

            var hasActiveGoals = _interactions.Any(i => i.Type == InteractionType.GoalOperation && 
                                                     i.Timestamp > DateTime.Now.AddDays(-7));

            if (!hasActiveGoals)
            {
                GenerateInsight(
                    "no_active_goals",
                    "缺少长期目标",
                    "设置长期目标可以帮助你更好地规划工作和生活",
                    "从你关心的事情开始，设定1-2个长期目标，并分解为可执行的任务",
                    0.9
                );
            }
        }

        private void UpdateOrCreatePattern(string patternId, string name, string description, 
            int observationCount, Dictionary<string, int> frequencyData = null)
        {
            var existingPattern = _patterns.FirstOrDefault(p => p.PatternId == patternId);
            
            if (existingPattern != null)
            {
                existingPattern.LastObserved = DateTime.Now;
                existingPattern.ObservationCount += observationCount;
                existingPattern.Description = description;
                
                if (frequencyData != null)
                {
                    foreach (var kvp in frequencyData)
                    {
                        if (!existingPattern.FrequencyByHour.ContainsKey(kvp.Key))
                        {
                            existingPattern.FrequencyByHour[kvp.Key] = 0;
                        }
                        existingPattern.FrequencyByHour[kvp.Key] += kvp.Value;
                    }
                }
                
                PatternDetected?.Invoke(existingPattern);
            }
            else
            {
                var newPattern = new BehaviorPattern
                {
                    PatternId = patternId,
                    PatternName = name,
                    Description = description,
                    Confidence = Math.Min(1.0, observationCount / 10.0),
                    FirstObserved = DateTime.Now,
                    LastObserved = DateTime.Now,
                    ObservationCount = observationCount,
                    FrequencyByHour = frequencyData ?? new Dictionary<string, int>(),
                    FrequencyByDay = new Dictionary<string, int>()
                };
                
                _patterns.Add(newPattern);
                PatternDetected?.Invoke(newPattern);
            }

            SavePatterns();
        }

        private void GenerateInsight(string insightId, string title, string description, 
            string recommendation, double priority)
        {
            var existingInsight = _insights.FirstOrDefault(i => i.InsightId == insightId);
            
            if (existingInsight == null)
            {
                var newInsight = new WorkHabitInsight
                {
                    InsightId = insightId,
                    Title = title,
                    Description = description,
                    Recommendation = recommendation,
                    GeneratedAt = DateTime.Now,
                    Priority = priority,
                    IsAcknowledged = false
                };
                
                _insights.Add(newInsight);
                NewInsightGenerated?.Invoke(newInsight);
                SaveInsights();
            }
        }

        public List<BehaviorPattern> GetPatterns()
        {
            lock (_lock)
            {
                return _patterns.OrderByDescending(p => p.LastObserved).ToList();
            }
        }

        public List<WorkHabitInsight> GetInsights(bool includeAcknowledged = false)
        {
            lock (_lock)
            {
                var query = _insights.AsEnumerable();
                
                if (!includeAcknowledged)
                {
                    query = query.Where(i => !i.IsAcknowledged);
                }
                
                return query.OrderByDescending(i => i.Priority).ThenByDescending(i => i.GeneratedAt).ToList();
            }
        }

        public void AcknowledgeInsight(string insightId)
        {
            lock (_lock)
            {
                var insight = _insights.FirstOrDefault(i => i.InsightId == insightId);
                if (insight != null)
                {
                    insight.IsAcknowledged = true;
                    SaveInsights();
                }
            }
        }

        public List<UserInteraction> GetInteractions(DateTime? startDate = null, DateTime? endDate = null, 
            InteractionType? type = null)
        {
            lock (_lock)
            {
                var query = _interactions.AsEnumerable();
                
                if (startDate.HasValue)
                {
                    query = query.Where(i => i.Timestamp >= startDate.Value);
                }
                
                if (endDate.HasValue)
                {
                    query = query.Where(i => i.Timestamp <= endDate.Value);
                }
                
                if (type.HasValue)
                {
                    query = query.Where(i => i.Type == type.Value);
                }
                
                return query.OrderByDescending(i => i.Timestamp).Take(1000).ToList();
            }
        }

        private void CleanupOldInteractions()
        {
            var cutoffDate = DateTime.Now.AddDays(-InteractionRetentionDays);
            _interactions = _interactions.Where(i => i.Timestamp >= cutoffDate).ToList();
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_interactionsPath))
                {
                    string json = File.ReadAllText(_interactionsPath);
                    var serializer = new JavaScriptSerializer();
                    _interactions = serializer.Deserialize<List<UserInteraction>>(json) ?? new List<UserInteraction>();
                }

                if (File.Exists(_patternsPath))
                {
                    string json = File.ReadAllText(_patternsPath);
                    var serializer = new JavaScriptSerializer();
                    _patterns = serializer.Deserialize<List<BehaviorPattern>>(json) ?? new List<BehaviorPattern>();
                }

                if (File.Exists(_insightsPath))
                {
                    string json = File.ReadAllText(_insightsPath);
                    var serializer = new JavaScriptSerializer();
                    _insights = serializer.Deserialize<List<WorkHabitInsight>>(json) ?? new List<WorkHabitInsight>();
                }

                CleanupOldInteractions();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserBehaviorObserver] Failed to load data: {ex.Message}");
            }
        }

        private void SaveInteractions()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_interactions);
                File.WriteAllText(_interactionsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserBehaviorObserver] Failed to save interactions: {ex.Message}");
            }
        }

        private void SavePatterns()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_patterns);
                File.WriteAllText(_patternsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserBehaviorObserver] Failed to save patterns: {ex.Message}");
            }
        }

        private void SaveInsights()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_insights);
                File.WriteAllText(_insightsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UserBehaviorObserver] Failed to save insights: {ex.Message}");
            }
        }
    }
}
