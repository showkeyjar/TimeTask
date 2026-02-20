using System;
using System.Collections.Generic;
using System.IO;

namespace TimeTask
{
    public sealed class KnowledgeRuleSet
    {
        public List<string> TaskMarkers { get; } = new List<string> { "- [ ]", "* [ ]", "TODO", "待办", "待处理" };
        public List<string> ActionVerbs { get; } = new List<string> { "完成", "处理", "跟进", "整理", "提交", "联系", "安排", "评审", "修复", "优化" };
        public List<string> ExcludePrefixes { get; } = new List<string> { "#", "```", ">", "![", "---" };
        public List<string> DeadlineKeywords { get; } = new List<string> { "截止", "due", "ddl", "今天", "明天", "后天" };
        public List<string> HighPriorityKeywords { get; } = new List<string> { "紧急", "尽快", "马上", "asap", "critical", "blocker" };
        public List<string> LowPriorityKeywords { get; } = new List<string> { "有空", "可选", "之后", "someday", "maybe" };
        public int MinTitleLength { get; set; } = 4;
        public int MaxTitleLength { get; set; } = 120;
        public double CheckboxConfidence { get; set; } = 0.94;
        public double VerbConfidence { get; set; } = 0.75;
        public double DeadlineBoost { get; set; } = 0.1;

        public static KnowledgeRuleSet Load(string path)
        {
            var rules = new KnowledgeRuleSet();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return rules;
            }

            string currentListKey = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = RemoveComment(raw).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("- "))
                {
                    if (!string.IsNullOrEmpty(currentListKey))
                    {
                        AddToList(rules, currentListKey, line.Substring(2).Trim().Trim('"'));
                    }
                    continue;
                }

                int separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim().Trim('"');
                if (value.Length == 0)
                {
                    currentListKey = key;
                    continue;
                }

                currentListKey = null;
                ApplyScalar(rules, key, value);
            }

            return rules;
        }

        private static string RemoveComment(string line)
        {
            int commentIdx = line.IndexOf('#');
            if (commentIdx < 0)
            {
                return line;
            }
            return line.Substring(0, commentIdx);
        }

        private static void ApplyScalar(KnowledgeRuleSet rules, string key, string value)
        {
            switch (key)
            {
                case "min_title_length":
                    if (int.TryParse(value, out int minLen))
                    {
                        rules.MinTitleLength = Math.Max(1, minLen);
                    }
                    break;
                case "max_title_length":
                    if (int.TryParse(value, out int maxLen))
                    {
                        rules.MaxTitleLength = Math.Max(rules.MinTitleLength, maxLen);
                    }
                    break;
                case "checkbox_confidence":
                    if (double.TryParse(value, out double checkConf))
                    {
                        rules.CheckboxConfidence = Clamp01(checkConf);
                    }
                    break;
                case "verb_confidence":
                    if (double.TryParse(value, out double verbConf))
                    {
                        rules.VerbConfidence = Clamp01(verbConf);
                    }
                    break;
                case "deadline_boost":
                    if (double.TryParse(value, out double boost))
                    {
                        rules.DeadlineBoost = Math.Max(0.0, Math.Min(0.3, boost));
                    }
                    break;
            }
        }

        private static void AddToList(KnowledgeRuleSet rules, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            switch (key)
            {
                case "task_markers":
                    AddDistinct(rules.TaskMarkers, value);
                    break;
                case "action_verbs":
                    AddDistinct(rules.ActionVerbs, value);
                    break;
                case "exclude_prefixes":
                    AddDistinct(rules.ExcludePrefixes, value);
                    break;
                case "deadline_keywords":
                    AddDistinct(rules.DeadlineKeywords, value);
                    break;
                case "high_priority_keywords":
                    AddDistinct(rules.HighPriorityKeywords, value);
                    break;
                case "low_priority_keywords":
                    AddDistinct(rules.LowPriorityKeywords, value);
                    break;
            }
        }

        private static void AddDistinct(List<string> list, string value)
        {
            if (!list.Contains(value))
            {
                list.Add(value);
            }
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
