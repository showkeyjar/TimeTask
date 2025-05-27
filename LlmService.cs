using System;
using System.Configuration; // Added for ConfigurationManager
using System.Threading.Tasks;
using OpenAI; // Assuming this is the correct namespace for OpenAI-DotNet
using OpenAI.Managers; // Assuming this namespace is needed for OpenAIService
using OpenAI.ObjectModels.RequestModels; // Assuming this namespace is needed for ChatCompletionCreateRequest
using System.Text.RegularExpressions; // For parsing clarity response

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
        private const string PlaceholderApiKey = "YOUR_API_KEY_GOES_HERE"; // Defined constant for placeholder

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

        // Constructor that now reads from App.config
        public LlmService()
        {
            LoadApiKeyFromConfig();
            InitializeOpenAiService();
        }

        // Example usage for GetTaskPriorityAsync:
        // LlmService service = LlmService.Create();
        // string task = "Develop a comprehensive marketing plan for the new product launch.";
        // var (importance, urgency) = await service.GetTaskPriorityAsync(task);
        // Console.WriteLine($"Task: {task}\nImportance: {importance}, Urgency: {urgency}");
        // Expected output (example): Importance: High, Urgency: Medium (or similar, depending on LLM)

        // Example usage for AnalyzeTaskClarityAsync:
        // LlmService service = LlmService.Create();
        // string vagueTask = "Work on the report.";
        // var (status, question) = await service.AnalyzeTaskClarityAsync(vagueTask);
        // Console.WriteLine($"Task: {vagueTask}\nStatus: {status}\nQuestion: {question}");
        // Expected output (example): Status: NeedsClarification, Question: What specific report are you referring to and what aspects need to be worked on?
        //
        // string clearTask = "Submit the Q2 financial report to accounting by EOD Tuesday.";
        // (status, question) = await service.AnalyzeTaskClarityAsync(clearTask);
        // Console.WriteLine($"Task: {clearTask}\nStatus: {status}\nQuestion: {question}");
        // Expected output (example): Status: Clear, Question: N/A

        // Example usage for DecomposeTaskAsync:
        // LlmService service = LlmService.Create();
        // string complexTask = "Organize a client appreciation event.";
        // var (decompStatus, subtasks) = await service.DecomposeTaskAsync(complexTask);
        // Console.WriteLine($"Task: {complexTask}\nStatus: {decompStatus}");
        // if (subtasks.Any()) {
        //     Console.WriteLine("Subtasks:");
        //     subtasks.ForEach(st => Console.WriteLine($"- {st}"));
        // }
        // Expected output (example):
        // Task: Organize a client appreciation event.
        // Status: NeedsDecomposition
        // Subtasks:
        // - Determine guest list and budget
        // - Choose date, time, and venue
        // - Plan event agenda and entertainment
        // - Send invitations and manage RSVPs
        //
        // string simpleTask = "Send weekly update email to the team.";
        // (decompStatus, subtasks) = await service.DecomposeTaskAsync(simpleTask);
        // Console.WriteLine($"Task: {simpleTask}\nStatus: {decompStatus}");
        // Expected output (example): Task: Send weekly update email to the team. Status: Sufficient

        // Example usage for GenerateTaskReminderAsync:
        // LlmService service = LlmService.Create();
        // string oldTask = "Finalize budget report";
        // TimeSpan age = TimeSpan.FromDays(28); // 4 weeks
        // var (reminder, suggestions) = await service.GenerateTaskReminderAsync(oldTask, age);
        // Console.WriteLine($"Reminder: {reminder}");
        // if (suggestions.Any()) {
        //     Console.WriteLine("Suggestions:");
        //     suggestions.ForEach(s => Console.WriteLine($"- {s}"));
        // }
        // Expected output (example):
        // Reminder: Just checking in on the 'Finalize budget report' task. It's been about 4 weeks old. How's it going?
        // Suggestions:
        // - Ready to complete it now?
        // - Need to adjust its plan or priority?
        // - Want to break it into smaller pieces?

        private string FormatTimeSpan(TimeSpan ts)
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
            var suggestions = new List<string>();
            string reminder = string.Empty;

            if (string.IsNullOrWhiteSpace(llmResponse) || llmResponse.StartsWith("LLM dummy response") || llmResponse.StartsWith("Error from LLM"))
            {
                Console.WriteLine($"LLM did not provide a valid reminder for '{taskDescription}'. Response: {llmResponse}");
                return (reminder, suggestions);
            }

            try
            {
                // Normalize line endings
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
                    // Add more suggestions if needed (Suggestion4, Suggestion5, etc.)
                }

                if (string.IsNullOrWhiteSpace(reminder) && !suggestions.Any())
                {
                     Console.WriteLine($"Could not parse reminder or suggestions from LLM response: '{llmResponse}'.");
                     // Return empty if nothing was parsed, otherwise return what was parsed.
                }
                
                return (reminder, suggestions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM reminder response for '{taskDescription}': {ex.Message}. Response was: {llmResponse}");
                return (string.Empty, new List<string>()); // Return empty on exception
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
            var subtasks = new List<string>();

            if (string.IsNullOrWhiteSpace(llmResponse) || llmResponse.StartsWith("LLM dummy response") || llmResponse.StartsWith("Error from LLM"))
            {
                Console.WriteLine($"LLM did not provide a valid decomposition for '{taskDescription}'. Response: {llmResponse}");
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
                        status = DecompositionStatus.Unknown; // Default if parsing enum fails
                        Console.WriteLine($"Could not parse decomposition status value '{statusStr}' from LLM response: '{llmResponse}'.");
                    }
                }
                else
                {
                     Console.WriteLine($"Could not find 'Status:' for decomposition in LLM response: '{llmResponse}'.");
                     return (DecompositionStatus.Unknown, subtasks); // Cannot proceed without status
                }

                if (status == DecompositionStatus.NeedsDecomposition)
                {
                    // Find the "Subtasks:" line and take everything after it
                    int subtasksHeaderIndex = llmResponse.IndexOf("Subtasks:", StringComparison.OrdinalIgnoreCase);
                    if (subtasksHeaderIndex != -1)
                    {
                        string subtasksSection = llmResponse.Substring(subtasksHeaderIndex + "Subtasks:".Length);
                        string[] lines = subtasksSection.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines)
                        {
                            string trimmedLine = line.Trim();
                            // Remove optional prefixes like '-' or '*'
                            if (trimmedLine.StartsWith("-") || trimmedLine.StartsWith("*"))
                            {
                                trimmedLine = trimmedLine.Substring(1).Trim();
                            }
                            if (!string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                            {
                                subtasks.Add(trimmedLine);
                            }
                        }
                         if (!subtasks.Any()) // If "Subtasks:" was present but no actual tasks found or only N/A
                        {
                            Console.WriteLine($"Decomposition status is NeedsDecomposition but no valid subtasks found in response: '{llmResponse}'.");
                            // Potentially could change status to Unknown or Sufficient if no subtasks are actually listed.
                            // For now, we trust the LLM's status and return an empty list if parsing yields nothing.
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Decomposition status is NeedsDecomposition but 'Subtasks:' header not found in LLM response: '{llmResponse}'.");
                        // Status is NeedsDecomposition, but no subtasks provided. Could be an LLM error.
                        // Return as Unknown with empty list, or keep as NeedsDecomposition with empty list.
                        status = DecompositionStatus.Unknown; // LLM failed to follow format
                    }
                }
                // If status is Sufficient, subtasks list remains empty, which is correct.
                // If status is Unknown due to parsing failure, subtasks list also remains empty.

                return (status, subtasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM decomposition response for '{taskDescription}': {ex.Message}. Response was: {llmResponse}");
                return (DecompositionStatus.Unknown, new List<string>());
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

            try
            {
                // Expected format:
                // Status: [Clear/NeedsClarification]
                // Question: [If NeedsClarification, the question. Otherwise, N/A]
                
                ClarityStatus status = ClarityStatus.Unknown;
                string question = "Failed to parse clarity analysis.";

                // Use Regex for more robust parsing of potentially multi-line or slightly varied responses
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
                        status = ClarityStatus.Unknown; // Default if parsing the enum value fails
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
                        question = string.Empty; // Standardize question for Clear status
                    }
                }
                else
                {
                     Console.WriteLine($"Could not find 'Question:' line in LLM response: '{llmResponse}'.");
                     // Keep default "Failed to parse..." or set based on status if Status was parsed
                     if(status != ClarityStatus.Unknown) question = "Question not found in response.";
                }
                
                // If status is still unknown after trying to parse, it implies a significant parsing failure.
                if (status == ClarityStatus.Unknown && !statusMatch.Success)
                {
                    question = "Failed to parse status and question from LLM response.";
                }


                return (status, question);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM clarity response for '{taskDescription}': {ex.Message}. Response was: {llmResponse}");
                return (ClarityStatus.Unknown, "Failed to analyze task clarity due to an exception.");
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

            try
            {
                // Expected format: "Importance: [High/Medium/Low], Urgency: [High/Medium/Low]"
                string importance = "Unknown";
                string urgency = "Unknown";

                // Normalize response for easier parsing
                string normalizedResponse = llmResponse.Trim();

                // Attempt to parse using string splitting
                string[] parts = normalizedResponse.Split(',');
                if (parts.Length == 2)
                {
                    string importancePart = parts[0].Trim();
                    string urgencyPart = parts[1].Trim();

                    if (importancePart.StartsWith("Importance:"))
                    {
                        importance = importancePart.Substring("Importance:".Length).Trim();
                    }
                    if (urgencyPart.StartsWith("Urgency:"))
                    {
                        urgency = urgencyPart.Substring("Urgency:".Length).Trim();
                    }
                }
                
                // Basic validation if the values are among expected ones (optional, but good for robustness)
                string[] validPriorities = { "High", "Medium", "Low", "Unknown" };
                if (!Array.Exists(validPriorities, p => p.Equals(importance, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"Parsed Importance '{importance}' is not a recognized value. Defaulting to Unknown.");
                    importance = "Unknown";
                }
                if (!Array.Exists(validPriorities, p => p.Equals(urgency, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"Parsed Urgency '{urgency}' is not a recognized value. Defaulting to Unknown.");
                    urgency = "Unknown";
                }
                
                // If parsing failed to find both, mark as Unknown
                if (importance == "Unknown" && urgency == "Unknown" && !normalizedResponse.Contains("Importance:") && !normalizedResponse.Contains("Urgency:"))
                {
                     Console.WriteLine($"Could not parse Importance/Urgency from LLM response: '{llmResponse}'. Defaulting to Unknown.");
                }


                return (importance, urgency);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM priority response for '{taskDescription}': {ex.Message}. Response was: {llmResponse}");
                return ("Unknown", "Unknown");
            }
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
                _apiKey = null; // Explicitly set to null if config is broken
            }

            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == PlaceholderApiKey)
            {
                Console.WriteLine("Warning: LLM Service initialized using placeholder or missing API key from App.config. Please configure a valid OpenAIApiKey in App.config.");
                _apiKey = PlaceholderApiKey; // Fallback to placeholder
            }
        }

        private void InitializeOpenAiService()
        {
            // The OpenAI-DotNet library typically uses an API key directly in the constructor of OpenAIService
            // or through OpenAiOptions configuration.
            _openAiService = new OpenAIService(new OpenAI.OpenAiOptions()
            {
                ApiKey = _apiKey
            });
        }
        
        // Initialization method (alternative to constructor or for re-initialization)
        // Now also reads from App.config
        public void Init()
        {
            LoadApiKeyFromConfig();
            InitializeOpenAiService();
        }

        public async Task<string> GetCompletionAsync(string prompt)
        {
            if (_openAiService == null || _apiKey == PlaceholderApiKey || string.IsNullOrWhiteSpace(_apiKey))
            {
                // Return a dummy response if the service isn't properly initialized
                Console.WriteLine("LLM Service not properly initialized. Returning dummy response.");
                return await Task.FromResult($"LLM dummy response for: {prompt}");
            }

            try
            {
                // This is a simplified example for chat completion.
                // You might need to adjust based on the specific model and parameters you intend to use.
                var completionResult = await _openAiService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
                {
                    Messages = new[]
                    {
                        ChatMessage.FromUser(prompt)
                    },
                    Model = OpenAI.ObjectModels.Models.Gpt_3_5_Turbo, // Or your preferred model
                    MaxTokens = 150 // Adjust as needed
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
                // Fallback to dummy response in case of an exception
                return await Task.FromResult($"LLM dummy response (due to exception) for: {prompt}");
            }
        }

        // Static method to create an instance. Now uses the default constructor which handles App.config.
        public static LlmService Create()
        {
            // The constructor will handle API key loading from App.config and warnings.
            return new LlmService();
        }
    }
}
