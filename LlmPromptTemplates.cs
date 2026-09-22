using System;

namespace TimeTask
{
    /// <summary>
    /// LLM 提示词模板（纯数据）。从 LlmService 拆出：模板与服务逻辑分离，
    /// 便于统一审阅措辞、做 A/B 对比与后续本地化。占位符用 {name} 形式，由调用方 Replace。
    /// </summary>
    internal static class LlmPromptTemplates
    {
        // Prompts remain the same
        internal const string PrioritizationSystemPrompt = 
            "Analyze the following task description and determine its importance and urgency. " +
            "Return your answer strictly in the format: \"Importance: [High/Medium/Low], Urgency: [High/Medium/Low]\". " +
            "Do not add any other text, explanations, or elaborations. Just the single line in the specified format.\n" +
            "For example, if the task is 'Fix critical login bug', you should respond with: \"Importance: High, Urgency: High\".\n" +
            "Task: ";

        internal const string ClarityAnalysisSystemPrompt =
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

        internal const string TaskDecompositionSystemPrompt =
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

        internal const string TaskReminderSystemPrompt =
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

        internal const string ReminderProfileHintTemplate =
            "\n\nUser behavior context (local profile, use as soft guidance only):\n{userContext}\n" +
            "Adapt tone and suggestions to reduce interruption. Keep one clear next step.";

        internal const string SkillRecommendationSystemPromptTemplate =
            "You are a task execution copilot. Based on the task and context, recommend 1-3 skills that best help the user move forward now. " +
            "Allowed skill_id values ONLY: {allowedSkillIds}. " +
            "Return ONLY a valid JSON array. Each item must contain: " +
            "\"skill_id\", \"title\", \"why\", \"next_step\", \"confidence\" (0 to 1). " +
            "Keep title/why/next_step concise and actionable.\n" +
            "Task: {taskDescription}\n" +
            "Importance: {importance}\n" +
            "Urgency: {urgency}\n" +
            "InactiveDuration: {inactiveDuration}\n" +
            "UserContext: {userContext}";

        internal const string ConversationTaskExtractPrompt =
            "You are an assistant helping a user capture personal action items from a multi-speaker conversation. " +
            "Extract ONLY tasks that the user should do. " +
            "Return a JSON array of strings. Do not include any extra text.\n" +
            "Conversation:\n";

        internal const string ImportTaskParsePrompt = @"
You are an assistant helping extract personal tasks from project plan text.
Return ONLY a JSON object with fields: ""title"", ""owner"", ""start_date"", ""confidence"".
- ""title"": concise task title.
- ""owner"": person responsible, or empty string if not mentioned.
- ""start_date"": yyyy-MM-dd if found, else empty string.
- ""confidence"": 0 to 1.

Raw Text:
{raw}

Context:
{context}

Return only JSON, no extra text.";

        internal const string GoalDecompositionSystemPrompt = @"
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

      Ensure the tasks are logically sequenced and contribute towards the main goal. Distribute tasks reasonably across the duration. For this request, please provide a detailed daily task plan for the **first 2 weeks** only, based on the user's goal of '{userGoal}' (total duration '{userDuration}'). This 2-week plan should be very detailed.
      User Input:
      Goal: ""{userGoal}""
      Duration: ""{userDuration}""

IMPORTANT: Your entire response MUST be a valid JSON array of task objects for the first 2 weeks, starting with '[' and ending with ']'. Do not include any other text, explanations, or markdown formatting outside of this JSON array. Be direct in your JSON output."; // Note the {userGoal} and {userDuration} placeholders.

        internal const string LearningPlanDecompositionSystemPrompt = @"
      You are an expert learning plan assistant. Your task is to take a user's learning subject and goal, along with a specified duration, and break it down into a series of progressive learning milestones. For each milestone, you must provide a title, description, and estimated time for completion.

      The user will provide the subject, goal, and duration. You need to generate a structured learning plan with milestones that will help the user achieve their learning goal within the given timeframe.

      Respond with a JSON array of milestone objects. Each object should have the following fields:
      -   ""stage"": An integer representing the stage number in the learning plan (e.g., 1, 2, 3...).
      -   ""title"": A string title for the milestone.
      -   ""description"": A string describing what will be learned in this milestone.
      -   ""estimated_time"": A string describing the estimated time to complete this milestone (e.g., ""2 weeks"", ""1 month"").
      -   ""is_completed"": A boolean indicating completion status (always false for new plans).

      Example Input from User:
      Subject: ""Python Programming""
      Goal: ""Learn Python for web development""
      Duration: ""3 months""

      Example JSON Output:
      [
        {
          ""stage"": 1,
          ""title"": ""Python Fundamentals"",
          ""description"": ""Master Python basics including variables, data types, control flow, functions, and object-oriented programming concepts."",
          ""estimated_time"": ""3 weeks"",
          ""is_completed"": false
        },
        {
          ""stage"": 2,
          ""title"": ""Web Development with Flask"",
          ""description"": ""Learn Flask framework, routing, templates, and building RESTful APIs."",
          ""estimated_time"": ""4 weeks"",
          ""is_completed"": false
        }
      ]

      Ensure the milestones are logically sequenced and progressively build upon each other. Distribute milestones reasonably across the duration. For this request, please provide a comprehensive learning plan for the subject '{subject}' with the goal '{goal}' (total duration '{duration}').

      User Input:
      Subject: ""{subject}""
      Goal: ""{goal}""
      Duration: ""{duration}""

IMPORTANT: Your entire response MUST be a valid JSON array of milestone objects, starting with '[' and ending with ']'. Do not include any other text, explanations, or markdown formatting outside of this JSON array. Be direct in your JSON output."; // Note the {subject}, {goal}, and {duration} placeholders.
    }
}
