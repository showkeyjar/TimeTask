using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Models;

namespace TimeTask.Services
{
    public class OpenAIService : ILLMService, IDisposable
    {
        private readonly ILogger<OpenAIService> _logger;
        private readonly OpenAIAPI _openAiService;
        private bool _disposed = false;
        private const string DefaultModel = "gpt-3.5-turbo";

        public OpenAIService(ILogger<OpenAIService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            var apiKey = ConfigurationManager.AppSettings["OpenAI:ApiKey"];
            
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("OpenAI API key is not configured. Some features may not work correctly.");
                apiKey = "invalid-api-key";
            }
            
            try
            {
                _openAiService = new OpenAIAPI(apiKey);
                _logger.LogInformation("OpenAI service initialized");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize OpenAI service");
                throw;
            }
        }

        private const string TaskClassificationPrompt = "你是一个任务分类助手，请根据任务描述将其分类到以下四个象限之一：\n" +
                                          "0: 重要且紧急（需要立即处理的任务）\n" +
                                          "1: 重要不紧急（需要规划的任务）\n" +
                                          "2: 不重要但紧急（可以委派的任务）\n" +
                                          "3: 不重要不紧急（可以暂缓的任务）\n" +
                                          "只需返回0-3之间的数字，不要包含其他内容。";

        public async Task<(string reminder, List<string> suggestions)> GenerateTaskReminderAsync(string taskDescription, TimeSpan timeSinceLastModified)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                throw new ArgumentException("Task description cannot be null or empty", nameof(taskDescription));
                
            try
            {
                // Simulate async work
                await Task.Delay(100).ConfigureAwait(false);
                
                // TODO: Implement actual reminder generation logic using OpenAI API
                var reminder = $"Reminder for task: {taskDescription}";
                var suggestions = new List<string> { "Suggestion 1", "Suggestion 2" };
                
                return (reminder, suggestions);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error generating task reminder");
                throw;
            }
        }

        public async Task<(DecompositionStatus status, List<string> subtasks)> DecomposeTaskAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                throw new ArgumentException("Task description cannot be null or empty", nameof(taskDescription));
                
            try
            {
                // Simulate async work
                await Task.Delay(100).ConfigureAwait(false);
                
                // TODO: Implement actual task decomposition logic using OpenAI API
                return (DecompositionStatus.Sufficient, new List<string> { "Subtask 1", "Subtask 2" });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error decomposing task");
                return (DecompositionStatus.Unknown, new List<string>());
            }
        }

        public async Task<(ClarityStatus status, string question)> AnalyzeTaskClarityAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                throw new ArgumentException("Task description cannot be null or empty", nameof(taskDescription));
                
            try
            {
                // Simulate async work
                await Task.Delay(100).ConfigureAwait(false);
                
                // TODO: Implement actual clarity analysis logic using OpenAI API
                return (ClarityStatus.Clear, string.Empty);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error analyzing task clarity");
                return (ClarityStatus.Unknown, string.Empty);
            }
        }

        public async Task<(string Importance, string Urgency)> GetTaskPriorityAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                throw new ArgumentException("Task description cannot be null or empty", nameof(taskDescription));
                
            try
            {
                // Simulate async work
                await Task.Delay(100).ConfigureAwait(false);
                
                // TODO: Implement actual priority analysis logic using OpenAI API
                return ("5", "5");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting task priority");
                return ("5", "5");
            }
        }

        public async Task<int> ClassifyTaskAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                throw new ArgumentException("Task description cannot be null or empty", nameof(taskDescription));
                
            try
            {
                var chat = _openAiService.Chat.CreateConversation();
                chat.AppendSystemMessage(TaskClassificationPrompt);
                chat.AppendUserInput(taskDescription);
                
                string response = await chat.GetResponseFromChatbotAsync().ConfigureAwait(false);
                
                if (string.IsNullOrWhiteSpace(response))
                {
                    _logger?.LogWarning("Empty response received from classification API");
                    return 0; // Default to important and urgent
                }
                
                if (int.TryParse(response.Trim(), out int result) && result >= 0 && result <= 3)
                {
                    return result;
                }

                _logger?.LogWarning($"Unexpected classification result: {response}");
                return 0; // Default to important and urgent
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error in ClassifyTaskAsync for: {taskDescription}");
                return 0; // Default to important and urgent
            }
        }

        public async Task<string> AnalyzeTaskAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                throw new ArgumentException("Task description cannot be null or empty", nameof(taskDescription));
                
            try
            {
                var chat = _openAiService.Chat.CreateConversation();
                chat.Model = DefaultModel;
                chat.RequestParameters.Temperature = 0.7;
                chat.RequestParameters.MaxTokens = 500;
                
                chat.AppendSystemMessage("你是一个任务分析助手，请根据任务描述提供以下分析：\n" +
                                      "1. 任务可能需要的步骤分解\n" +
                                      "2. 可能的挑战和解决方案\n" +
                                      "3. 提高效率的建议\n" +
                                      "请用清晰的中文回答。");
                chat.AppendUserInput(taskDescription);
                
                string response = await chat.GetResponseFromChatbotAsync().ConfigureAwait(false);
                
                if (string.IsNullOrWhiteSpace(response))
                {
                    _logger?.LogWarning("Empty response received from task analysis API");
                    return "未能获取任务分析结果，请重试。";
                }
                
                return response;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error in AnalyzeTaskAsync for: {taskDescription}");
                return "分析任务时出错，请检查网络连接后重试。";
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // No explicit dispose needed for OpenAIAPI
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
