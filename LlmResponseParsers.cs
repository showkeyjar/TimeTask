using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TimeTask
{
    /// <summary>
    /// LLM 文本响应的纯函数解析器。
    /// 从 LlmService 拆出：解析逻辑不依赖任何服务状态，只做「文本 → 结构」的转换，
    /// 方便独立单测与复用（ConversationCaptureService 等也会直接调用）。
    /// LlmService 上保留同名转发方法以兼容既有调用方与测试。
    /// </summary>
    internal static class LlmResponseParsers
    {
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
                // 标签感知解析：Reminder / Suggestion1..3 的取值从各自标签之后开始，
                // 一直延续到下一个已知标签所在行的行首（或文本末尾）。
                // 这样多行的 Reminder 值不会被截断，也不会把后续 Suggestion 标签吞进上一个值里。
                var labelRegex = new Regex(@"(?im)^[\t ]*(Reminder|Suggestion[1-3])[\t ]*:[\t ]*");
                var labels = labelRegex.Matches(llmResponse).Cast<Match>().ToList();

                for (int i = 0; i < labels.Count; i++)
                {
                    int valueStart = labels[i].Index + labels[i].Length;
                    int valueEnd = (i + 1 < labels.Count) ? labels[i + 1].Index : llmResponse.Length;
                    string value = llmResponse.Substring(valueStart, valueEnd - valueStart).Trim();

                    string label = labels[i].Groups[1].Value.ToUpperInvariant();
                    if (label == "REMINDER")
                    {
                        reminder = value;
                    }
                    else if (!string.IsNullOrWhiteSpace(value) && !value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add(value);
                    }
                }

                if (string.IsNullOrEmpty(reminder) && !suggestions.Any())
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

                // 只要存在 "Subtasks:" 标签就解析子任务（状态未知/标签拼写错误时也解析），
                // 这样 "Status: Maybe" 之类异常状态不会连带丢掉已给出的子任务列表。
                // 唯一例外：Sufficient 明确表示无需分解，忽略子任务块。
                if (status != DecompositionStatus.Sufficient)
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
                    }
                    else if (status == ClarityStatus.Unknown)
                    {
                        question = "Failed to parse status and question from LLM response.";
                    }
                }

                // 状态标签缺失/非法时，若 Question 本身已成功解析则保留它，
                // 不要用兜底文案覆盖真实内容（否则调用方拿不到 LLM 实际给出的问题）。
                if (status == ClarityStatus.Unknown && !statusMatch.Success && string.IsNullOrWhiteSpace(question))
                {
                    // If status is still unknown because regex failed
                    question = "Failed to parse status from LLM response.";
                }
                else if (status == ClarityStatus.NeedsClarification && string.IsNullOrWhiteSpace(question))
                {
                    Console.WriteLine("Warning: Status is NeedsClarification, but question is empty or N/A.");
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
    }
}
