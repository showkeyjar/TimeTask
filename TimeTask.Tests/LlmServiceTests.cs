using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using TimeTask; // Reference to the main project namespace

namespace TimeTask.Tests
{
    [TestClass]
    public class LlmServiceTests
    {
        private LlmService _llmServiceInstanceForFormatTimeSpan; // Only needed for instance methods like FormatTimeSpan

        [TestInitialize]
        public void TestInitialize()
        {
            // For testing FormatTimeSpan (if it were not static)
            _llmServiceInstanceForFormatTimeSpan = LlmService.Create(); 
        }

        [TestMethod]
        public void FormatTimeSpan_LessThanHour_ReturnsLessThanHour()
        {
            var result = _llmServiceInstanceForFormatTimeSpan.FormatTimeSpan(TimeSpan.FromMinutes(30));
            Assert.AreEqual("less than an hour old", result);
        }

        [TestMethod]
        public void FormatTimeSpan_SingleHour_ReturnsHourOld()
        {
            var result = _llmServiceInstanceForFormatTimeSpan.FormatTimeSpan(TimeSpan.FromHours(1));
            Assert.AreEqual("1 hour old", result);
        }

        [TestMethod]
        public void FormatTimeSpan_MultipleHours_ReturnsHoursOld()
        {
            var result = _llmServiceInstanceForFormatTimeSpan.FormatTimeSpan(TimeSpan.FromHours(5));
            Assert.AreEqual("5 hours old", result);
        }

        [TestMethod]
        public void FormatTimeSpan_SingleDay_ReturnsDayOld()
        {
            var result = _llmServiceInstanceForFormatTimeSpan.FormatTimeSpan(TimeSpan.FromDays(1));
            Assert.AreEqual("1 day old", result);
        }

        [TestMethod]
        public void FormatTimeSpan_MultipleDays_ReturnsDaysOld()
        {
            var result = _llmServiceInstanceForFormatTimeSpan.FormatTimeSpan(TimeSpan.FromDays(3));
            Assert.AreEqual("3 days old", result);
        }

        [TestMethod]
        public void FormatTimeSpan_SingleWeek_ReturnsWeekOld()
        {
            var result = _llmServiceInstanceForFormatTimeSpan.FormatTimeSpan(TimeSpan.FromDays(7));
            Assert.AreEqual("1 week old", result);
        }

        [TestMethod]
        public void FormatTimeSpan_MultipleWeeks_ReturnsWeeksOld()
        {
            var result = _llmServiceInstanceForFormatTimeSpan.FormatTimeSpan(TimeSpan.FromDays(21));
            Assert.AreEqual("3 weeks old", result);
        }

        [TestMethod]
        public void FormatTimeSpan_MixedDaysAndWeeks_ReturnsWeeksOld()
        {
            var result = _llmServiceInstanceForFormatTimeSpan.FormatTimeSpan(TimeSpan.FromDays(17)); // 2 weeks and 3 days
            Assert.AreEqual("2 weeks old", result); // Current logic truncates to weeks
        }

        // --- Tests for GetTaskPriorityAsync Parsing ---
        [TestMethod]
        public void ParsePriorityResponse_ValidInput_ParsesCorrectly()
        {
            string llmResponse = "Importance: High, Urgency: Medium";
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("High", importance);
            Assert.AreEqual("Medium", urgency);
        }

        [TestMethod]
        public void ParsePriorityResponse_CaseVariations_ParsesCorrectly()
        {
            string llmResponse = "importance: low, urgency: high";
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("low", importance);
            Assert.AreEqual("high", urgency);
        }
        
        [TestMethod]
        public void ParsePriorityResponse_ExtraSpaces_ParsesCorrectly()
        {
            string llmResponse = "  Importance:  High  ,  Urgency:  Medium  ";
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("High", importance);
            Assert.AreEqual("Medium", urgency);
        }

        [TestMethod]
        public void ParsePriorityResponse_MalformedResponse_ReturnsUnknown()
        {
            string llmResponse = "Importance High Urgency Medium"; // Missing colon and comma
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("Unknown", importance);
            Assert.AreEqual("Unknown", urgency);
        }

        [TestMethod]
        public void ParsePriorityResponse_MissingUrgencyField_ReturnsUnknownForUrgency()
        {
            string llmResponse = "Importance: High";
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("High", importance);
            Assert.AreEqual("Unknown", urgency);
        }
        
        [TestMethod]
        public void ParsePriorityResponse_MissingImportanceField_ReturnsUnknownForImportance()
        {
            string llmResponse = "Urgency: Low";
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("Unknown", importance);
            Assert.AreEqual("Low", urgency);
        }

        [TestMethod]
        public void ParsePriorityResponse_EmptyString_ReturnsUnknown()
        {
            var (importance, urgency) = LlmService.ParsePriorityResponse(string.Empty);
            Assert.AreEqual("Unknown", importance);
            Assert.AreEqual("Unknown", urgency);
        }

        [TestMethod]
        public void ParsePriorityResponse_NullString_ReturnsUnknown()
        {
            var (importance, urgency) = LlmService.ParsePriorityResponse(null);
            Assert.AreEqual("Unknown", importance);
            Assert.AreEqual("Unknown", urgency);
        }

        [TestMethod]
        public void ParsePriorityResponse_InvalidValues_ReturnsUnknown()
        {
            string llmResponse = "Importance: Critical, Urgency: ASAP";
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("Unknown", importance);
            Assert.AreEqual("Unknown", urgency);
        }

        // --- Tests for AnalyzeTaskClarityAsync Parsing ---
        [TestMethod]
        public void ParseClarityResponse_ValidClear_ParsesCorrectly()
        {
            string llmResponse = "Status: Clear\nQuestion: N/A";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.Clear, status);
            Assert.AreEqual(string.Empty, question); // N/A should become empty
        }

        [TestMethod]
        public void ParseClarityResponse_ValidNeedsClarification_ParsesCorrectly()
        {
            string llmResponse = "Status: NeedsClarification\nQuestion: What is the deadline?";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.NeedsClarification, status);
            Assert.AreEqual("What is the deadline?", question);
        }
        
        [TestMethod]
        public void ParseClarityResponse_MalformedResponse_ReturnsUnknown()
        {
            string llmResponse = "Status NeedsClarification Question What is the deadline?";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.Unknown, status); // Status parsing fails
            Assert.IsTrue(question.Contains("Failed to parse") || question.Contains("Question not found"));
        }

        [TestMethod]
        public void ParseClarityResponse_StatusOnly_ParsesStatus()
        {
            string llmResponse = "Status: Clear";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.Clear, status);
            Assert.IsTrue(question.Contains("Question not found"));
        }
        
        [TestMethod]
        public void ParseClarityResponse_EmptyString_ReturnsUnknown()
        {
            var (status, question) = LlmService.ParseClarityResponse(string.Empty);
            Assert.AreEqual(ClarityStatus.Unknown, status);
            Assert.AreEqual("LLM response was empty.", question);
        }

        // --- Tests for DecomposeTaskAsync Parsing ---
        [TestMethod]
        public void ParseDecompositionResponse_ValidNeedsDecomposition_ParsesCorrectly()
        {
            string llmResponse = "Status: NeedsDecomposition\nSubtasks:\n- Subtask 1\n* Subtask 2\n  Another Subtask";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.NeedsDecomposition, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(3, subtasks.Count);
            Assert.AreEqual("Subtask 1", subtasks[0]);
            Assert.AreEqual("Subtask 2", subtasks[1]);
            Assert.AreEqual("Another Subtask", subtasks[2]);
        }

        [TestMethod]
        public void ParseDecompositionResponse_ValidSufficient_ParsesCorrectly()
        {
            string llmResponse = "Status: Sufficient\nSubtasks: N/A";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.Sufficient, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count);
        }
        
        [TestMethod]
        public void ParseDecompositionResponse_MalformedResponse_ReturnsUnknown()
        {
            string llmResponse = "Status NeedsDecomposition Subtasks - Subtask1";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.Unknown, status); // Status parsing fails
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count);
        }

        [TestMethod]
        public void ParseDecompositionResponse_StatusOnly_ReturnsStatusAndNoSubtasks()
        {
            string llmResponse = "Status: Sufficient";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.Sufficient, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count); // No "Subtasks:" line
        }

        [TestMethod]
        public void ParseDecompositionResponse_NeedsDecompositionNoSubtasksHeader_ReturnsUnknown()
        {
            string llmResponse = "Status: NeedsDecomposition\nActual Subtask 1"; // Missing "Subtasks:" header
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.Unknown, status); // Fails to find subtasks header
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count);
        }
        
        [TestMethod]
        public void ParseDecompositionResponse_NeedsDecompositionEmptySubtasks_ReturnsEmptyList()
        {
            string llmResponse = "Status: NeedsDecomposition\nSubtasks:\n   \n"; // Empty subtasks
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.NeedsDecomposition, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count);
        }

        // --- Tests for GenerateTaskReminderAsync Parsing ---
        [TestMethod]
        public void ParseReminderResponse_ValidInput_ParsesCorrectly()
        {
            string llmResponse = "Reminder: Time to check this!\nSuggestion1: Do it.\nSuggestion2: Plan it.\nSuggestion3: Delegate it.";
            var (reminder, suggestions) = LlmService.ParseReminderResponse(llmResponse);
            Assert.AreEqual("Time to check this!", reminder);
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(3, suggestions.Count);
            Assert.AreEqual("Do it.", suggestions[0]);
            Assert.AreEqual("Plan it.", suggestions[1]);
            Assert.AreEqual("Delegate it.", suggestions[2]);
        }
        
        [TestMethod]
        public void ParseReminderResponse_ReminderOnly_ParsesReminder()
        {
            string llmResponse = "Reminder: Just a friendly poke!";
            var (reminder, suggestions) = LlmService.ParseReminderResponse(llmResponse);
            Assert.AreEqual("Just a friendly poke!", reminder);
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(0, suggestions.Count);
        }

        [TestMethod]
        public void ParseReminderResponse_SuggestionsOnly_ParsesSuggestions()
        {
            string llmResponse = "Suggestion1: Think about it.\nSuggestion2: Write it down.";
            var (reminder, suggestions) = LlmService.ParseReminderResponse(llmResponse);
            Assert.IsTrue(string.IsNullOrEmpty(reminder));
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(2, suggestions.Count);
            Assert.AreEqual("Think about it.", suggestions[0]);
            Assert.AreEqual("Write it down.", suggestions[1]);
        }
        
        [TestMethod]
        public void ParseReminderResponse_MalformedResponse_ReturnsEmpty()
        {
            string llmResponse = "This is not the expected format.";
            var (reminder, suggestions) = LlmService.ParseReminderResponse(llmResponse);
            Assert.IsTrue(string.IsNullOrEmpty(reminder));
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(0, suggestions.Count);
        }

        [TestMethod]
        public void ParseReminderResponse_EmptyString_ReturnsEmpty()
        {
            var (reminder, suggestions) = LlmService.ParseReminderResponse(string.Empty);
            Assert.IsTrue(string.IsNullOrEmpty(reminder));
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(0, suggestions.Count);
        }
    }
}
