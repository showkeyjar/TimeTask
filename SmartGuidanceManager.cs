using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;

namespace TimeTask
{
    public class OnboardingScenario
    {
        public string ScenarioId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string TriggerCondition { get; set; }
        public List<string> RecommendedSkills { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int Priority { get; set; }
        public bool AutoActivate { get; set; }
    }

    public class SmartGuidanceManager
    {
        private readonly string _dataPath;
        private readonly string _onboardingDataPath;
        private List<OnboardingScenario> _scenarios;
        private UserProfileManager _userProfileManager;

        public event Action<OnboardingScenario> ScenarioTriggered;
        public event Action<string> GuidanceSuggested;

        public SmartGuidanceManager(string appDataPath)
        {
            _dataPath = appDataPath;
            _onboardingDataPath = Path.Combine(appDataPath, "onboarding_scenarios.json");
            _userProfileManager = new UserProfileManager();
            _scenarios = new List<OnboardingScenario>();
            LoadScenarios();
        }

        public void Initialize()
        {
            InitializeDefaultScenarios();
            CheckAndTriggerScenarios();
        }

        private void InitializeDefaultScenarios()
        {
            if (_scenarios.Count == 0)
            {
                _scenarios = new List<OnboardingScenario>
                {
                    new OnboardingScenario
                    {
                        ScenarioId = "first_task",
                        Title = "欢迎使用！添加你的第一个任务",
                        Description = "开始使用TimeTask，添加一个你今天需要完成的任务。系统会自动帮你分析重要性和紧急性。",
                        TriggerCondition = "no_tasks",
                        RecommendedSkills = new List<string> { "decompose" },
                        Priority = 100,
                        AutoActivate = true,
                        IsCompleted = false
                    },
                    new OnboardingScenario
                    {
                        ScenarioId = "long_term_goal_intro",
                        Title = "设置长期目标，让工作更有方向",
                        Description = "长期目标可以帮助你更好地规划任务。比如：'学习Python编程'、'完成年度项目'等。",
                        TriggerCondition = "has_tasks_no_goals",
                        RecommendedSkills = new List<string> { "clarify_goal", "decompose" },
                        Priority = 90,
                        AutoActivate = true,
                        IsCompleted = false
                    },
                    new OnboardingScenario
                    {
                        ScenarioId = "skill_decompose_intro",
                        Title = "任务太大？试试任务拆解",
                        Description = "当你遇到复杂任务时，使用'任务拆解'功能可以将大任务分解成可执行的小步骤。",
                        TriggerCondition = "complex_task_detected",
                        RecommendedSkills = new List<string> { "decompose" },
                        Priority = 85,
                        AutoActivate = true,
                        IsCompleted = false
                    },
                    new OnboardingScenario
                    {
                        ScenarioId = "skill_focus_sprint_intro",
                        Title = "提高专注力：专注冲刺模式",
                        Description = "当你需要集中精力完成任务时，使用'专注冲刺'功能可以安排短时高专注的工作时段。",
                        TriggerCondition = "task_stuck",
                        RecommendedSkills = new List<string> { "focus_sprint" },
                        Priority = 80,
                        AutoActivate = true,
                        IsCompleted = false
                    },
                    new OnboardingScenario
                    {
                        ScenarioId = "voice_task_intro",
                        Title = "解放双手：语音添加任务",
                        Description = "开启语音监听，你可以通过说话快速添加任务。比如：'提醒我明天下午3点开会'。",
                        TriggerCondition = "voice_enabled",
                        RecommendedSkills = new List<string>(),
                        Priority = 70,
                        AutoActivate = false,
                        IsCompleted = false
                    },
                    new OnboardingScenario
                    {
                        ScenarioId = "goal_progress_check",
                        Title = "检查目标进度",
                        Description = "定期回顾你的长期目标进度，确保你朝着正确的方向前进。",
                        TriggerCondition = "periodic_goal_check",
                        RecommendedSkills = new List<string> { "risk_check", "priority_rebalance" },
                        Priority = 75,
                        AutoActivate = false,
                        IsCompleted = false
                    }
                };
                SaveScenarios();
            }
            else
            {
                NormalizeScenarioAutoActivateFlags();
                SaveScenarios();
            }
        }

        private void NormalizeScenarioAutoActivateFlags()
        {
            foreach (var scenario in _scenarios)
            {
                if (scenario == null || string.IsNullOrWhiteSpace(scenario.ScenarioId))
                {
                    continue;
                }

                switch (scenario.ScenarioId)
                {
                    case "first_task":
                    case "long_term_goal_intro":
                    case "skill_decompose_intro":
                    case "skill_focus_sprint_intro":
                        scenario.AutoActivate = true;
                        break;
                    default:
                        scenario.AutoActivate = false;
                        break;
                }
            }
        }

        public void CheckAndTriggerScenarios()
        {
            var activeScenarios = _scenarios.Where(s => !s.IsCompleted).OrderByDescending(s => s.Priority).ToList();
            
            foreach (var scenario in activeScenarios)
            {
                if (ShouldTriggerScenario(scenario))
                {
                    TriggerScenario(scenario);
                    break;
                }
            }
        }

        private bool ShouldTriggerScenario(OnboardingScenario scenario)
        {
            switch (scenario.TriggerCondition)
            {
                case "no_tasks":
                    return CheckNoTasksCondition();
                case "has_tasks_no_goals":
                    return CheckHasTasksNoGoalsCondition();
                case "complex_task_detected":
                    return CheckComplexTaskCondition();
                case "task_stuck":
                    return CheckTaskStuckCondition();
                case "voice_enabled":
                    return CheckVoiceEnabledCondition();
                case "periodic_goal_check":
                    return CheckPeriodicGoalCheckCondition();
                default:
                    return false;
            }
        }

        private bool CheckNoTasksCondition()
        {
            string[] quadrantFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            foreach (var file in quadrantFiles)
            {
                string filePath = Path.Combine(_dataPath, file);
                if (File.Exists(filePath))
                {
                    var tasks = HelperClass.ReadCsv(filePath);
                    if (tasks != null && tasks.Any(t => t.IsActive))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private bool CheckHasTasksNoGoalsCondition()
        {
            bool hasTasks = false;
            string[] quadrantFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            foreach (var file in quadrantFiles)
            {
                string filePath = Path.Combine(_dataPath, file);
                if (File.Exists(filePath))
                {
                    var tasks = HelperClass.ReadCsv(filePath);
                    if (tasks != null && tasks.Any(t => t.IsActive))
                    {
                        hasTasks = true;
                        break;
                    }
                }
            }

            if (!hasTasks) return false;

            string goalsPath = Path.Combine(_dataPath, "long_term_goals.csv");
            if (!File.Exists(goalsPath)) return true;

            var goals = HelperClass.ReadLongTermGoalsCsv(goalsPath);
            return goals == null || !goals.Any(g => g.IsActive);
        }

        private bool CheckComplexTaskCondition()
        {
            string[] quadrantFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            foreach (var file in quadrantFiles)
            {
                string filePath = Path.Combine(_dataPath, file);
                if (File.Exists(filePath))
                {
                    var tasks = HelperClass.ReadCsv(filePath);
                    if (tasks != null)
                    {
                        var complexTasks = tasks.Where(t => 
                            t.IsActive && 
                            !string.IsNullOrWhiteSpace(t.Task) &&
                            (t.Task.Length > 20 || t.Task.Contains("项目") || t.Task.Contains("系统") || t.Task.Contains("开发"))
                        ).ToList();
                        
                        if (complexTasks.Any())
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool CheckTaskStuckCondition()
        {
            string[] quadrantFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            foreach (var file in quadrantFiles)
            {
                string filePath = Path.Combine(_dataPath, file);
                if (File.Exists(filePath))
                {
                    var tasks = HelperClass.ReadCsv(filePath);
                    if (tasks != null)
                    {
                        var stuckTasks = tasks.Where(t => 
                            t.IsActive && 
                            t.LastModifiedDate < DateTime.Now.AddDays(-3) &&
                            t.InactiveWarningCount >= 1
                        ).ToList();
                        
                        if (stuckTasks.Any())
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool CheckVoiceEnabledCondition()
        {
            return true;
        }

        private bool CheckPeriodicGoalCheckCondition()
        {
            string goalsPath = Path.Combine(_dataPath, "long_term_goals.csv");
            if (!File.Exists(goalsPath)) return false;

            var goals = HelperClass.ReadLongTermGoalsCsv(goalsPath);
            if (goals == null || !goals.Any(g => g.IsActive)) return false;

            var activeGoals = goals.Where(g => g.IsActive).ToList();
            return activeGoals.Any(g => g.LastReviewDate < DateTime.Now.AddDays(-7));
        }

        private void TriggerScenario(OnboardingScenario scenario)
        {
            ScenarioTriggered?.Invoke(scenario);
            GuidanceSuggested?.Invoke(scenario.ScenarioId);
        }

        public void CompleteScenario(string scenarioId)
        {
            var scenario = _scenarios.FirstOrDefault(s => s.ScenarioId == scenarioId);
            if (scenario != null)
            {
                scenario.IsCompleted = true;
                scenario.CompletedAt = DateTime.Now;
                SaveScenarios();
            }
        }

        public List<OnboardingScenario> GetActiveScenarios()
        {
            return _scenarios.Where(s => !s.IsCompleted).OrderByDescending(s => s.Priority).ToList();
        }

        public List<OnboardingScenario> GetCompletedScenarios()
        {
            return _scenarios.Where(s => s.IsCompleted).OrderByDescending(s => s.CompletedAt).ToList();
        }

        public void ResetScenario(string scenarioId)
        {
            var scenario = _scenarios.FirstOrDefault(s => s.ScenarioId == scenarioId);
            if (scenario != null)
            {
                scenario.IsCompleted = false;
                scenario.CompletedAt = null;
                SaveScenarios();
            }
        }

        public void ResetAllScenarios()
        {
            foreach (var scenario in _scenarios)
            {
                scenario.IsCompleted = false;
                scenario.CompletedAt = null;
            }
            SaveScenarios();
        }

        private void LoadScenarios()
        {
            try
            {
                if (File.Exists(_onboardingDataPath))
                {
                    string json = File.ReadAllText(_onboardingDataPath);
                    var serializer = new JavaScriptSerializer();
                    var obj = serializer.Deserialize<List<OnboardingScenario>>(json);
                    _scenarios = obj ?? new List<OnboardingScenario>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load onboarding scenarios: {ex.Message}");
                _scenarios = new List<OnboardingScenario>();
            }
        }

        private void SaveScenarios()
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_scenarios);
                File.WriteAllText(_onboardingDataPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save onboarding scenarios: {ex.Message}");
            }
        }

        public List<string> GetRecommendedSkillsForTask(ItemGrid task)
        {
            var recommendedSkills = new List<string>();

            if (task != null && !string.IsNullOrWhiteSpace(task.Task))
            {
                if (task.Task.Length > 20 || task.Task.Contains("项目") || task.Task.Contains("系统"))
                {
                    recommendedSkills.Add("decompose");
                }

                if (string.Equals(task.Importance, "High", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(task.Urgency, "High", StringComparison.OrdinalIgnoreCase))
                {
                    recommendedSkills.Add("focus_sprint");
                }

                if (task.LastModifiedDate < DateTime.Now.AddDays(-2))
                {
                    recommendedSkills.Add("risk_check");
                }
            }

            return recommendedSkills;
        }
    }
}
