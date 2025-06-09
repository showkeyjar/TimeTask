using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks; // For Task
using Betalgo.Ranul.OpenAI.Interfaces; // For IOpenAIService
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels; // For ChatCompletionCreateRequest
using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels; // For ChatCompletionCreateResponse, ChatChoiceResponse
using Betalgo.Ranul.OpenAI.ObjectModels.SharedModels; // For ResponseMessage
using System.Collections.Generic; // For List
using System.Text.Json; // For JsonException and JsonSerializer
using System.Net.Http; // Required by IOpenAIService
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
            // Regex parsing normalizes to Title Case for valid values
            string llmResponse = "importance: low, urgency: high";
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("Low", importance, "Importance should be normalized to 'Low'");
            Assert.AreEqual("High", urgency, "Urgency should be normalized to 'High'");
        }
        
        [TestMethod]
        public void ParsePriorityResponse_ExtraSpacesAndDifferentOrder_ParsesCorrectly()
        {
            // Order doesn't matter for regex, and spaces should be handled
            string llmResponse = "  Urgency:  Medium   Importance:  High  ";
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
        public void ParsePriorityResponse_MissingUrgencyValue_ReturnsUnknownForUrgency()
        {
            string llmResponse = "Importance: High, Urgency:";
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("High", importance);
            Assert.AreEqual("Unknown", urgency);
        }
        
        [TestMethod]
        public void ParsePriorityResponse_MissingImportanceLabel_ReturnsUnknownForImportance()
        {
            string llmResponse = "High, Urgency: Low";
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

        [TestMethod]
        public void ParsePriorityResponse_MediumImportanceMediumUrgency_ParsesCorrectly()
        {
            string llmResponse = "Importance: Medium, Urgency: Medium";
            var (importance, urgency) = LlmService.ParsePriorityResponse(llmResponse);
            Assert.AreEqual("Medium", importance);
            Assert.AreEqual("Medium", urgency);
        }

        // --- Tests for AnalyzeTaskClarityAsync Parsing ---
        [TestMethod]
        public void ParseClarityResponse_ValidClear_ParsesCorrectly()
        {
            string llmResponse = "Status: Clear\nQuestion: N/A";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.Clear, status);
            Assert.AreEqual(string.Empty, question, "Question should be empty for Clear status with N/A.");
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
        public void ParseClarityResponse_ValidNeedsClarificationMultiLineQuestion_ParsesCorrectly()
        {
            string llmResponse = "Status: NeedsClarification\nQuestion: What is the deadline?\nAnd who is responsible?";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.NeedsClarification, status);
            Assert.AreEqual("What is the deadline?\nAnd who is responsible?", question.Trim());
        }
        
        [TestMethod]
        public void ParseClarityResponse_MalformedStatus_ReturnsUnknown()
        {
            string llmResponse = "Stat: NeedsClarification\nQuestion: What is the deadline?";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.Unknown, status, "Status should be Unknown due to malformed label.");
            // Question might be parsed or not depending on how strict regex is, but status is key.
            // Current regex for question is independent, so it might parse.
            Assert.AreEqual("What is the deadline?", question);
        }

        [TestMethod]
        public void ParseClarityResponse_MalformedQuestionLabel_ReturnsStatusAndEmptyQuestionOrGenericError()
        {
            string llmResponse = "Status: Clear\nQestion: N/A"; // Misspelled "Question"
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.Clear, status);
            Assert.IsTrue(string.IsNullOrEmpty(question) || question.Contains("Question expected but not found") || question.Contains("Failed to parse status and question"), "Question should be empty or indicate not found.");
        }


        [TestMethod]
        public void ParseClarityResponse_StatusOnly_Clear_ParsesStatusAndEmptyQuestion()
        {
            string llmResponse = "Status: Clear";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.Clear, status);
            Assert.IsTrue(string.IsNullOrEmpty(question) || question.Contains("Question not found"), "Question should be empty or indicate not found.");
        }

        [TestMethod]
        public void ParseClarityResponse_StatusOnly_NeedsClarification_SetsQuestionToIndicateNotFound()
        {
            string llmResponse = "Status: NeedsClarification";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.NeedsClarification, status);
            Assert.IsTrue(question.Contains("Question expected but not found"), "Question should indicate it was expected but not found.");
        }
        
        [TestMethod]
        public void ParseClarityResponse_EmptyString_ReturnsUnknown()
        {
            var (status, question) = LlmService.ParseClarityResponse(string.Empty);
            Assert.AreEqual(ClarityStatus.Unknown, status);
            Assert.AreEqual("LLM response was empty.", question);
        }

        [TestMethod]
        public void ParseClarityResponse_NeedsClarificationMissingQuestionValue_ReturnsNeedsClarificationAndIndicatesNotFound()
        {
            string llmResponse = "Status: NeedsClarification\nQuestion:";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.NeedsClarification, status);
            Assert.IsTrue(string.IsNullOrWhiteSpace(question) || question.Contains("Question expected but not found"), "Question should be empty or indicate it was expected but not found.");
        }

        [TestMethod]
        public void ParseClarityResponse_NeedsClarificationQuestionNA_ReturnsNeedsClarificationAndEmptyQuestion()
        {
            string llmResponse = "Status: NeedsClarification\nQuestion: N/A";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.NeedsClarification, status);
            // If status is NeedsClarification, N/A for question might mean the LLM failed.
            // The new parser logic keeps "N/A" if status is NeedsClarification. This test reflects that.
            // If "N/A" should be string.Empty here, the core parsing logic needs adjustment.
            Assert.AreEqual("N/A", question, "Question should be 'N/A' as per current parsing logic for NeedsClarification.");
        }


        [TestMethod]
        public void ParseClarityResponse_WhitespaceVariations_ParsesCorrectly()
        {
            string llmResponse = "  Status:   NeedsClarification  \n  Question:   What is the main goal?  ";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.NeedsClarification, status);
            Assert.AreEqual("What is the main goal?", question.Trim());
        }

        [TestMethod]
        public void ParseClarityResponse_CaseInsensitiveLabels_ParsesCorrectly()
        {
            string llmResponse = "sTaTuS: clear\nqUeStIoN: n/a";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.Clear, status);
            Assert.AreEqual(string.Empty, question);
        }

        [TestMethod]
        public void ParseClarityResponse_InvalidStatusValue_ReturnsUnknown()
        {
            string llmResponse = "Status: Maybe\nQuestion: Some question";
            var (status, question) = LlmService.ParseClarityResponse(llmResponse);
            Assert.AreEqual(ClarityStatus.Unknown, status);
            Assert.AreEqual("Some question", question); // Question is still parsed
        }

        // --- Tests for DecomposeTaskAsync Parsing ---
        [TestMethod]
        public void ParseDecompositionResponse_ValidNeedsDecomposition_ParsesCorrectly()
        {
            string llmResponse = "Status: NeedsDecomposition\nSubtasks:\n- Subtask 1\n* Subtask 2\n  Another Subtask\n    * Indented Subtask";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.NeedsDecomposition, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(4, subtasks.Count);
            Assert.AreEqual("Subtask 1", subtasks[0]);
            Assert.AreEqual("Subtask 2", subtasks[1]);
            Assert.AreEqual("Another Subtask", subtasks[2]);
            Assert.AreEqual("Indented Subtask", subtasks[3]);
        }

        [TestMethod]
        public void ParseDecompositionResponse_ValidSufficient_ParsesCorrectly()
        {
            string llmResponse = "Status: Sufficient\nSubtasks: N/A";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.Sufficient, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count, "Subtasks should be empty for Sufficient status with N/A.");
        }
        
        [TestMethod]
        public void ParseDecompositionResponse_MalformedStatusLabel_ReturnsUnknown()
        {
            string llmResponse = "Stat: NeedsDecomposition\nSubtasks:\n- Subtask1";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.Unknown, status, "Status should be Unknown due to malformed label.");
            // Subtasks might still be parsed if the "Subtasks:" label is correct.
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(1, subtasks.Count, "Subtasks should be parsed if label is correct.");
            Assert.AreEqual("Subtask1", subtasks[0]);
        }

        [TestMethod]
        public void ParseDecompositionResponse_MalformedSubtasksLabel_ReturnsStatusAndEmptySubtasks()
        {
            string llmResponse = "Status: NeedsDecomposition\nSubtask:\n- Subtask1"; // Misspelled "Subtasks"
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.NeedsDecomposition, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count, "Subtasks should be empty due to malformed label.");
        }

        [TestMethod]
        public void ParseDecompositionResponse_StatusOnly_Sufficient_ReturnsStatusAndNoSubtasks()
        {
            string llmResponse = "Status: Sufficient";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.Sufficient, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count);
        }

        [TestMethod]
        public void ParseDecompositionResponse_StatusOnly_NeedsDecomposition_ReturnsStatusAndNoSubtasks()
        {
            string llmResponse = "Status: NeedsDecomposition";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.NeedsDecomposition, status); // Status is parsed
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count, "Subtasks should be empty as Subtasks block is missing.");
        }

        [TestMethod]
        public void ParseDecompositionResponse_NeedsDecompositionNoSubtasksHeader_ReturnsNeedsDecompositionAndEmptySubtasks()
        {
            string llmResponse = "Status: NeedsDecomposition\nActual Subtask 1"; // Missing "Subtasks:" header
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.NeedsDecomposition, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count, "Subtasks should be empty as Subtasks block is missing.");
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

        [TestMethod]
        public void ParseDecompositionResponse_NeedsDecompositionSubtasksNA_ReturnsNeedsDecompositionAndEmptyList()
        {
            // New regex logic should treat "N/A" under Subtasks as no actual subtasks.
            string llmResponse = "Status: NeedsDecomposition\nSubtasks: N/A";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.NeedsDecomposition, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(0, subtasks.Count, "Subtasks should be empty when 'N/A' is provided.");
        }

        [TestMethod]
        public void ParseDecompositionResponse_NeedsDecompositionSubtasksMultiLineAndMixedPrefixes_ParsesCorrectly()
        {
            string llmResponse = "sTaTuS: needsdecomposition\n\nSuBtAsKs: \n - Task A \n * Task B \n Task C (no prefix)\n\n - Task D with extra space";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.NeedsDecomposition, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(4, subtasks.Count);
            Assert.AreEqual("Task A", subtasks[0]);
            Assert.AreEqual("Task B", subtasks[1]);
            Assert.AreEqual("Task C (no prefix)", subtasks[2]);
            Assert.AreEqual("Task D with extra space", subtasks[3]);
        }

        [TestMethod]
        public void ParseDecompositionResponse_InvalidStatusValue_ReturnsUnknownAndParsesSubtasks()
        {
            string llmResponse = "Status: Maybe\nSubtasks:\n- Subtask 1";
            var (status, subtasks) = LlmService.ParseDecompositionResponse(llmResponse);
            Assert.AreEqual(DecompositionStatus.Unknown, status);
            Assert.IsNotNull(subtasks);
            Assert.AreEqual(1, subtasks.Count); // Subtasks are still parsed
            Assert.AreEqual("Subtask 1", subtasks[0]);
        }

        // --- Tests for GenerateTaskReminderAsync Parsing ---
        [TestMethod]
        public void ParseReminderResponse_ValidInputAllFields_ParsesCorrectly()
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
        public void ParseReminderResponse_SuggestionsOnlyNoReminder_ParsesSuggestions()
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
        public void ParseReminderResponse_MalformedLabels_ReturnsEmptyOrPartiallyParsed()
        {
            string llmResponse = "Remindr: This is not the expected format.\nSgestion1: Test";
            var (reminder, suggestions) = LlmService.ParseReminderResponse(llmResponse);
            Assert.IsTrue(string.IsNullOrEmpty(reminder), "Reminder should be empty due to misspelled label.");
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(0, suggestions.Count, "Suggestions should be empty due to misspelled label.");
        }

        [TestMethod]
        public void ParseReminderResponse_EmptyString_ReturnsEmpty()
        {
            var (reminder, suggestions) = LlmService.ParseReminderResponse(string.Empty);
            Assert.IsTrue(string.IsNullOrEmpty(reminder));
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(0, suggestions.Count);
        }

        [TestMethod]
        public void ParseReminderResponse_CaseAndWhitespaceVariations_ParsesCorrectly()
        {
            string llmResponse = "  rEmInDeR:   The main reminder.  \n  sUgGeStIoN1:   First suggestion. \n SUGGESTION2: Second one. ";
            var (reminder, suggestions) = LlmService.ParseReminderResponse(llmResponse);
            Assert.AreEqual("The main reminder.", reminder.Trim());
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(2, suggestions.Count);
            Assert.AreEqual("First suggestion.", suggestions[0].Trim());
            Assert.AreEqual("Second one.", suggestions[1].Trim());
        }

        [TestMethod]
        public void ParseReminderResponse_MultiLineReminderAndSuggestions_ParsesCorrectly()
        {
            string llmResponse = "Reminder: This is a reminder\nthat spans multiple lines.\nSuggestion1: This is suggestion 1\nalso on multiple lines.\nSuggestion2: S2";
            var (reminder, suggestions) = LlmService.ParseReminderResponse(llmResponse);
            Assert.AreEqual("This is a reminder\nthat spans multiple lines.", reminder.Trim());
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(2, suggestions.Count);
            Assert.AreEqual("This is suggestion 1\nalso on multiple lines.", suggestions[0].Trim());
            Assert.AreEqual("S2", suggestions[1].Trim());
        }

        [TestMethod]
        public void ParseReminderResponse_Suggestion3Missing_ParsesUpToSuggestion2()
        {
            string llmResponse = "Reminder: Check this.\nSuggestion1: Action A.\nSuggestion2: Action B.";
            var (reminder, suggestions) = LlmService.ParseReminderResponse(llmResponse);
            Assert.AreEqual("Check this.", reminder);
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(2, suggestions.Count);
            Assert.AreEqual("Action A.", suggestions[0]);
            Assert.AreEqual("Action B.", suggestions[1]);
        }

        [TestMethod]
        public void ParseReminderResponse_Suggestion3IsNA_Suggestion3NotAdded()
        {
            string llmResponse = "Reminder: Reminder text.\nSuggestion1: Sug 1.\nSuggestion2: Sug 2.\nSuggestion3: N/A";
            var (reminder, suggestions) = LlmService.ParseReminderResponse(llmResponse);
            Assert.AreEqual("Reminder text.", reminder);
            Assert.IsNotNull(suggestions);
            Assert.AreEqual(2, suggestions.Count, "Suggestion3 with N/A should not be added.");
        }

        [TestMethod]
        public async Task GetCompletionAsync_WhenApiThrowsJsonException_ReturnsErrorStringAndLogs()
        {
            // Arrange
            var mockOpenAiService = new MockOpenAIServiceForJsonException(throwJsonExceptionOnCreateCompletion: true);
            // Use non-placeholder values for API key and a valid model name for the LlmService instance
            var llmService = new LlmService(mockOpenAiService, "VALID_API_KEY_FOR_TEST", "http://localhost/v1", "gpt-test-model");
            string prompt = "Test prompt for JsonException scenario";

            // Act
            string result = await llmService.GetCompletionAsync(prompt);

            // Assert
            Assert.IsNotNull(result, "Result should not be null.");
            // Check that the result string indicates a parsing error as per the implemented catch block
            Assert.IsTrue(result.Contains("Error from LLM: Could not parse the entire response."), $"Unexpected result format: {result}");
            Assert.IsTrue(result.Contains("Details:"), $"Result should contain details of JsonException: {result}");
        }
    }

    // Mock classes defined within the TimeTask.Tests namespace
    public class MockOpenAIServiceForJsonException : IOpenAIService
    {
        private readonly bool _throwJsonExceptionOnCreateCompletion;

        public MockOpenAIServiceForJsonException(bool throwJsonExceptionOnCreateCompletion)
        {
            _throwJsonExceptionOnCreateCompletion = throwJsonExceptionOnCreateCompletion;
            ChatCompletion = new MockChatCompletionForJsonException(_throwJsonExceptionOnCreateCompletion);
        }

        public IChatCompletionService ChatCompletion { get; }
        public IModelService Models => throw new NotImplementedException();
        public IFileService Files => throw new NotImplementedException();
        public IFineTuneService FineTunes => throw new NotImplementedException();
        public IImageService Images => throw new NotImplementedException();
        public IEmbeddingService Embeddings => throw new NotImplementedException();
        public IAudioService Audio => throw new NotImplementedException();
        public IModerationService Moderations => throw new NotImplementedException();
        public ICompletionsService Completions => throw new NotImplementedException();
        public IEditService Edits => throw new NotImplementedException();
        public HttpClient HttpClient => throw new NotImplementedException();

        public void SetDefaultModelId(string modelId) { /* Do nothing */ }
    }

    public class MockChatCompletionForJsonException : IChatCompletionService
    {
        private readonly bool _throwJsonException;

        public MockChatCompletionForJsonException(bool throwJsonException)
        {
            _throwJsonException = throwJsonException;
        }

        public Task<ChatCompletionCreateResponse> CreateCompletion(ChatCompletionCreateRequest chatCompletionCreateRequest, string? modelId = null, CancellationToken cancellationToken = default)
        {
            if (_throwJsonException)
            {
                try
                {
                    // Force a JsonException by parsing invalid JSON
                    JsonSerializer.Deserialize<object>("{\"malformed_json\": ");
                }
                catch (JsonException ex)
                {
                    // Re-throw the caught JsonException to simulate the library throwing it with details
                    throw new JsonException(ex.Message, ex.Path, ex.LineNumber, ex.BytePositionInLine, ex);
                }
            }
            // This part is for completeness if the mock was used to simulate success
            var successfulResponse = new ChatCompletionCreateResponse
            {
                Successful = true,
                Choices = new List<ChatChoiceResponse>
                {
                    new ChatChoiceResponse { Message = new ResponseMessage { Role = "assistant", Content = "Test content"} }
                },
                Model = modelId ?? "test-mock-model",
                Id = "mock-completion-id",
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            return Task.FromResult(successfulResponse);
        }

        public IAsyncEnumerable<ChatCompletionCreateResponse> CreateCompletionAsStream(ChatCompletionCreateRequest chatCompletionCreateRequest, string? modelId = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    [TestClass]
    public class LlmServiceGoalDecompositionTests
    {
        // Helper to simulate the JSON parsing part of DecomposeGoalIntoDailyTasksAsync
        private List<ProposedDailyTask> ParseGoalDecompositionJson(string jsonResponse)
        {
            if (string.IsNullOrWhiteSpace(jsonResponse) || jsonResponse.StartsWith("LLM dummy response") || jsonResponse.StartsWith("Error from LLM"))
            {
                return new List<ProposedDailyTask>();
            }
            try
            {
                jsonResponse = jsonResponse.Trim();
                if (jsonResponse.StartsWith("```json")) { jsonResponse = jsonResponse.Substring(7); }
                if (jsonResponse.EndsWith("```")) { jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3); }
                jsonResponse = jsonResponse.Trim();
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return System.Text.Json.JsonSerializer.Deserialize<List<ProposedDailyTask>>(jsonResponse, options) ?? new List<ProposedDailyTask>();
            }
            catch { return new List<ProposedDailyTask>(); } // Simplified error handling for test helper
        }

        [TestMethod]
        public void ParseGoalDecomposition_ValidJsonMultipleTasks_ReturnsCorrectTasks()
        {
            string mockJsonResponse = @"
        [
          { ""day"": 1, ""task_description"": ""Task 1"", ""quadrant"": ""Important & Urgent"", ""estimated_time"": ""1 hour"" },
          { ""day"": 2, ""task_description"": ""Task 2"", ""quadrant"": ""Important & Not Urgent"", ""estimated_time"": ""30 mins"" }
        ]";
            var result = ParseGoalDecompositionJson(mockJsonResponse);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].Day);
            Assert.AreEqual("Task 1", result[0].TaskDescription);
            Assert.AreEqual("Important & Urgent", result[0].Quadrant);
            Assert.AreEqual("1 hour", result[0].EstimatedTime);
            Assert.AreEqual(2, result[1].Day);
            Assert.AreEqual("Task 2", result[1].TaskDescription);
            Assert.AreEqual("Important & Not Urgent", result[1].Quadrant);
            Assert.AreEqual("30 mins", result[1].EstimatedTime);
        }

        [TestMethod]
        public void ParseGoalDecomposition_ValidJsonSingleTask_ReturnsCorrectTask()
        {
            string mockJsonResponse = @"[{""day"":1,""task_description"":""Single Task"",""quadrant"":""Low & Low"",""estimated_time"":""2h""}]"; // Using different quadrant for variety
            var result = ParseGoalDecompositionJson(mockJsonResponse);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].Day);
            Assert.AreEqual("Single Task", result[0].TaskDescription);
            Assert.AreEqual("Low & Low", result[0].Quadrant);
            Assert.AreEqual("2h", result[0].EstimatedTime);
        }

        [TestMethod]
        public void ParseGoalDecomposition_EmptyJsonArray_ReturnsEmptyList()
        {
            string mockJsonResponse = "[]";
            var result = ParseGoalDecompositionJson(mockJsonResponse);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ParseGoalDecomposition_JsonWrappedInMarkdown_ParsesCorrectly()
        {
            string mockJsonResponse = @"```json
        [
          { ""day"": 1, ""task_description"": ""Task 1"", ""quadrant"": ""Important & Urgent"", ""estimated_time"": ""1 hour"" }
        ]
        ```";
            var result = ParseGoalDecompositionJson(mockJsonResponse);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Task 1", result[0].TaskDescription);
        }

        [TestMethod]
        public void ParseGoalDecomposition_MalformedJson_ReturnsEmptyList()
        {
            string mockJsonResponse = "[{\"day\":1, \"task_description\":\"Task 1\""; // Malformed JSON
            var result = ParseGoalDecompositionJson(mockJsonResponse);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ParseGoalDecomposition_JsonWithMissingFields_ParsesWithDefaults()
        {
            string mockJsonResponse = @"[{""task_description"":""Task with missing fields""}]";
            var result = ParseGoalDecompositionJson(mockJsonResponse);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Task with missing fields", result[0].TaskDescription);
            Assert.AreEqual(0, result[0].Day); // Default for int
            Assert.IsNull(result[0].Quadrant); // Default for string
            Assert.IsNull(result[0].EstimatedTime); // Default for string
        }

        [TestMethod]
        public void ParseGoalDecomposition_NullOrEmptyResponse_ReturnsEmptyList()
        {
            Assert.AreEqual(0, ParseGoalDecompositionJson(null).Count, "Null response should return empty list.");
            Assert.AreEqual(0, ParseGoalDecompositionJson("").Count, "Empty string response should return empty list.");
            Assert.AreEqual(0, ParseGoalDecompositionJson("  ").Count, "Whitespace response should return empty list.");
        }

        [TestMethod]
        public void ParseGoalDecomposition_DummyErrorResponses_ReturnsEmptyList()
        {
            Assert.AreEqual(0, ParseGoalDecompositionJson("LLM dummy response (Configuration Error: API key missing or placeholder). Prompt: ...").Count, "Dummy response should return empty list.");
            Assert.AreEqual(0, ParseGoalDecompositionJson("Error from LLM: Some error message.").Count, "Error from LLM response should return empty list.");
        }
    }
}
