using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TimeTask
{
    public sealed class KnowledgeTaskExtractor
    {
        private static readonly Regex DateRegex = new Regex(@"(20\d{2})[-/\.](\d{1,2})[-/\.](\d{1,2})", RegexOptions.Compiled);
        private readonly KnowledgeRuleSet _rules;

        public KnowledgeTaskExtractor(KnowledgeRuleSet rules)
        {
            _rules = rules ?? new KnowledgeRuleSet();
        }

        public List<ExtractedTaskCandidate> Extract(ObsidianNote note)
        {
            var results = new List<ExtractedTaskCandidate>();
            if (note == null || string.IsNullOrWhiteSpace(note.Content))
            {
                return results;
            }

            bool inCodeBlock = false;
            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = note.Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var rawLine in lines)
            {
                string line = (rawLine ?? string.Empty).Trim();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inCodeBlock = !inCodeBlock;
                    continue;
                }

                if (inCodeBlock || IsExcluded(line))
                {
                    continue;
                }

                bool checkboxTask = IsCheckboxTask(line);
                bool actionTask = !checkboxTask && HasAnyKeyword(line, _rules.ActionVerbs);
                if (!checkboxTask && !actionTask)
                {
                    continue;
                }

                string title = CleanupTitle(line);
                if (title.Length < _rules.MinTitleLength || title.Length > _rules.MaxTitleLength)
                {
                    continue;
                }

                if (!dedupe.Add(title))
                {
                    continue;
                }

                bool hasDeadline = HasAnyKeyword(line, _rules.DeadlineKeywords);
                double confidence = checkboxTask ? _rules.CheckboxConfidence : _rules.VerbConfidence;
                if (hasDeadline)
                {
                    confidence = Math.Min(1.0, confidence + _rules.DeadlineBoost);
                }

                results.Add(new ExtractedTaskCandidate
                {
                    Title = title,
                    Priority = ResolvePriority(line),
                    DueAt = ParseDueAt(line),
                    Confidence = confidence,
                    SourcePath = note.RelativePath,
                    EvidenceLine = line
                });
            }

            return results;
        }

        private bool IsExcluded(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            for (int i = 0; i < _rules.ExcludePrefixes.Count; i++)
            {
                if (line.StartsWith(_rules.ExcludePrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsCheckboxTask(string line)
        {
            for (int i = 0; i < _rules.TaskMarkers.Count; i++)
            {
                if (line.StartsWith(_rules.TaskMarkers[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string CleanupTitle(string line)
        {
            string title = line
                .Replace("- [ ]", string.Empty)
                .Replace("* [ ]", string.Empty)
                .Replace("- [x]", string.Empty)
                .Replace("- [X]", string.Empty)
                .Replace("TODO:", string.Empty)
                .Replace("TODO", string.Empty)
                .Trim();

            title = Regex.Replace(title, @"\[[^\]]+\]\([^)]+\)", "$1");
            title = Regex.Replace(title, @"`{1,3}", string.Empty);
            title = Regex.Replace(title, @"\s+", " ");
            return title.Trim(' ', '-', '*', ':', '。', '；');
        }

        private static bool HasAnyKeyword(string input, List<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(input) || keywords == null)
            {
                return false;
            }

            for (int i = 0; i < keywords.Count; i++)
            {
                if (input.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private string ResolvePriority(string line)
        {
            if (HasAnyKeyword(line, _rules.HighPriorityKeywords))
            {
                return "high";
            }
            if (HasAnyKeyword(line, _rules.LowPriorityKeywords))
            {
                return "low";
            }
            return "medium";
        }

        private static DateTime? ParseDueAt(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            var match = DateRegex.Match(line);
            if (match.Success)
            {
                string composed = $"{match.Groups[1].Value}-{match.Groups[2].Value.PadLeft(2, '0')}-{match.Groups[3].Value.PadLeft(2, '0')}";
                if (DateTime.TryParseExact(composed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exact))
                {
                    return exact;
                }
            }

            DateTime today = DateTime.Today;
            if (line.IndexOf("今天", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("today", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return today;
            }
            if (line.IndexOf("明天", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("tomorrow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return today.AddDays(1);
            }
            if (line.IndexOf("后天", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return today.AddDays(2);
            }

            return null;
        }
    }
}
