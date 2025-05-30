using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TimeTask.Services
{
    public class ZhipuAIService : ILLMService, IDisposable
    {
        private readonly ILogger<ZhipuAIService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private bool _disposed = false;
        private const string ApiBaseUrl = "https://open.bigmodel.cn/api/paas/v3/chat/completions";
        private const string ModelName = "chatglm_pro";

        public ZhipuAIService(ILogger<ZhipuAIService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _apiKey = ConfigurationManager.AppSettings["ZhipuAI:ApiKey"]?.Trim();
            
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "YOUR_API_KEY_HERE")
            {
                _logger.LogWarning("Zhipu AI API key is not configured. Some features may not work correctly.");
            }

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }
        }

        public async Task<int> ClassifyTaskAsync(string taskDescription)
        {
            try
            {
                var prompt = $"请将以下任务分类到四象限中（只需返回数字0-3）：\n" +
                           $"0 - 重要且紧急\n" +
                           $"1 - 重要不紧急\n" +
                           $"2 - 不重要但紧急\n" +
                           $"3 - 不重要不紧急\n\n" +
                           $"任务：{taskDescription}";

                var response = await GetChatCompletionAsync(new[]
                {
                    new { role = "user", content = prompt }
                });

                if (int.TryParse(response?.Trim(), out int result) && result >= 0 && result <= 3)
                {
                    return result;
                }
                
                return 0; // Default to 重要且紧急
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ClassifyTaskAsync");
                return 0; // Default to 重要且紧急
            }
        }

        public async Task<string> AnalyzeTaskAsync(string taskDescription)
        {
            try
            {
                var messages = new[]
                {
                    new { role = "system", content = "你是一个任务分析助手，请根据任务描述提供以下分析：\n1. 任务目标\n2. 关键步骤\n3. 可能遇到的挑战\n4. 建议的解决方案\n\n请用清晰的中文回答。" },
                    new { role = "user", content = taskDescription }
                };

                var response = await GetChatCompletionAsync(messages);
                return response ?? "未能获取任务分析结果，请重试。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AnalyzeTaskAsync");
                return $"分析任务时出错: {ex.Message}";
            }
        }

        public async Task<(string reminder, List<string> suggestions)> GenerateTaskReminderAsync(string taskDescription, TimeSpan timeSinceLastModified)
        {
            try
            {
                var prompt = $"任务：{taskDescription}\n" +
                           $"该任务已经{timeSinceLastModified.TotalHours:0}小时未更新。请生成一个提醒和3条建议。" +
                           "使用以下JSON格式回复：\n" +
                           "{\"reminder\": \"提醒内容\", \"suggestions\": [\"建议1\", \"建议2\", \"建议3\"]}";

                var messages = new[] { new { role = "user", content = prompt } };
                var response = await GetChatCompletionAsync(messages);

                if (!string.IsNullOrEmpty(response))
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<ReminderResponse>(response);
                        return (result?.Reminder ?? "请记得处理这个任务", 
                                result?.Suggestions ?? new List<string> { "考虑设置一个截止时间", "将任务分解为小步骤", "安排专门的时间处理" });
                    }
                    catch
                    {
                        // If JSON parsing fails, return the raw response
                        return (response, new List<string>());
                    }
                }

                return ("请记得处理这个任务", new List<string> { "考虑设置一个截止时间", "将任务分解为小步骤", "安排专门的时间处理" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GenerateTaskReminderAsync");
                return ($"生成提醒时出错: {ex.Message}", new List<string>());
            }
        }

        public async Task<(DecompositionStatus status, List<string> subtasks)> DecomposeTaskAsync(string taskDescription)
        {
            try
            {
                var prompt = $"请将以下任务分解为子任务（如果任务已经足够具体，请返回'Sufficient'）：\n\n{taskDescription}\n\n" +
                           "请以JSON格式返回，包含'status'和'subtasks'字段。例如：\n" +
                           "{\"status\": \"NeedsDecomposition\", \"subtasks\": [\"子任务1\", \"子任务2\"]} 或 {\"status\": \"Sufficient\", \"subtasks\": []}";

                var messages = new[] { new { role = "user", content = prompt } };
                var response = await GetChatCompletionAsync(messages);

                if (!string.IsNullOrEmpty(response))
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<DecompositionResponse>(response);
                        if (result != null)
                        {
                            var status = result.Status?.ToLower() == "sufficient" 
                                ? DecompositionStatus.Sufficient 
                                : DecompositionStatus.NeedsDecomposition;
                            
                            return (status, result.Subtasks ?? new List<string>());
                        }
                    }
                    catch
                    {
                        // If JSON parsing fails, return as a single subtask
                        return (DecompositionStatus.Sufficient, new List<string> { taskDescription });
                    }
                }

                return (DecompositionStatus.Sufficient, new List<string> { taskDescription });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DecomposeTaskAsync");
                return (DecompositionStatus.Unknown, new List<string>());
            }
        }

        public async Task<(ClarityStatus status, string question)> AnalyzeTaskClarityAsync(string taskDescription)
        {
            try
            {
                var prompt = $"请分析以下任务描述是否清晰明确。如果不清晰，请提出一个问题来澄清：\n\n{taskDescription}\n\n" +
                           "请以JSON格式返回，包含'status'和'question'字段。例如：\n" +
                           "{\"status\": \"Clear\", \"question\": \"\"} 或 {\"status\": \"NeedsClarification\", \"question\": \"需要澄清的问题\"}";

                var messages = new[] { new { role = "user", content = prompt } };
                var response = await GetChatCompletionAsync(messages);

                if (!string.IsNullOrEmpty(response))
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<ClarityResponse>(response);
                        if (result != null)
                        {
                            var status = result.Status?.ToLower() == "clear" 
                                ? ClarityStatus.Clear 
                                : ClarityStatus.NeedsClarification;
                            
                            return (status, result.Question ?? string.Empty);
                        }
                    }
                    catch
                    {
                        // If JSON parsing fails, assume the task is clear
                        return (ClarityStatus.Clear, string.Empty);
                    }
                }

                return (ClarityStatus.Clear, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AnalyzeTaskClarityAsync");
                return (ClarityStatus.Unknown, string.Empty);
            }
        }

        public async Task<(string Importance, string Urgency)> GetTaskPriorityAsync(string taskDescription)
        {
            try
            {
                var prompt = $"请分析以下任务的重要性和紧急性，返回1-5的评分（1最低，5最高）。\n" +
                           $"任务：{taskDescription}\n\n" +
                           $"请以JSON格式返回，例如：\n" +
                           "{\"importance\": \"3\", \"urgency\": \"4\"}";

                var messages = new[] { new { role = "user", content = prompt } };
                var response = await GetChatCompletionAsync(messages);

                if (!string.IsNullOrEmpty(response))
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<PriorityResponse>(response);
                        if (result != null)
                        {
                            return (result.Importance ?? "3", result.Urgency ?? "3");
                        }
                    }
                    catch
                    {
                        // If JSON parsing fails, return default values
                    }
                }

                return ("3", "3");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetTaskPriorityAsync");
                return ("3", "3");
            }
        }

        private async Task<string> GetChatCompletionAsync(object[] messages)
        {
            try
            {
                var request = new
                {
                    model = ModelName,
                    messages,
                    temperature = 0.7,
                    max_tokens = 2000
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request), 
                    Encoding.UTF8, 
                    "application/json");

                var response = await _httpClient.PostAsync(ApiBaseUrl, content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonDocument.Parse(responseContent);
                
                return jsonResponse.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Zhipu AI API");
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }

        private class ReminderResponse
        {
            public string Reminder { get; set; } = string.Empty;
            public List<string> Suggestions { get; set; } = new List<string>();
        }

        private class DecompositionResponse
        {
            public string Status { get; set; } = string.Empty;
            public List<string> Subtasks { get; set; } = new List<string>();
        }

        private class ClarityResponse
        {
            public string Status { get; set; } = string.Empty;
            public string Question { get; set; } = string.Empty;
        }

        private class PriorityResponse
        {
            public string Importance { get; set; } = "3";
            public string Urgency { get; set; } = "3";
        }
    }
}
