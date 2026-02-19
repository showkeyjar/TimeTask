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
    public enum GoalAdjustmentType
    {
        ExtendDeadline,
        IncreasePriority,
        DecomposeTask,
        ReduceScope,
        SuggestAlternative,
        RequestReview
    }

    public class GoalAdjustmentSuggestion
    {
        public string SuggestionId { get; set; }
        public string GoalId { get; set; }
        public string GoalDescription { get; set; }
        public GoalAdjustmentType AdjustmentType { get; set; }
        public string Reason { get; set; }
        public string Suggestion { get; set; }
        public DateTime GeneratedAt { get; set; }
        public bool IsAccepted { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public bool IsDismissed { get; set; }
        public DateTime? DismissedAt { get; set; }
        public double Urgency { get; set; }
    }

    public class GoalProgressTracker
    {
        public string GoalId { get; set; }
        public DateTime LastCheckDate { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int ActiveTasks { get; set; }
        public int StuckTasks { get; set; }
        public double CompletionRate { get; set; }
        public TimeSpan TimeElapsed { get; set; }
        public TimeSpan TimeRemaining { get; set; }
        public double ExpectedProgress { get; set; }
        public double ProgressDeviation { get; set; }
        public bool IsBehindSchedule { get; set; }
        public bool IsAtRisk { get; set; }
    }

    public class AdaptiveGoalManager
    {
        private readonly string _dataPath;
        private readonly string _suggestionsPath;
        private readonly string _progressHistoryPath;
        private readonly object _lock = new object();
        private List<GoalAdjustmentSuggestion> _suggestions;
        private List<GoalProgressTracker> _progressHistory;
        private UserProfileManager _userProfileManager;
        private UserBehaviorObserver _behaviorObserver;
        private const int SuggestionRetentionDays = 30;
        private const int ProgressHistoryRetentionDays = 90;

        public event Action<GoalAdjustmentSuggestion> AdjustmentSuggested;
        public event Action<GoalProgressTracker> GoalAtRisk;

        public AdaptiveGoalManager(string appDataPath, UserProfileManager userProfileManager, UserBehaviorObserver behaviorObserver)
        {
            _dataPath = appDataPath;
            _suggestionsPath = Path.Combine(appDataPath, "adaptive", "adjustment_suggestions.json");
            _progressHistoryPath = Path.Combine(appDataPath, "adaptive", "progress_history.json");
            
            Directory.CreateDirectory(Path.GetDirectoryName(_suggestionsPath));
            
            _suggestions = new List<GoalAdjustmentSuggestion>();
            _progressHistory = new List<GoalProgressTracker>();
            _userProfileManager = userProfileManager;
            _behaviorObserver = behaviorObserver;
            
            LoadData();
        }

        public void CheckAndAdjustGoals()
        {
            lock (_lock)
            {
                var goals = LoadActiveGoals();
                if (goals == null || !goals.Any()) return;

                foreach (var goal in goals)
                {
                    var tracker = AnalyzeGoalProgress(goal);
                    SaveProgressTracker(tracker);

                    if (tracker.IsAtRisk)
                    {
                        GoalAtRisk?.Invoke(tracker);
                        GenerateAdjustmentSuggestions(goal, tracker);
                    }
                }

                CleanupOldData();
            }
        }

        private List<LongTermGoal> LoadActiveGoals()
        {
            string goalsPath = Path.Combine(_dataPath, "long_term_goals.csv");
            if (!File.Exists(goalsPath)) return null;

            var goals = HelperClass.ReadLongTermGoalsCsv(goalsPath);
            return goals?.Where(g => g.IsActive).ToList();
        }

        private GoalProgressTracker AnalyzeGoalProgress(LongTermGoal goal)
        {
            var tracker = new GoalProgressTracker
            {
                GoalId = goal.Id,
                LastCheckDate = DateTime.Now
            };

            var relatedTasks = LoadRelatedTasks(goal.Id);
            tracker.TotalTasks = relatedTasks.Count;
            tracker.CompletedTasks = relatedTasks.Count(t => !t.IsActive);
            tracker.ActiveTasks = relatedTasks.Count(t => t.IsActive);
            tracker.StuckTasks = relatedTasks.Count(t => t.IsActive && 
                t.LastModifiedDate < DateTime.Now.AddDays(-3) && 
                t.InactiveWarningCount >= 1);

            tracker.CompletionRate = tracker.TotalTasks > 0 
                ? (double)tracker.CompletedTasks / tracker.TotalTasks 
                : 0;

            if (goal.StartDate.HasValue)
            {
                tracker.TimeElapsed = DateTime.Now - goal.StartDate.Value;
            }

            if (goal.EndDate.HasValue)
            {
                tracker.TimeRemaining = goal.EndDate.Value - DateTime.Now;
            }

            if (goal.StartDate.HasValue && goal.EndDate.HasValue)
            {
                var totalDuration = goal.EndDate.Value - goal.StartDate.Value;
                tracker.ExpectedProgress = totalDuration.TotalDays > 0 
                    ? tracker.TimeElapsed.TotalDays / totalDuration.TotalDays 
                    : 0;

                tracker.ProgressDeviation = tracker.ExpectedProgress - tracker.CompletionRate;
                tracker.IsBehindSchedule = tracker.ProgressDeviation > 0.2;
            }

            tracker.IsAtRisk = DetermineIfAtRisk(tracker, goal);

            return tracker;
        }

        private List<ItemGrid> LoadRelatedTasks(string goalId)
        {
            var relatedTasks = new List<ItemGrid>();
            string[] quadrantFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };

            foreach (var file in quadrantFiles)
            {
                string filePath = Path.Combine(_dataPath, file);
                if (File.Exists(filePath))
                {
                    var tasks = HelperClass.ReadCsv(filePath);
                    if (tasks != null)
                    {
                        relatedTasks.AddRange(tasks.Where(t => t.LongTermGoalId == goalId));
                    }
                }
            }

            return relatedTasks;
        }

        private bool DetermineIfAtRisk(GoalProgressTracker tracker, LongTermGoal goal)
        {
            if (tracker.IsBehindSchedule && tracker.ProgressDeviation > 0.3)
            {
                return true;
            }

            if (tracker.TimeRemaining.TotalDays < 7 && tracker.CompletionRate < 0.5)
            {
                return true;
            }

            if (tracker.StuckTasks > tracker.ActiveTasks * 0.5)
            {
                return true;
            }

            if (goal.LastReviewDate < DateTime.Now.AddDays(-14) && tracker.CompletionRate < 0.3)
            {
                return true;
            }

            return false;
        }

        private void GenerateAdjustmentSuggestions(LongTermGoal goal, GoalProgressTracker tracker)
        {
            var existingSuggestions = _suggestions
                .Where(s => s.GoalId == goal.Id && 
                           !s.IsAccepted && 
                           !s.IsDismissed &&
                           s.GeneratedAt > DateTime.Now.AddDays(-7))
                .ToList();

            if (existingSuggestions.Any()) return;

            var suggestions = new List<GoalAdjustmentSuggestion>();

            if (tracker.IsBehindSchedule && tracker.ProgressDeviation > 0.3)
            {
                suggestions.Add(new GoalAdjustmentSuggestion
                {
                    SuggestionId = Guid.NewGuid().ToString("N"),
                    GoalId = goal.Id,
                    GoalDescription = goal.Description,
                    AdjustmentType = GoalAdjustmentType.ExtendDeadline,
                    Reason = $"目标进度落后预期{tracker.ProgressDeviation:P1}",
                    Suggestion = $"考虑将目标截止日期从{goal.EndDate?.ToString("yyyy-MM-dd")}延长1-2周，以匹配当前进度",
                    GeneratedAt = DateTime.Now,
                    Urgency = 0.8
                });
            }

            if (tracker.StuckTasks > tracker.ActiveTasks * 0.5)
            {
                suggestions.Add(new GoalAdjustmentSuggestion
                {
                    SuggestionId = Guid.NewGuid().ToString("N"),
                    GoalId = goal.Id,
                    GoalDescription = goal.Description,
                    AdjustmentType = GoalAdjustmentType.DecomposeTask,
                    Reason = $"有{tracker.StuckTasks}个任务卡住超过3天",
                    Suggestion = "使用任务拆解功能，将卡住的任务分解为更小的可执行步骤",
                    GeneratedAt = DateTime.Now,
                    Urgency = 0.9
                });
            }

            if (tracker.TimeRemaining.TotalDays < 7 && tracker.CompletionRate < 0.5)
            {
                suggestions.Add(new GoalAdjustmentSuggestion
                {
                    SuggestionId = Guid.NewGuid().ToString("N"),
                    GoalId = goal.Id,
                    GoalDescription = goal.Description,
                    AdjustmentType = GoalAdjustmentType.ReduceScope,
                    Reason = $"距离截止日期仅剩{tracker.TimeRemaining.Days}天，但完成率仅{tracker.CompletionRate:P0}",
                    Suggestion = "考虑减少目标范围，优先完成最核心的部分",
                    GeneratedAt = DateTime.Now,
                    Urgency = 1.0
                });

                suggestions.Add(new GoalAdjustmentSuggestion
                {
                    SuggestionId = Guid.NewGuid().ToString("N"),
                    GoalId = goal.Id,
                    GoalDescription = goal.Description,
                    AdjustmentType = GoalAdjustmentType.IncreasePriority,
                    Reason = "目标即将到期但进度不足",
                    Suggestion = "将相关任务提升到高优先级，集中精力完成",
                    GeneratedAt = DateTime.Now,
                    Urgency = 1.0
                });
            }

            if (goal.LastReviewDate < DateTime.Now.AddDays(-14))
            {
                suggestions.Add(new GoalAdjustmentSuggestion
                {
                    SuggestionId = Guid.NewGuid().ToString("N"),
                    GoalId = goal.Id,
                    GoalDescription = goal.Description,
                    AdjustmentType = GoalAdjustmentType.RequestReview,
                    Reason = $"目标已{Math.Floor((DateTime.Now - goal.LastReviewDate).TotalDays)}天未回顾",
                    Suggestion = "建议立即回顾目标进展，重新评估任务优先级和时间安排",
                    GeneratedAt = DateTime.Now,
                    Urgency = 0.7
                });
            }

            foreach (var suggestion in suggestions)
            {
                _suggestions.Add(suggestion);
                AdjustmentSuggested?.Invoke(suggestion);
            }

            SaveSuggestions();
        }

        public void AcceptSuggestion(string suggestionId)
        {
            lock (_lock)
            {
                var suggestion = _suggestions.FirstOrDefault(s => s.SuggestionId == suggestionId);
                if (suggestion != null)
                {
                    suggestion.IsAccepted = true;
                    suggestion.AcceptedAt = DateTime.Now;
                    SaveSuggestions();

                    ApplyAdjustment(suggestion);
                    _behaviorObserver.RecordSystemEvent("goal_adjustment_accepted", 
                        $"Goal: {suggestion.GoalDescription}, Type: {suggestion.AdjustmentType}");
                }
            }
        }

        public void DismissSuggestion(string suggestionId)
        {
            lock (_lock)
            {
                var suggestion = _suggestions.FirstOrDefault(s => s.SuggestionId == suggestionId);
                if (suggestion != null)
                {
                    suggestion.IsDismissed = true;
                    suggestion.DismissedAt = DateTime.Now;
                    SaveSuggestions();

                    _behaviorObserver.RecordSystemEvent("goal_adjustment_dismissed",
                        $"Goal: {suggestion.GoalDescription}, Type: {suggestion.AdjustmentType}");
                }
            }
        }

        private void ApplyAdjustment(GoalAdjustmentSuggestion suggestion)
        {
            var goals = LoadActiveGoals();
            if (goals == null) return;

            var goal = goals.FirstOrDefault(g => g.Id == suggestion.GoalId);
            if (goal == null) return;

            switch (suggestion.AdjustmentType)
            {
                case GoalAdjustmentType.ExtendDeadline:
                    if (goal.EndDate.HasValue)
                    {
                        goal.EndDate = goal.EndDate.Value.AddDays(14);
                        SaveGoal(goal);
                    }
                    break;

                case GoalAdjustmentType.IncreasePriority:
                    IncreaseGoalTaskPriority(goal.Id);
                    break;

                case GoalAdjustmentType.DecomposeTask:
                    var stuckTasks = LoadRelatedTasks(goal.Id)
                        .Where(t => t.IsActive && 
                                   t.LastModifiedDate < DateTime.Now.AddDays(-3))
                        .ToList();
                    
                    foreach (var task in stuckTasks.Take(3))
                    {
                        _behaviorObserver.RecordSystemEvent("suggest_task_decomposition", 
                            $"Task: {task.Task}");
                    }
                    break;

                case GoalAdjustmentType.ReduceScope:
                    var lowPriorityTasks = LoadRelatedTasks(goal.Id)
                        .Where(t => t.IsActive && 
                                   string.Equals(t.Importance, "Low", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    foreach (var task in lowPriorityTasks)
                    {
                        task.IsActiveInQuadrant = false;
                    }
                    SaveGoalTasks(goal.Id, lowPriorityTasks);
                    break;

                case GoalAdjustmentType.RequestReview:
                    goal.LastReviewDate = DateTime.Now;
                    SaveGoal(goal);
                    break;
            }
        }

        private void IncreaseGoalTaskPriority(string goalId)
        {
            var tasks = LoadRelatedTasks(goalId);
            foreach (var task in tasks.Where(t => t.IsActive))
            {
                if (string.Equals(task.Importance, "Low", StringComparison.OrdinalIgnoreCase))
                {
                    task.Importance = "Medium";
                }
                else if (string.Equals(task.Urgency, "Low", StringComparison.OrdinalIgnoreCase))
                {
                    task.Urgency = "Medium";
                }
            }
            SaveGoalTasks(goalId, tasks);
        }

        private void SaveGoal(LongTermGoal goal)
        {
            string goalsPath = Path.Combine(_dataPath, "long_term_goals.csv");
            var goals = HelperClass.ReadLongTermGoalsCsv(goalsPath) ?? new List<LongTermGoal>();
            
            var existingGoal = goals.FirstOrDefault(g => g.Id == goal.Id);
            if (existingGoal != null)
            {
                goals.Remove(existingGoal);
            }
            goals.Add(goal);

            var csvLines = new List<string>
            {
                "id,description,startDate,endDate,isActive,lastReviewDate"
            };

            foreach (var g in goals)
            {
                csvLines.Add($"{g.Id},{g.Description},{g.StartDate?.ToString("o") ?? ""},{g.EndDate?.ToString("o") ?? ""},{g.IsActive},{g.LastReviewDate:o}");
            }

            File.WriteAllLines(goalsPath, csvLines);
        }

        private void SaveGoalTasks(string goalId, List<ItemGrid> tasks)
        {
            string[] quadrantFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            foreach (var file in quadrantFiles)
            {
                string filePath = Path.Combine(_dataPath, file);
                if (File.Exists(filePath))
                {
                    var allTasks = HelperClass.ReadCsv(filePath);
                    if (allTasks != null)
                    {
                        foreach (var task in allTasks.Where(t => t.LongTermGoalId == goalId))
                        {
                            var updatedTask = tasks.FirstOrDefault(t => 
                                t.Task == task.Task && 
                                t.CreatedDate == task.CreatedDate);
                            
                            if (updatedTask != null)
                            {
                                task.Importance = updatedTask.Importance;
                                task.Urgency = updatedTask.Urgency;
                                task.IsActiveInQuadrant = updatedTask.IsActiveInQuadrant;
                                task.LastModifiedDate = DateTime.Now;
                            }
                        }
                        HelperClass.WriteCsv(allTasks, filePath);
                    }
                }
            }
        }

        public List<GoalAdjustmentSuggestion> GetActiveSuggestions()
        {
            lock (_lock)
            {
                return _suggestions
                    .Where(s => !s.IsAccepted && !s.IsDismissed)
                    .OrderByDescending(s => s.Urgency)
                    .ThenByDescending(s => s.GeneratedAt)
                    .ToList();
            }
        }

        public List<GoalProgressTracker> GetProgressHistory(string goalId = null)
        {
            lock (_lock)
            {
                var query = _progressHistory.AsEnumerable();
                
                if (!string.IsNullOrWhiteSpace(goalId))
                {
                    query = query.Where(p => p.GoalId == goalId);
                }
                
                return query.OrderByDescending(p => p.LastCheckDate).ToList();
            }
        }

        private void SaveProgressTracker(GoalProgressTracker tracker)
        {
            _progressHistory.Add(tracker);
            SaveProgressHistory();
        }

        private void CleanupOldData()
        {
            var suggestionCutoff = DateTime.Now.AddDays(-SuggestionRetentionDays);
            _suggestions = _suggestions.Where(s => s.GeneratedAt >= suggestionCutoff).ToList();

            var progressCutoff = DateTime.Now.AddDays(-ProgressHistoryRetentionDays);
            _progressHistory = _progressHistory.Where(p => p.LastCheckDate >= progressCutoff).ToList();

            SaveSuggestions();
            SaveProgressHistory();
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_suggestionsPath))
                {
                    string json = File.ReadAllText(_suggestionsPath);
                    var serializer = new JavaScriptSerializer();
                    _suggestions = serializer.Deserialize<List<GoalAdjustmentSuggestion>>(json) ?? new List<GoalAdjustmentSuggestion>();
                }

                if (File.Exists(_progressHistoryPath))
                {
                    string json = File.ReadAllText(_progressHistoryPath);
                    var serializer = new JavaScriptSerializer();
                    _progressHistory = serializer.Deserialize<List<GoalProgressTracker>>(json) ?? new List<GoalProgressTracker>();
                }

                CleanupOldData();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdaptiveGoalManager] Failed to load data: {ex.Message}");
            }
        }

        private void SaveSuggestions()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_suggestions);
                File.WriteAllText(_suggestionsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdaptiveGoalManager] Failed to save suggestions: {ex.Message}");
            }
        }

        private void SaveProgressHistory()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_progressHistory);
                File.WriteAllText(_progressHistoryPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdaptiveGoalManager] Failed to save progress history: {ex.Message}");
            }
        }
    }
}
