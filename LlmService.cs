using System;
using System.Configuration; // Added for ConfigurationManager
using System.Threading.Tasks;
using OpenAI; 
using OpenAI.Managers; 
using OpenAI.ObjectModels.RequestModels; 
using System.Text.RegularExpressions; 
using System.Collections.Generic; // For List<string>
using System.Linq; // For Any()

namespace TimeTask
{
    public enum ClarityStatus
    {
        Clear,
        NeedsClarification,
        Unknown
    }

    public enum DecompositionStatus
    {
        Sufficient,
        NeedsDecomposition,
        Unknown
    }

    public class LlmService
    {
        private OpenAIService _openAiService;
        private string _apiKey;
        private const string PlaceholderApiKey = "YOUR_API_KEY_GOES_HERE"; 

        private const string PrioritizationSystemPrompt = 
            "Analyze the following task description and determine its importance and urgency. " +
            "Return your answer strictly in the format: \"Importance: [High/Medium/Low], Urgency: [High/Medium/Low]\". " +
            "Do not add any other text, explanations, or elaborations. Just the single line in the specified format.\n" +
            "For example, if the task is 'Fix critical login bug', you should respond with: \"Importance: High, Urgency: High\".\n" +
            "Task: ";

        private const string ClarityAnalysisSystemPrompt =
            "Analyze the following task description for clarity, specificity, and actionability. " +
            "Respond in the following format ONLY:\n" +
            "Status: [Clear/NeedsClarification]\n" +
            "Question: [If Status is NeedsClarification, provide a concise question to the user to get the necessary details. Otherwise, write N/A]\n" +
            "Examples:\n" +
            "Input Task: Organize event.\n" +
            "Status: NeedsClarification\n" +
            "Question: What kind of event is it and what are the key objectives or desired outcomes?\n\n" +
            "Input Task: Draft a project proposal for Q3 by Friday.\n" +
            "Status: Clear\n" +
            "Question: N/A\n\n" +
            "Input Task: ";

        private const string TaskDecompositionSystemPrompt =
            "Analyze the following task description. If the task is too broad or complex, break it down into 2-5 actionable sub-tasks. " +
            "If the task is already granular and actionable, indicate that it is sufficient. " +
            "Respond in the following format ONLY:\n" +
            "Status: [Sufficient/NeedsDecomposition]\n" +
            "Subtasks: [If NeedsDecomposition, provide a list of sub-tasks, each on a new line, optionally prefixed with '-' or '*'. If Sufficient, write N/A.]\n" +
            "Examples:\n" +
            "Input Task: Plan company retreat.\n" +
            "Status: NeedsDecomposition\n" +
            "Subtasks:\n" +
            "- Define budget and objectives\n" +
            "- Research and select venue\n" +
            "- Plan agenda and activities\n" +
            "- Coordinate logistics (transport, accommodation)\n\n" +
            "Input Task: Email John about the meeting report.\n" +
            "Status: Sufficient\n" +
            "Subtasks: N/A\n\n" +
            "Input Task: ";

        private const string TaskReminderSystemPrompt =
            "You are an assistant helping a user review a task. The task is described below, and you're given how old it is (time since last modification). " +
            "Generate a brief, friendly, encouraging reminder about the task. " +
            "Then, provide 2-3 actionable, concise suggestions for the user. " +
            "Respond in the following format ONLY:\n" +
            "Reminder: [Generated reminder text]\n" +
            "Suggestion1: [Text for suggestion 1]\n" +
            "Suggestion2: [Text for suggestion 2]\n" +
            "(Optional) Suggestion3: [Text for suggestion 3]\n\n" +
            "Task Description: {taskDescription}\n" +
            "Task Age: {taskAge}\n\n" +
            "Example output:\n" +
            "Reminder: Just checking in on the '{taskDescription}' task. It's been about {taskAge}. How's it going?\n" +
            "Suggestion1: Ready to complete it now?\n" +
            "Suggestion2: Need to adjust its plan or priority?\n" +
            "Suggestion3: Want to break it into smaller pieces?";

        public LlmService()
        {
            LoadApiKeyFromConfig();
            InitializeOpenAiService();
        }

        internal string FormatTimeSpan(TimeSpan ts) // Made internal for testing
        {
            if (ts.TotalDays >= 7)
            {
                int weeks = (int)(ts.TotalDays / 7);
                return $"{weeks} week{(weeks > 1 ? "s" : "")} old";
            }
            if (ts.TotalDays >= 1)
            {
                return $"{(int)ts.TotalDays} day{(ts.TotalDays > 1 ? "s" : "")} old";
            }
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours} hour{(ts.TotalHours > 1 ? "s" : "")} old";
            }
            return "less than an hour old";
        }

        internal static (string reminder, List<string> suggestions) ParseReminderResponse(string llmResponse)
        {
            var suggestions = new List<string>();
            string reminder = string.Empty;

            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                return (reminder, suggestions);
            }

            try
            {
                llmResponse = llmResponse.Replace("\r\n", "\n").Replace("\r", "\n");
                string[] lines = llmResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("Reminder:", StringComparison.OrdinalIgnoreCase))
                    {
                        reminder = trimmedLine.Substring("Reminder:".Length).Trim();
                    }
                    else if (trimmedLine.StartsWith("Suggestion1:", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add(trimmedLine.Substring("Suggestion1:".Length).Trim());
                    }
                    else if (trimmedLine.StartsWith("Suggestion2:", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add(trimmedLine.Substring("Suggestion2:".Length).Trim());
                    }
                    else if (trimmedLine.StartsWith("Suggestion3:", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add(trimmedLine.Substring("Suggestion3:".Length).Trim());
                    }
                }

                if (string.IsNullOrWhiteSpace(reminder) && !suggestions.Any())
                {
                    Console.WriteLine($"Could not parse reminder or suggestions from LLM response: '{llmResponse}'.");
                }
                return (reminder, suggestions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM reminder response: {ex.Message}. Response was: {llmResponse}");
                return (string.Empty, new List<string>());
            }
        }
        
        public async Task<(string reminder, List<string> suggestions)> GenerateTaskReminderAsync(string taskDescription, TimeSpan timeSinceLastModified)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
            {
                return (string.Empty, new List<string>());
            }

            string formattedAge = FormatTimeSpan(timeSinceLastModified);
            string fullPrompt = TaskReminderSystemPrompt
                                .Replace("{taskDescription}", taskDescription)
                                .Replace("{taskAge}", formattedAge);
            
            string llmResponse = await GetCompletionAsync(fullPrompt);

            if (string.IsNullOrWhiteSpace(llmResponse) || llmResponse.StartsWith("LLM dummy response") || llmResponse.StartsWith("Error from LLM"))
            {
                Console.WriteLine($"LLM did not provide a valid reminder for '{taskDescription}'. Response: {llmResponse}");
                return (string.Empty, new List<string>());
            }
            return ParseReminderResponse(llmResponse);
        }

        internal static (DecompositionStatus status, List<string> subtasks) ParseDecompositionResponse(string llmResponse)
        {
            var subtasks = new List<string>();
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                return (DecompositionStatus.Unknown, subtasks);
            }

            try
            {
                DecompositionStatus status = DecompositionStatus.Unknown;
                var statusMatch = Regex.Match(llmResponse, @"Status:\s*(Sufficient|NeedsDecomposition)", RegexOptions.IgnoreCase);

                if (statusMatch.Success)
                {
                    string statusStr = statusMatch.Groups[1].Value;
                    if (!Enum.TryParse(statusStr, true, out status))
                    {
                        status = DecompositionStatus.Unknown;
                        Console.WriteLine($"Could not parse decomposition status value '{statusStr}' from LLM response: '{llmResponse}'.");
                    }
                }
                else
                {
                    Console.WriteLine($"Could not find 'Status:' for decomposition in LLM response: '{llmResponse}'.");
                    return (DecompositionStatus.Unknown, subtasks);
                }

                if (status == DecompositionStatus.NeedsDecomposition)
                {
                    int subtasksHeaderIndex = llmResponse.IndexOf("Subtasks:", StringComparison.OrdinalIgnoreCase);
                    if (subtasksHeaderIndex != -1)
                    {
                        string subtasksSection = llmResponse.Substring(subtasksHeaderIndex + "Subtasks:".Length);
                        string[] lines = subtasksSection.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines)
                        {
                            string trimmedLine = line.Trim();
                            if (trimmedLine.StartsWith("-") || trimmedLine.StartsWith("*"))
                            {
                                trimmedLine = trimmedLine.Substring(1).Trim();
                            }
                            if (!string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                            {
                                subtasks.Add(trimmedLine);
                            }
                        }
                        if (!subtasks.Any())
                        {
                            Console.WriteLine($"Decomposition status is NeedsDecomposition but no valid subtasks found in response: '{llmResponse}'.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Decomposition status is NeedsDecomposition but 'Subtasks:' header not found in LLM response: '{llmResponse}'.");
                        status = DecompositionStatus.Unknown; 
                    }
                }
                return (status, subtasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM decomposition response: {ex.Message}. Response was: {llmResponse}");
                return (DecompositionStatus.Unknown, new List<string>());
            }
        }

        public async Task<(DecompositionStatus status, List<string> subtasks)> DecomposeTaskAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
            {
                return (DecompositionStatus.Unknown, new List<string>());
            }

            string fullPrompt = TaskDecompositionSystemPrompt + taskDescription;
            string llmResponse = await GetCompletionAsync(fullPrompt);
            
            if (string.IsNullOrWhiteSpace(llmResponse) || llmResponse.StartsWith("LLM dummy response") || llmResponse.StartsWith("Error from LLM"))
            {
                Console.WriteLine($"LLM did not provide a valid decomposition for '{taskDescription}'. Response: {llmResponse}");
                return (DecompositionStatus.Unknown, new List<string>());
            }
            return ParseDecompositionResponse(llmResponse);
        }

        internal static (ClarityStatus status, string question) ParseClarityResponse(string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                 return (ClarityStatus.Unknown, "LLM response was empty.");
            }

            try
            {
                ClarityStatus status = ClarityStatus.Unknown;
                string question = "Failed to parse clarity analysis.";

                var statusMatch = Regex.Match(llmResponse, @"Status:\s*(Clear|NeedsClarification)", RegexOptions.IgnoreCase);
                var questionMatch = Regex.Match(llmResponse, @"Question:\s*(.*)", RegexOptions.IgnoreCase);

                if (statusMatch.Success)
                {
                    string statusStr = statusMatch.Groups[1].Value;
                    if (Enum.TryParse(statusStr, true, out ClarityStatus parsedStatus))
                    {
                        status = parsedStatus;
                    }
                    else
                    {
                        Console.WriteLine($"Could not parse status value '{statusStr}' from LLM response: '{llmResponse}'.");
                        status = ClarityStatus.Unknown; 
                    }
                }
                else
                {
                    Console.WriteLine($"Could not find 'Status:' line in LLM response: '{llmResponse}'.");
                }

                if (questionMatch.Success)
                {
                    question = questionMatch.Groups[1].Value.Trim();
                    if (status == ClarityStatus.Clear && (string.IsNullOrWhiteSpace(question) || question.Equals("N/A", StringComparison.OrdinalIgnoreCase)))
                    {
                        question = string.Empty; 
                    }
                }
                else
                {
                    Console.WriteLine($"Could not find 'Question:' line in LLM response: '{llmResponse}'.");
                    if (status != ClarityStatus.Unknown) question = "Question not found in response.";
                }
                
                if (status == ClarityStatus.Unknown && !statusMatch.Success)
                {
                    question = "Failed to parse status and question from LLM response.";
                }
                return (status, question);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM clarity response: {ex.Message}. Response was: {llmResponse}");
                return (ClarityStatus.Unknown, "Failed to analyze task clarity due to an exception.");
            }
        }

        public async Task<(ClarityStatus status, string question)> AnalyzeTaskClarityAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
            {
                return (ClarityStatus.Unknown, "Task description cannot be empty.");
            }

            string fullPrompt = ClarityAnalysisSystemPrompt + taskDescription;
            string llmResponse = await GetCompletionAsync(fullPrompt);

            if (string.IsNullOrWhiteSpace(llmResponse) || llmResponse.StartsWith("LLM dummy response") || llmResponse.StartsWith("Error from LLM"))
            {
                Console.WriteLine($"LLM did not provide a valid clarity analysis for '{taskDescription}'. Response: {llmResponse}");
                return (ClarityStatus.Unknown, "Failed to analyze task clarity (LLM error or dummy response).");
            }
            return ParseClarityResponse(llmResponse);
        }

        internal static (string Importance, string Urgency) ParsePriorityResponse(string llmResponse)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                return ("Unknown", "Unknown");
            }
            try
            {
                string importance = "Unknown";
                string urgency = "Unknown";
                string normalizedResponse = llmResponse.Trim();
                string[] parts = normalizedResponse.Split(',');

                if (parts.Length == 2)
                {
                    string importancePart = parts[0].Trim();
                    string urgencyPart = parts[1].Trim();

                    if (importancePart.StartsWith("Importance:", StringComparison.OrdinalIgnoreCase))
                    {
                        importance = importancePart.Substring("Importance:".Length).Trim();
                    }
                    if (urgencyPart.StartsWith("Urgency:", StringComparison.OrdinalIgnoreCase))
                    {
                        urgency = urgencyPart.Substring("Urgency:".Length).Trim();
                    }
                }

                string[] validPriorities = { "High", "Medium", "Low" };
                var validPrioritySet = new HashSet<string>(validPriorities, StringComparer.OrdinalIgnoreCase);

                if (!validPrioritySet.Contains(importance)) importance = "Unknown";
                if (!validPrioritySet.Contains(urgency)) urgency = "Unknown";
                
                if (importance == "Unknown" && urgency == "Unknown" && 
                    !(normalizedResponse.IndexOf("Importance:", StringComparison.OrdinalIgnoreCase) >= 0 && 
                      normalizedResponse.IndexOf("Urgency:", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                     Console.WriteLine($"Could not parse Importance/Urgency from LLM response: '{llmResponse}'. Defaulting to Unknown.");
                }
                return (importance, urgency);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM priority response: {ex.Message}. Response was: {llmResponse}");
                return ("Unknown", "Unknown");
            }
        }

        public async Task<(string Importance, string Urgency)> GetTaskPriorityAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription))
            {
                return ("Unknown", "Unknown");
            }

            string fullPrompt = PrioritizationSystemPrompt + taskDescription;
            string llmResponse = await GetCompletionAsync(fullPrompt);

            if (string.IsNullOrWhiteSpace(llmResponse) || llmResponse.StartsWith("LLM dummy response") || llmResponse.StartsWith("Error from LLM"))
            {
                Console.WriteLine($"LLM did not provide a valid priority response for '{taskDescription}'. Response: {llmResponse}");
                return ("Unknown", "Unknown");
            }
            return ParsePriorityResponse(llmResponse);
        }
        
        private void LoadApiKeyFromConfig()
        {
            try
            {
                _apiKey = ConfigurationManager.AppSettings["OpenAIApiKey"];
            }
            catch (ConfigurationErrorsException ex)
            {
                Console.WriteLine($"Error reading App.config: {ex.Message}");
                _apiKey = null; 
            }

            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == PlaceholderApiKey)
            {
                Console.WriteLine("Warning: LLM Service initialized using placeholder or missing API key from App.config. Please configure a valid OpenAIApiKey in App.config.");
                _apiKey = PlaceholderApiKey; 
            }
        }

        private void InitializeOpenAiService()
        {
            _openAiService = new OpenAIService(new OpenAI.OpenAiOptions()
            {
                ApiKey = _apiKey
            });
        }
        
        public void Init()
        {
            LoadApiKeyFromConfig();
            InitializeOpenAiService();
        }

        public async Task<string> GetCompletionAsync(string prompt)
        {
            if (_openAiService == null || _apiKey == PlaceholderApiKey || string.IsNullOrWhiteSpace(_apiKey))
            {
                Console.WriteLine("LLM Service not properly initialized. Returning dummy response.");
                return await Task.FromResult($"LLM dummy response for: {prompt}");
            }

            try
            {
                var completionResult = await _openAiService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
                {
                    Messages = new[]
                    {
                        ChatMessage.FromUser(prompt)
                    },
                    Model = "gpt-3.5-turbo", // Changed to string literal for broader compatibility
                    MaxTokens = 150 
                });

                if (completionResult.Successful)
                {
                    return completionResult.Choices.First().Message.Content;
                }
                else
                {
                    if (completionResult.Error == null)
                    {
                        throw new Exception("Unknown Error");
                    }
                    Console.WriteLine($"LLM API Error: {completionResult.Error.Message}");
                    return $"Error from LLM: {completionResult.Error.Message}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during LLM call: {ex.Message}");
                return await Task.FromResult($"LLM dummy response (due to exception) for: {prompt}");
            }
        }

        public static LlmService Create()
        {
            return new LlmService();
        }
    }
}
