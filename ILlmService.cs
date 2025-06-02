using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TimeTask
{
    // ClarityStatus and DecompositionStatus enums are expected to be in the TimeTask namespace,
    // typically defined in LlmService.cs or their own files.

    public interface ILlmService
    {
        Task<(string Importance, string Urgency)> GetTaskPriorityAsync(string taskDescription);

        Task<(ClarityStatus status, string question)> AnalyzeTaskClarityAsync(string taskDescription);

        Task<(DecompositionStatus status, List<string> subtasks)> DecomposeTaskAsync(string taskDescription);

        Task<(string reminder, List<string> suggestions)> GenerateTaskReminderAsync(string taskDescription, TimeSpan timeSinceLastModified);

        Task<string> GetCompletionAsync(string prompt); // General purpose completion method
    }
}
