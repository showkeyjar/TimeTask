using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Models;
using TimeTask.Models;
using TimeTask.Services;

namespace TimeTask
{
    public class LlmService : ILLMService, IDisposable
    {
        private readonly ILogger<LlmService> _logger;
        private readonly OpenAIAPI _openAiService;
        private bool _disposed = false;
        private const string PlaceholderApiKey = "YOUR_API_KEY_HERE";
        private const string DefaultModel = "gpt-3.5-turbo";

        public LlmService(ILogger<LlmService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            try
            {
                var apiKey = LoadApiKeyFromConfig() ?? PlaceholderApiKey;
                _openAiService = new OpenAIAPI(apiKey);
                
                if (apiKey == PlaceholderApiKey)
                {
                    _logger.LogWarning("No valid OpenAI API key found in configuration. Using placeholder key.");
                }
                else
                {
                    _logger.LogInformation("LLM Service initialized with API key");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize LLM Service");
                throw;
            }
        }
        
        private string? LoadApiKeyFromConfig()
        {
            try
            {
                var key = ConfigurationManager.AppSettings["OpenAIApiKey"];
                return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading API key from configuration");
                return null;
            }
        }

        private async Task<string> GetCompletionAsync(string prompt, string? systemPrompt = null, float temperature = 0.7f, int maxTokens = 1000)
        {
            try
            {
                var chat = _openAiService.Chat.CreateConversation();
                
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    chat.AppendSystemMessage(systemPrompt);
                }
                
                chat.AppendUserInput(prompt);
                
                var response = await chat.GetResponseFromChatbotAsync();
                return response;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in GetCompletionAsync");
                return $"Error processing your request: {ex.Message}";
            }
        }

        // Prompts with proper string literals
        private const string TaskClassificationPrompt = @"你是一个任务分类助手，请根据任务描述将其分类到以下四个象限之一：
0: 重要且紧急（需要立即处理的任务）
1: 重要不紧急（需要规划的任务）
2: 不重要但紧急（可以委派的任务）
3: 不重要不紧急（可以暂缓的任务）
只需返回0-3之间的数字，不要包含其他内容。";
            
        private const string TaskAnalysisPrompt = @"你是一个任务分析助手，请根据任务描述提供以下分析：
1. 任务可能需要的步骤分解
2. 可能的挑战和解决方案
3. 提高效率的建议
请用清晰的中文回答。";
            
        private const string PrioritizationSystemPrompt = @"Analyze the following task description and determine its importance and urgency. 
Return your answer strictly in the format: ""Importance: [High/Medium/Low], Urgency: [High/Medium/Low]"". 
Do not add any other text, explanations, or elaborations. Just the single line in the specified format.
For example, if the task is 'Fix critical login bug', you should respond with: ""Importance: High, Urgency: High"".
Task: ";

        private const string ClarityAnalysisSystemPrompt = @"Analyze the following task description for clarity, specificity, and actionability. 
Respond in the following format ONLY:
Status: [Clear/NeedsClarification]
Question: [If Status is NeedsClarification, provide a concise question to the user to get the necessary details. Otherwise, write N/A]
Examples:
Input Task: Organize event.
Status: NeedsClarification
Question: What kind of event is it and what are the key objectives or desired outcomes?

Input Task: Draft a project proposal for Q3 by Friday.
Status: Clear
Question: N/A

Input Task: ";

        private const string TaskDecompositionSystemPrompt = @"Analyze the following task description. If the task is too broad or complex, break it down into 2-5 actionable sub-tasks. 
If the task is already granular and actionable, indicate that it is sufficient. 
Respond in the following format ONLY:
Status: [Sufficient/NeedsDecomposition]
Subtasks: [If NeedsDecomposition, provide a list of sub-tasks, each on a new line, optionally prefixed with '-' or '*'. If Sufficient, write N/A.]
Examples:
Input Task: Plan company retreat.
Status: NeedsDecomposition
Subtasks:
- Define budget and objectives
- Research and select venue
- Plan agenda and activities
- Coordinate logistics (transport, accommodation)

Input Task: Email John about the meeting report.
Status: Sufficient
Subtasks: N/A

Input Task: ";

        private const string TaskReminderSystemPrompt = @"Example output:
Reminder: Just checking in on the '{taskDescription}' task. It's been about {taskAge}. How's it going?
Suggestion1: Ready to complete it now?
Suggestion2: Need to adjust its plan or priority?
Suggestion3: Want to break it into smaller pieces?";

        public async Task<(string reminder, List<string> suggestions)> GenerateTaskReminderAsync(string taskDescription, TimeSpan timeSinceLastModified)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                return (string.Empty, new List<string>());

            string formattedAge = FormatTimeSpan(timeSinceLastModified);
            string fullPrompt = TaskReminderSystemPrompt
                .Replace("{taskDescription}", taskDescription)
                .Replace("{taskAge}", formattedAge);

            string response = await GetCompletionAsync(fullPrompt);
            return ParseReminderResponse(response);
        }

        public async Task<(DecompositionStatus status, List<string> subtasks)> DecomposeTaskAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                return (DecompositionStatus.Unknown, new List<string>());

            string fullPrompt = TaskDecompositionSystemPrompt + taskDescription;
            string response = await GetCompletionAsync(fullPrompt);
            return ParseDecompositionResponse(response);
        }

        public async Task<(ClarityStatus status, string question)> AnalyzeTaskClarityAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                return (ClarityStatus.Unknown, "Task description cannot be empty.");

            string fullPrompt = ClarityAnalysisSystemPrompt + taskDescription;
            string response = await GetCompletionAsync(fullPrompt);
            return ParseClarityResponse(response);
        }

        public async Task<(string Importance, string Urgency)> GetTaskPriorityAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                return ("Unknown", "Unknown");

            string fullPrompt = PrioritizationSystemPrompt + taskDescription;
            string response = await GetCompletionAsync(fullPrompt);
            return ParsePriorityResponse(response);
        }

        public async Task<int> ClassifyTaskAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                return 0; // Default to first category

            string response = await GetCompletionAsync(TaskClassificationPrompt + taskDescription);
            if (int.TryParse(response.Trim(), out int category) && category >= 0 && category <= 3)
                return category;
            
            return 0; // Default to first category if parsing fails
        }

        public async Task<string> AnalyzeTaskAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
                return "Task description cannot be empty.";

            return await GetCompletionAsync(TaskAnalysisPrompt + taskDescription);
        }

        private (string reminder, List<string> suggestions) ParseReminderResponse(string response)
        {
            var suggestions = new List<string>();
            string reminder = string.Empty;

            if (string.IsNullOrWhiteSpace(response))
                return (reminder, suggestions);

            string[] lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (string line in lines)
            {
                if (line.StartsWith("Reminder:") && string.IsNullOrEmpty(reminder))
                    reminder = line.Substring("Reminder:".Length).Trim();
                else if (line.StartsWith("Suggestion1:"))
                    suggestions.Add(line.Substring("Suggestion1:".Length).Trim());
                else if (line.StartsWith("Suggestion2:"))
                    suggestions.Add(line.Substring("Suggestion2:".Length).Trim());
                else if (line.StartsWith("Suggestion3:"))
                    suggestions.Add(line.Substring("Suggestion3:".Length).Trim());
            }

            return (reminder, suggestions);
        }

        private (DecompositionStatus status, List<string> subtasks) ParseDecompositionResponse(string response)
        {
            var subtasks = new List<string>();
            if (string.IsNullOrWhiteSpace(response))
                return (DecompositionStatus.Unknown, subtasks);

            bool needsDecomposition = response.Contains("Status: NeedsDecomposition");
            bool inSubtasksSection = false;

            foreach (string line in response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Trim().StartsWith("Subtasks:", StringComparison.OrdinalIgnoreCase))
                {
                    inSubtasksSection = true;
                    continue;
                }

                if (inSubtasksSection && !line.Trim().Equals("N/A", StringComparison.OrdinalIgnoreCase))
                {
                    string subtask = line.TrimStart(' ', '-', '*').Trim();
                    if (!string.IsNullOrEmpty(subtask))
                        subtasks.Add(subtask);
                }
            }

            return (needsDecomposition ? DecompositionStatus.NeedsDecomposition : DecompositionStatus.Sufficient, subtasks);
        }

        private (ClarityStatus status, string question) ParseClarityResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return (ClarityStatus.Unknown, "No response from AI");

            bool needsClarification = response.Contains("Status: NeedsClarification");
            string question = "N/A";

            if (needsClarification)
            {
                int questionIndex = response.IndexOf("Question:", StringComparison.OrdinalIgnoreCase);
                if (questionIndex >= 0)
                {
                    question = response.Substring(questionIndex + "Question:".Length).Trim();
                }
            }

            return (needsClarification ? ClarityStatus.NeedsClarification : ClarityStatus.Clear, question);
        }

        private (string Importance, string Urgency) ParsePriorityResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return ("Unknown", "Unknown");

            string importance = "Unknown";
            string urgency = "Unknown";

            string[] parts = response.Split(',');
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("Importance:", StringComparison.OrdinalIgnoreCase))
                    importance = trimmed.Substring("Importance:".Length).Trim();
                else if (trimmed.StartsWith("Urgency:", StringComparison.OrdinalIgnoreCase))
                    urgency = trimmed.Substring("Urgency:".Length).Trim();
            }

            return (importance, urgency);
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalMinutes < 1)
                return "less than a minute";
            if (timeSpan.TotalHours < 1)
                return $"{(int)timeSpan.TotalMinutes} minute{(timeSpan.TotalMinutes >= 2 ? "s" : "")}";
            if (timeSpan.TotalDays < 1)
                return $"{(int)timeSpan.TotalHours} hour{(timeSpan.TotalHours >= 2 ? "s" : "")}";
            if (timeSpan.TotalDays < 30)
                return $"{(int)timeSpan.TotalDays} day{(timeSpan.TotalDays >= 2 ? "s" : "")}";
            if (timeSpan.TotalDays < 365)
                return $"{(int)(timeSpan.TotalDays / 30)} month{((int)(timeSpan.TotalDays / 30) >= 2 ? "s" : "")}";
            return $"{(int)(timeSpan.TotalDays / 365)} year{(timeSpan.TotalDays >= 730 ? "s" : "")}";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~LlmService()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // OpenAIAPI doesn't implement IDisposable in all versions
                    if (_openAiService is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                _disposed = true;
            }
        }
    }
}
