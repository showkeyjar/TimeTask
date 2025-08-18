using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace TimeTask.Tests
{
    /// <summary>
    /// 测试改进的任务提醒系统
    /// </summary>
    public class TestReminderSystem
    {
        public static async Task RunTests()
        {
            Console.WriteLine("=== 任务提醒系统测试 ===");
            
            // 测试1: 提醒时间计算
            TestReminderTiming();
            
            // 测试2: 设置验证
            TestSettingsValidation();
            
            // 测试3: 任务优先级分析（需要LLM服务）
            await TestTaskPriorityAnalysis();
            
            Console.WriteLine("=== 测试完成 ===");
        }
        
        private static void TestReminderTiming()
        {
            Console.WriteLine("\n--- 测试提醒时间计算 ---");
            
            var testTask = new ItemGrid
            {
                Task = "测试任务",
                CreatedDate = DateTime.Now.AddDays(-2),
                LastModifiedDate = DateTime.Now.AddDays(-2),
                InactiveWarningCount = 0,
                IsActive = true
            };
            
            // 模拟第一次提醒条件
            var inactiveDuration = DateTime.Now - testTask.LastModifiedDate;
            var firstWarningThreshold = TimeSpan.FromDays(1);
            
            if (inactiveDuration > firstWarningThreshold && testTask.InactiveWarningCount == 0)
            {
                Console.WriteLine("✓ 第一次提醒条件满足");
                testTask.InactiveWarningCount = 1;
                testTask.LastModifiedDate = DateTime.Now;
            }
            
            // 模拟第二次提醒条件
            testTask.LastModifiedDate = DateTime.Now.AddDays(-4);
            inactiveDuration = DateTime.Now - testTask.LastModifiedDate;
            var secondWarningThreshold = TimeSpan.FromDays(3);
            
            if (inactiveDuration > secondWarningThreshold && testTask.InactiveWarningCount == 1)
            {
                Console.WriteLine("✓ 第二次提醒条件满足");
                testTask.InactiveWarningCount = 2;
            }
            
            Console.WriteLine($"任务警告次数: {testTask.InactiveWarningCount}");
        }
        
        private static void TestSettingsValidation()
        {
            Console.WriteLine("\n--- 测试设置验证 ---");
            
            // 测试有效设置
            var validSettings = new
            {
                FirstWarningAfterDays = 1,
                SecondWarningAfterDays = 3,
                StaleTaskThresholdDays = 14,
                MaxInactiveWarnings = 3,
                ReminderCheckIntervalMinutes = 5
            };
            
            bool isValid = ValidateReminderSettings(
                validSettings.FirstWarningAfterDays,
                validSettings.SecondWarningAfterDays,
                validSettings.StaleTaskThresholdDays,
                validSettings.MaxInactiveWarnings,
                validSettings.ReminderCheckIntervalMinutes
            );
            
            Console.WriteLine($"✓ 有效设置验证: {(isValid ? "通过" : "失败")}");
            
            // 测试无效设置
            bool isInvalid = ValidateReminderSettings(5, 3, 14, 3, 5); // 第二次提醒小于第一次
            Console.WriteLine($"✓ 无效设置验证: {(!isInvalid ? "通过" : "失败")}");
        }
        
        private static bool ValidateReminderSettings(int first, int second, int stale, int maxWarnings, int interval)
        {
            if (first < 1 || first > 30) return false;
            if (second < first || second > 60) return false;
            if (stale < second || stale > 365) return false;
            if (maxWarnings < 1 || maxWarnings > 10) return false;
            if (interval < 1 || interval > 60) return false;
            return true;
        }
        
        private static async Task TestTaskPriorityAnalysis()
        {
            Console.WriteLine("\n--- 测试任务优先级分析 ---");
            
            try
            {
                var llmService = LlmService.Create();
                
                var testTasks = new[]
                {
                    "修复生产环境的关键bug",
                    "准备下周的项目演示",
                    "整理办公桌",
                    "回复邮件"
                };
                
                foreach (var taskDesc in testTasks)
                {
                    try
                    {
                        var (importance, urgency) = await llmService.AnalyzeTaskPriorityAsync(taskDesc);
                        var quadrant = GetQuadrantFromPriority(importance, urgency);
                        Console.WriteLine($"任务: {taskDesc}");
                        Console.WriteLine($"  重要性: {importance}, 紧急性: {urgency}");
                        Console.WriteLine($"  推荐象限: {quadrant}");
                        Console.WriteLine();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"分析任务 '{taskDesc}' 时出错: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LLM服务不可用: {ex.Message}");
            }
        }
        
        private static string GetQuadrantFromPriority(string importance, string urgency)
        {
            bool isImportant = importance?.ToLower().Contains("high") == true;
            bool isUrgent = urgency?.ToLower().Contains("high") == true;
            
            if (isImportant && isUrgent) return "重要且紧急";
            if (isImportant && !isUrgent) return "重要不紧急";
            if (!isImportant && isUrgent) return "不重要但紧急";
            return "不重要不紧急";
        }
        
        /// <summary>
        /// 创建测试任务数据
        /// </summary>
        public static List<ItemGrid> CreateTestTasks()
        {
            return new List<ItemGrid>
            {
                new ItemGrid
                {
                    Task = "需要第一次提醒的任务",
                    CreatedDate = DateTime.Now.AddDays(-2),
                    LastModifiedDate = DateTime.Now.AddDays(-2),
                    InactiveWarningCount = 0,
                    IsActive = true,
                    IsActiveInQuadrant = true,
                    Importance = "High",
                    Urgency = "High"
                },
                new ItemGrid
                {
                    Task = "需要第二次提醒的任务",
                    CreatedDate = DateTime.Now.AddDays(-5),
                    LastModifiedDate = DateTime.Now.AddDays(-4),
                    InactiveWarningCount = 1,
                    IsActive = true,
                    IsActiveInQuadrant = true,
                    Importance = "High",
                    Urgency = "Low"
                },
                new ItemGrid
                {
                    Task = "过期任务",
                    CreatedDate = DateTime.Now.AddDays(-20),
                    LastModifiedDate = DateTime.Now.AddDays(-15),
                    InactiveWarningCount = 2,
                    IsActive = true,
                    IsActiveInQuadrant = true,
                    Importance = "Medium",
                    Urgency = "Low"
                }
            };
        }
    }
}