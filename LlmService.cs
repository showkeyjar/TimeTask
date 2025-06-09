using System;
using System.Configuration;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

// Using statements for Betalgo.Ranul.OpenAI
using Betalgo.Ranul.OpenAI; // For OpenAIService, OpenAIOptions
using System.Text.Json; // Added for System.Text.Json
using Betalgo.Ranul.OpenAI.Interfaces; // For IOpenAIService
using Betalgo.Ranul.OpenAI.ObjectModels; // For Models (e.g., Models.Gpt_3_5_Turbo)
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels; // For ChatCompletionCreateRequest, ChatMessage

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

    public class ProposedDailyTask
    {
        public int Day { get; set; }
        public string TaskDescription { get; set; }
        public string Quadrant { get; set; }
        public string EstimatedTime { get; set; }
    }

    public class LlmService : ILlmService
    {
        private IOpenAIService _openAiService;
        private string _apiKey;
        private string _apiBaseUrl; // New field for API Base URL
        private string _modelName;  // New field for Model Name
        private const string PlaceholderApiKey = "YOUR_API_KEY_GOES_HERE"; 
        private const string DefaultModelName = "gpt-3.5-turbo"; // Default model

        // Prompts remain the same
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

        private const string GoalDecompositionSystemPrompt = @"
      You are an expert goal planning assistant. Your task is to take a user's long-term goal and a specified duration, and break it down into a series of smaller, actionable daily tasks. For each task, you must also categorize it into one of four quadrants based on its importance and urgency, and provide an estimated time for completion.

      The four quadrants are:
      1.  ""Important & Urgent""
      2.  ""Important & Not Urgent""
      3.  ""Not Important & Urgent""
      4.  ""Not Important & Not Urgent""

      The user will provide the goal and duration. You need to generate a plan of daily (or near-daily) tasks that will help the user achieve their goal within the given timeframe.

      Respond with a JSON array of task objects. Each object should have the following fields:
      -   ""task_description"": A string describing the task.
      -   ""quadrant"": A string representing one of the four quadrant categories (e.g., ""Important & Urgent"").
      -   ""estimated_time"": A string describing the estimated time to complete the task (e.g., ""1 hour"", ""30 minutes"").
      -   ""day"": An integer representing the day number in the plan (e.g., 1, 2, 3...). This is relative to the start of the plan.

      Example Input from User:
      Goal: ""I want to learn Python programming for web development.""
      Duration: ""3 months""

      Example JSON Output:
      [
        {
          ""day"": 1,
          ""task_description"": ""Set up Python development environment (install Python, VS Code, Git)."",
          ""quadrant"": ""Important & Urgent"",
          ""estimated_time"": ""2 hours""
        },
        {
          ""day"": 1,
          ""task_description"": ""Complete Chapter 1 of Python basics tutorial (variables, data types)."",
          ""quadrant"": ""Important & Not Urgent"",
          ""estimated_time"": ""1.5 hours""
        }
      ]

      Ensure the tasks are logically sequenced and contribute towards the main goal. Distribute tasks reasonably across the duration. If the goal is very long-term, you might group tasks by week, but individual tasks should still be daily or completable within a day. Focus on creating a practical and actionable plan.
      User Input:
      Goal: ""{userGoal}""
      Duration: ""{userDuration}""

IMPORTANT: Your entire response MUST be a valid JSON array of task objects, starting with '[' and ending with ']'. Do not include any other text, explanations, or markdown formatting outside of this JSON array."; // Note the {userGoal} and {userDuration} placeholders.
        
        public LlmService()
        {
            LoadLlmConfig(); // Renamed from LoadApiKeyFromConfig
            InitializeOpenAiService();
        }

        // Constructor for dependency injection, typically for testing
        internal LlmService(IOpenAIService openAiService, string apiKey, string apiBaseUrl, string modelName)
        {
            _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _apiBaseUrl = apiBaseUrl; // Can be null
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));

            // Ensure critical fields are not placeholder values if GetCompletionAsync relies on them
            if (_apiKey == PlaceholderApiKey)
            {
                // Or throw, or handle as per service's expectation for injected services.
                Console.WriteLine("Warning: LlmService injected with a placeholder API key.");
            }
            Console.WriteLine($"LlmService: Initialized with injected IOpenAIService. Model: {_modelName}. API Key Loaded: {!string.IsNullOrWhiteSpace(_apiKey) && _apiKey != PlaceholderApiKey}. Base URL: '{_apiBaseUrl ?? "OpenAI Default"}'");
        }

        public async Task<List<ProposedDailyTask>> DecomposeGoalIntoDailyTasksAsync(string goal, string durationString)
        {
            if (string.IsNullOrWhiteSpace(goal) || string.IsNullOrWhiteSpace(durationString))
            {
                Console.WriteLine("Goal or duration string is empty for DecomposeGoalIntoDailyTasksAsync.");
                return new List<ProposedDailyTask>();
            }

            string fullPrompt = GoalDecompositionSystemPrompt
                .Replace("{userGoal}", goal)
                .Replace("{userDuration}", durationString);

            string llmResponse = await GetCompletionAsync(fullPrompt);

            if (string.IsNullOrWhiteSpace(llmResponse) || llmResponse.StartsWith("LLM dummy response") || llmResponse.StartsWith("Error from LLM"))
            {
                Console.WriteLine($"LLM did not provide a valid response for goal decomposition. Goal: '{goal}'. Response: {llmResponse}");
                return new List<ProposedDailyTask>(); // Or throw an exception
            }

            try
            {
                // Clean the response if it's wrapped in markdown ```json ... ```
                llmResponse = llmResponse.Trim();
                if (llmResponse.StartsWith("```json"))
                {
                    llmResponse = llmResponse.Substring(7);
                }
                if (llmResponse.EndsWith("```"))
                {
                    llmResponse = llmResponse.Substring(0, llmResponse.Length - 3);
                }
                llmResponse = llmResponse.Trim();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Handles "task_description" vs "TaskDescription"
                };
                List<ProposedDailyTask> tasks = JsonSerializer.Deserialize<List<ProposedDailyTask>>(llmResponse, options);
                return tasks ?? new List<ProposedDailyTask>();
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"Error parsing JSON response for goal decomposition: {jsonEx.Message}. Response was: {llmResponse}");
                return new List<ProposedDailyTask>(); // Or throw
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred during goal decomposition: {ex.Message}. Response was: {llmResponse}");
                return new List<ProposedDailyTask>(); // Or throw
            }
        }

        internal string FormatTimeSpan(TimeSpan ts)
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
            Console.WriteLine($"Parsing LLM reminder response: \"{llmResponse}\"");
            var suggestions = new List<string>();
            string reminder = string.Empty;

            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                Console.WriteLine("LLM response is null or whitespace. Returning empty reminder and suggestions.");
                return (reminder, suggestions);
            }

            try
            {
                var reminderRegex = new Regex(@"Reminder\s*:\s*(.*)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                var suggestion1Regex = new Regex(@"Suggestion1\s*:\s*(.*)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                var suggestion2Regex = new Regex(@"Suggestion2\s*:\s*(.*)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                var suggestion3Regex = new Regex(@"Suggestion3\s*:\s*(.*)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                var reminderMatch = reminderRegex.Match(llmResponse);
                if (reminderMatch.Success)
                {
                    reminder = reminderMatch.Groups[1].Value.Trim();
                }
                else
                {
                    Console.WriteLine("Could not find 'Reminder:' pattern in LLM response.");
                }

                var suggestion1Match = suggestion1Regex.Match(llmResponse);
                if (suggestion1Match.Success)
                {
                    suggestions.Add(suggestion1Match.Groups[1].Value.Trim());
                }
                else
                {
                    Console.WriteLine("Could not find 'Suggestion1:' pattern in LLM response.");
                }

                var suggestion2Match = suggestion2Regex.Match(llmResponse);
                if (suggestion2Match.Success)
                {
                    suggestions.Add(suggestion2Match.Groups[1].Value.Trim());
                }
                else
                {
                     Console.WriteLine("Could not find 'Suggestion2:' pattern in LLM response.");
                }

                var suggestion3Match = suggestion3Regex.Match(llmResponse);
                if (suggestion3Match.Success)
                {
                    string sug3 = suggestion3Match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(sug3) && !sug3.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add(sug3);
                    }
                }
                // Optional: Log if Suggestion3 is not found, but it's optional so might be too noisy.

                if (string.IsNullOrWhiteSpace(reminder) && !suggestions.Any())
                {
                    Console.WriteLine($"Could not parse any reminder or suggestions from LLM response using regex: '{llmResponse}'.");
                }
                Console.WriteLine($"Parsed Reminder: \"{reminder}\", Suggestions: {suggestions.Count}");
                return (reminder, suggestions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM reminder response with regex: {ex.Message}. Response was: {llmResponse}");
                Console.WriteLine($"Defaulted Reminder/Suggestions due to exception: Reminder='', Suggestions=[]");
                return (string.Empty, new List<string>());
            }
        }
        
        public async Task<(string reminder, List<string> suggestions)> GenerateTaskReminderAsync(string taskDescription, TimeSpan timeSinceLastModified)
        {
            if (string.IsNullOrWhiteSpace(taskDescription)) return (string.Empty, new List<string>());
            string formattedAge = FormatTimeSpan(timeSinceLastModified);
            string fullPrompt = TaskReminderSystemPrompt.Replace("{taskDescription}", taskDescription).Replace("{taskAge}", formattedAge);
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
            Console.WriteLine($"Parsing LLM decomposition response: \"{llmResponse}\"");
            var subtasks = new List<string>();
            DecompositionStatus status = DecompositionStatus.Unknown;

            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                Console.WriteLine("LLM response is null or whitespace. Defaulting to Unknown status and empty subtasks.");
                return (status, subtasks);
            }

            try
            {
                var statusRegex = new Regex(@"Status\s*:\s*(Sufficient|NeedsDecomposition)", RegexOptions.IgnoreCase);
                var statusMatch = statusRegex.Match(llmResponse);

                if (statusMatch.Success)
                {
                    string statusStr = statusMatch.Groups[1].Value;
                    if (!Enum.TryParse(statusStr, true, out status))
                    {
                        status = DecompositionStatus.Unknown;
                        Console.WriteLine($"Could not parse decomposition status value '{statusStr}' from LLM response. Defaulting to Unknown.");
                    }
                }
                else
                {
                    Console.WriteLine($"Could not find 'Status:' for decomposition in LLM response. Defaulting to Unknown.");
                    // No early return, will log final status and subtasks at the end
                }

                if (status == DecompositionStatus.NeedsDecomposition)
                {
                    var subtasksRegex = new Regex(@"Subtasks\s*:\s*((?:.|\n)*)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    var subtasksMatch = subtasksRegex.Match(llmResponse);

                    if (subtasksMatch.Success)
                    {
                        string subtasksBlock = subtasksMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(subtasksBlock) && !subtasksBlock.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] lines = subtasksBlock.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
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
                        }
                        if (!subtasks.Any())
                        {
                            Console.WriteLine($"Decomposition status is NeedsDecomposition but no valid subtasks found or parsed from block: '{subtasksBlock}'.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Decomposition status is NeedsDecomposition but 'Subtasks:' block not found in LLM response.");
                        // Status might remain NeedsDecomposition but subtasks list will be empty.
                    }
                }
                Console.WriteLine($"Parsed Decomposition: Status={status}, Subtasks Count={subtasks.Count}");
                return (status, subtasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM decomposition response with regex: {ex.Message}. Response was: {llmResponse}");
                Console.WriteLine($"Defaulted Decomposition due to exception: Status=Unknown, Subtasks=[]");
                return (DecompositionStatus.Unknown, new List<string>());
            }
        }

        public async Task<(DecompositionStatus status, List<string> subtasks)> DecomposeTaskAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription)) return (DecompositionStatus.Unknown, new List<string>());
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
            Console.WriteLine($"Parsing LLM clarity response: \"{llmResponse}\"");
            ClarityStatus status = ClarityStatus.Unknown;
            string question = string.Empty; // Default to empty string

            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                Console.WriteLine("LLM response is null or whitespace. Defaulting to Unknown status and empty question.");
                question = "LLM response was empty."; // Keep original error message for this specific case
                return (status, question);
            }

            try
            {
                var statusRegex = new Regex(@"Status\s*:\s*(Clear|NeedsClarification)", RegexOptions.IgnoreCase);
                var questionRegex = new Regex(@"Question\s*:\s*((?:.|\n)*)", RegexOptions.IgnoreCase | RegexOptions.Multiline);

                var statusMatch = statusRegex.Match(llmResponse);
                if (statusMatch.Success)
                {
                    string statusStr = statusMatch.Groups[1].Value;
                    if (Enum.TryParse(statusStr, true, out ClarityStatus parsedStatus))
                    {
                        status = parsedStatus;
                    }
                    else
                    {
                        Console.WriteLine($"Could not parse clarity status value '{statusStr}' from LLM response. Defaulting to Unknown.");
                        status = ClarityStatus.Unknown;
                    }
                }
                else
                {
                    Console.WriteLine("Could not find 'Status:' pattern in LLM response. Defaulting to Unknown status.");
                    // No early return, will log final status and question at the end
                }

                var questionMatch = questionRegex.Match(llmResponse);
                if (questionMatch.Success)
                {
                    question = questionMatch.Groups[1].Value.Trim();
                    if (status == ClarityStatus.Clear && (string.IsNullOrWhiteSpace(question) || question.Equals("N/A", StringComparison.OrdinalIgnoreCase)))
                    {
                        question = string.Empty; // Clear question if status is Clear and question is N/A or empty
                    }
                }
                else
                {
                    Console.WriteLine("Could not find 'Question:' pattern in LLM response.");
                    if (status == ClarityStatus.NeedsClarification)
                    {
                        question = "Question expected but not found in response."; // More specific if status indicated a question was expected
                    } else if (status == ClarityStatus.Unknown) {
                        question = "Failed to parse status and question from LLM response.";
                    }
                }

                if (status == ClarityStatus.Unknown && !statusMatch.Success) { // If status is still unknown because regex failed
                     question = "Failed to parse status from LLM response.";
                } else if (status == ClarityStatus.NeedsClarification && string.IsNullOrWhiteSpace(question)) {
                    Console.WriteLine("Warning: Status is NeedsClarification, but question is empty or N/A.");
                    // question = "Clarification needed, but no specific question was parsed."; // Optionally override if needed
                }


                Console.WriteLine($"Parsed Clarity: Status={status}, Question=\"{question}\"");
                return (status, question);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM clarity response with regex: {ex.Message}. Response was: {llmResponse}");
                Console.WriteLine($"Defaulted Clarity due to exception: Status=Unknown, Question='Failed to analyze task clarity due to an exception.'");
                return (ClarityStatus.Unknown, "Failed to analyze task clarity due to an exception.");
            }
        }

        public async Task<(ClarityStatus status, string question)> AnalyzeTaskClarityAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription)) return (ClarityStatus.Unknown, "Task description cannot be empty.");
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
            Console.WriteLine($"Parsing LLM priority response: \"{llmResponse}\"");
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                Console.WriteLine("LLM response is null or whitespace. Defaulting to Unknown/Unknown.");
                return ("Unknown", "Unknown");
            }

            string importance = "Unknown";
            string urgency = "Unknown";

            try
            {
                // Regex to find "Importance: [value]" and "Urgency: [value]", case-insensitive labels, flexible whitespace
                var importanceRegex = new Regex(@"Importance\s*:\s*([A-Za-z]+)", RegexOptions.IgnoreCase);
                var urgencyRegex = new Regex(@"Urgency\s*:\s*([A-Za-z]+)", RegexOptions.IgnoreCase);

                var importanceMatch = importanceRegex.Match(llmResponse);
                var urgencyMatch = urgencyRegex.Match(llmResponse);

                string[] validPriorities = { "High", "Medium", "Low" };
                var validPrioritySet = new HashSet<string>(validPriorities, StringComparer.OrdinalIgnoreCase);

                if (importanceMatch.Success)
                {
                    string extractedImportance = importanceMatch.Groups[1].Value.Trim();
                    if (validPrioritySet.Contains(extractedImportance))
                    {
                        // Normalize to title case e.g. "high" -> "High"
                        importance = validPriorities.First(p => p.Equals(extractedImportance, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        Console.WriteLine($"Extracted importance '{extractedImportance}' is not a valid priority. Defaulting to Unknown.");
                    }
                }
                else
                {
                    Console.WriteLine("Could not find 'Importance:' pattern in LLM response.");
                }

                if (urgencyMatch.Success)
                {
                    string extractedUrgency = urgencyMatch.Groups[1].Value.Trim();
                    if (validPrioritySet.Contains(extractedUrgency))
                    {
                        // Normalize to title case
                        urgency = validPriorities.First(p => p.Equals(extractedUrgency, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        Console.WriteLine($"Extracted urgency '{extractedUrgency}' is not a valid priority. Defaulting to Unknown.");
                    }
                }
                else
                {
                    Console.WriteLine("Could not find 'Urgency:' pattern in LLM response.");
                }

                if (importance == "Unknown" && urgency == "Unknown" && !importanceMatch.Success && !urgencyMatch.Success)
                {
                     Console.WriteLine($"Could not parse Importance or Urgency from LLM response using regex: '{llmResponse}'. Both remain Unknown.");
                }
                Console.WriteLine($"Parsed Priority: Importance={importance}, Urgency={urgency}");
                return (importance, urgency);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing LLM priority response with regex: {ex.Message}. Response was: {llmResponse}");
                Console.WriteLine($"Defaulted Priority due to exception: Importance=Unknown, Urgency=Unknown");
                return ("Unknown", "Unknown");
            }
        }

        public async Task<(string Importance, string Urgency)> GetTaskPriorityAsync(string taskDescription)
        {
            if (string.IsNullOrWhiteSpace(taskDescription)) return ("Unknown", "Unknown");
            string fullPrompt = PrioritizationSystemPrompt + taskDescription;
            string llmResponse = await GetCompletionAsync(fullPrompt);
            if (string.IsNullOrWhiteSpace(llmResponse) || llmResponse.StartsWith("LLM dummy response") || llmResponse.StartsWith("Error from LLM"))
            {
                Console.WriteLine($"LLM did not provide a valid priority response for '{taskDescription}'. Response: {llmResponse}");
                return ("Unknown", "Unknown");
            }
            return ParsePriorityResponse(llmResponse);
        }
        
        private void LoadLlmConfig() // Renamed and updated
        {
            try
            {
                _apiKey = ConfigurationManager.AppSettings["OpenAIApiKey"];
                _apiBaseUrl = ConfigurationManager.AppSettings["LlmApiBaseUrl"]; // Load new setting
                _modelName = ConfigurationManager.AppSettings["LlmModelName"];   // Load new setting
            }
            catch (ConfigurationErrorsException ex)
            {
                Console.WriteLine($"Error reading App.config: {ex.Message}");
                _apiKey = null; 
                _apiBaseUrl = null;
                _modelName = null;
            }

            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == PlaceholderApiKey)
            {
                Console.WriteLine("Warning: LLM Service: API key is placeholder or missing in App.config. LLM features will use dummy responses.");
                _apiKey = PlaceholderApiKey; 
            }

            // _apiBaseUrl can be legitimately empty/null for OpenAI default
            if (string.IsNullOrWhiteSpace(_modelName))
            {
                Console.WriteLine($"Warning: LLM Service: Model name is missing in App.config. Defaulting to '{DefaultModelName}'.");
                _modelName = DefaultModelName;
            }
            else
            {
                // Basic check if model name from config looks like a known OpenAI static model string,
                // otherwise, use it as a custom model string.
                // Example: Models.Gpt_3_5_Turbo is a static string constant.
                // If _modelName is something like "ft:gpt-3.5-turbo:my-org:custom-suffix:id", it's a custom model.
                // The Betalgo library expects the model string directly.
            }
        }

        private void InitializeOpenAiService()
        {
            var options = new Betalgo.Ranul.OpenAI.OpenAIOptions()
            {
                ApiKey = _apiKey
            };

            if (!string.IsNullOrWhiteSpace(_apiBaseUrl))
            {
                options.BaseDomain = _apiBaseUrl; // Set BaseDomain if provided
                Console.WriteLine($"LlmService: Using custom API Base URL: {_apiBaseUrl}");
            }

            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(120)
            };

            _openAiService = new Betalgo.Ranul.OpenAI.Managers.OpenAIService(options, httpClient);
            Console.WriteLine($"LlmService: Initialized OpenAIService with custom HttpClient (120s timeout). Provider: OpenAI (Betalgo.Ranul.OpenAI). Model: {_modelName}.");
        }

        public async Task<string> GetCompletionAsync(string prompt)
        {
            if (_openAiService == null || _apiKey == PlaceholderApiKey || string.IsNullOrWhiteSpace(_apiKey))
            {
                Console.WriteLine("LLM Service not properly initialized. Returning dummy response.");
                if (_apiKey == PlaceholderApiKey || string.IsNullOrWhiteSpace(_apiKey))
                {
                    return await Task.FromResult($"LLM dummy response (Configuration Error: API key missing or placeholder). Prompt: {prompt}");
                }
                return await Task.FromResult($"LLM dummy response for: {prompt}");
            }

            // Added logging for diagnostics
            string endpointPath = "v1/chat/completions"; // Common path for chat completions
            string targetUrl = (_apiBaseUrl != null ? _apiBaseUrl.TrimEnd('/') : "https://api.openai.com") + "/" + endpointPath.TrimStart('/');
            Console.WriteLine($"LLM Request: Target URL: {targetUrl}");
            Console.WriteLine($"LLM Request: Model Name: {_modelName}");
            bool apiKeyLoaded = !string.IsNullOrWhiteSpace(_apiKey) && _apiKey != PlaceholderApiKey;
            string apiKeyFirstChars = apiKeyLoaded && _apiKey.Length >= 5 ? _apiKey.Substring(0, 5) : "N/A";
            Console.WriteLine($"LLM Request: ApiKey Loaded: {apiKeyLoaded}, First 5 chars: {(apiKeyLoaded ? apiKeyFirstChars : "N/A")}");

            try
            {
                var completionResult = await _openAiService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
                {
                    Messages = new List<ChatMessage> 
                    {
                        ChatMessage.FromUser(prompt) 
                    },
                    Model = _modelName, // Use configured model name
                    MaxTokens = 2048
                });

                if (completionResult.Successful)
                {
                    var choice = completionResult.Choices.FirstOrDefault();
                    var content = choice?.Message.Content;
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        Console.WriteLine("LLM API call was successful but returned empty or whitespace content.");
                        Console.WriteLine($"Choice Finish Reason: {choice?.FinishReason}");
                        try
                        {
                            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                            Console.WriteLine($"Full completionResult details: {System.Text.Json.JsonSerializer.Serialize(completionResult, options)}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to serialize completionResult: {ex.Message}");
                        }
                        // Return null or empty to maintain current behavior for downstream checks
                        return content;
                    }
                    return content;
                }
                else // Not successful
                {
                    try // Inner try for parsing the Error object
                    {
                        if (completionResult.Error == null)
                        {
                            Console.WriteLine("LLM API Error: Call was not successful but Error object is null.");
                            return "Error from LLM: Unknown error (null error object).";
                        }
                        // Accessing these properties might trigger JsonException if the Error object itself is malformed
                        string errorMessage = completionResult.Error.Message;
                        string errorCode = completionResult.Error.Code;
                        string errorType = completionResult.Error.Type;
                        Console.WriteLine($"LLM API Error: {errorMessage} (Code: {errorCode}, Type: {errorType})");
                        return $"Error from LLM: {errorMessage}";
                    }
                    catch (System.Text.Json.JsonException jsonExInner)
                    {
                        Console.WriteLine($"Error deserializing the LLM error object. Path: {jsonExInner.Path}, Line: {jsonExInner.LineNumber}, Pos: {jsonExInner.BytePositionInLine}. Details: {jsonExInner.Message}");
                        return $"Error from LLM: Could not parse the error details from response. Details: {jsonExInner.Message}";
                    }
                }
            }
            catch (System.Text.Json.JsonException jsonExOuter) // For issues with the entire response
            {
                Console.WriteLine($"Error deserializing the entire LLM response. Path: {jsonExOuter.Path}, Line: {jsonExOuter.LineNumber}, Pos: {jsonExOuter.BytePositionInLine}. Details: {jsonExOuter.Message}");
                return $"Error from LLM: Could not parse the entire response. Details: {jsonExOuter.Message}";
            }
            catch (Exception ex) // General catch-all
            {
                Console.WriteLine($"Exception during LLM call: {ex.ToString()}"); // Use ToString()
                return await Task.FromResult($"LLM dummy response (due to exception: {ex.Message}) for: {prompt}"); // Keep original return style
            }
        }

        public static LlmService Create()
        {
            return new LlmService();
        }

        public void ReloadConfigAndReinitialize()
        {
            Console.WriteLine("LlmService: Reloading configuration and re-initializing...");
            LoadLlmConfig();         // Reloads API key, base URL, and model name from App.config
            InitializeOpenAiService(); // Re-creates the OpenAIService instance with new settings
            Console.WriteLine("LlmService: Re-initialization complete.");
        }
    }
}
