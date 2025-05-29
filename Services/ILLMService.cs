using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TimeTask.Services
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

    public interface ILLMService : IDisposable
    {
        Task<int> ClassifyTaskAsync(string taskDescription);
        Task<string> AnalyzeTaskAsync(string taskDescription);
        Task<(string reminder, List<string> suggestions)> GenerateTaskReminderAsync(string taskDescription, TimeSpan timeSinceLastModified);
        Task<(DecompositionStatus status, List<string> subtasks)> DecomposeTaskAsync(string taskDescription);
        Task<(ClarityStatus status, string question)> AnalyzeTaskClarityAsync(string taskDescription);
        Task<(string Importance, string Urgency)> GetTaskPriorityAsync(string taskDescription);
    }
}
