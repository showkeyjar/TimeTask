using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TimeTask
{
    public sealed class VoiceTaskAnalysis
    {
        public string RawText { get; set; }
        public string SanitizedText { get; set; }
        public bool IsMeaningfulTask { get; set; }
        public bool HasStrongAction { get; set; }
        public bool HasTaskSignal { get; set; }
        public bool HasTimeExpression { get; set; }
        public bool IsQuestionLike { get; set; }
        public bool IsLongFreeformSpeech { get; set; }
        public bool IsReminderCandidate { get; set; }
        public double StructureScore { get; set; }
        public List<string> Keywords { get; set; } = new List<string>();
    }

    public static class TaskTextQualityHelper
    {
        private static readonly Regex MultiSpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex KeywordRegex = new Regex(@"[\u4e00-\u9fa5A-Za-z0-9]{2,}", RegexOptions.Compiled);
        private static readonly Regex RepeatedPunctuationRegex = new Regex(@"[，。！？,.!?;；]+", RegexOptions.Compiled);

        private static readonly HashSet<string> NoisePhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "好的","是的","嗯","嗯嗯","啊","哦","呀","吧","呢","啦","嘛","哈","哈哈","收到","知道了",
            "知道","明白","可以","行","行了","好吧","好呀","好啊","对","对啊","对呀","对吧","是吧",
            "不是","没有","什么","这个","那个","然后","就是","哎呀","我操","我靠","暂时不需要","帮我查一下"
        };

        private static readonly HashSet<string> NoiseKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "High","Medium","Low","Unknown","现在","今天","明天","后天","上午","下午","晚上","回头",
            "这个","那个","就是","然后","应该","可以","一下","一下子","我们","你们","他们","自己",
            "东西","事情","问题","时候","是不是","为什么","怎么","什么","不是","没有"
        };

        private static readonly string[] StrongActionWords = new[]
        {
            "做","完成","处理","解决","写","编辑","修改","检查","审核","提交","发送","回复","安排",
            "计划","准备","整理","归档","备份","安装","配置","学习","研究","阅读","查看","访问","联系",
            "打电话","开会","讨论","汇报","演示","修复","跟进","推进","确认","上线","复盘","同步","对接",
            "沟通","约见","拜访","评审","汇总","跟踪","催办","申请","付款","取款","购买","预订","预约"
        };

        private static readonly string[] TaskSignals = new[]
        {
            "提醒我","帮我","记得","需要","要","得","必须","应该","安排","会议","电话","文档","报告",
            "方案","代码","需求","任务","项目","计划","材料","周报","邮件","PPT","汇报"
        };

        private static readonly string[] ReminderSignals = new[]
        {
            "提醒我","记得","别忘了","到时候","叫我","定个提醒","提醒一下"
        };

        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Trim();
            normalized = RepeatedPunctuationRegex.Replace(normalized, " ");
            normalized = MultiSpaceRegex.Replace(normalized, " ");
            return normalized.Trim();
        }

        public static string SanitizeTaskText(string text)
        {
            string normalized = Normalize(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            normalized = Regex.Replace(normalized, @"^(请)?\s*(提醒我|帮我|记得)\s*", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"^(麻烦你|请|那个|就是|然后|我想|我需要|我现在要|我现在得|那就|现在)\s*", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"^(要|需要|必须|应该|得)\s*", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"(啊|呀|吧|呢|哦|啦|嘛)$", "", RegexOptions.IgnoreCase);
            normalized = MultiSpaceRegex.Replace(normalized, " ").Trim();

            return normalized;
        }

        public static bool IsMeaningfulTaskText(string text)
        {
            return AnalyzeVoiceTaskCandidate(text).IsMeaningfulTask;
        }

        public static VoiceTaskAnalysis AnalyzeVoiceTaskCandidate(string text)
        {
            string sanitized = SanitizeTaskText(text);
            var analysis = new VoiceTaskAnalysis
            {
                RawText = text,
                SanitizedText = sanitized
            };

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return analysis;
            }

            analysis.Keywords = ExtractKeywords(sanitized);
            analysis.HasStrongAction = StrongActionWords.Any(word => sanitized.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
            analysis.HasTaskSignal = TaskSignals.Any(word => sanitized.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
            analysis.HasTimeExpression = Regex.IsMatch(sanitized, @"(今天|明天|后天|本周|下周|周[一二三四五六日天]|上午|下午|晚上|明早|明晚|现在|\d+\s*(点|分|号|日|月)|半小时后|\d+\s*(分钟|小时|天)后)");
            analysis.IsReminderCandidate = ReminderSignals.Any(word => sanitized.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
            analysis.IsQuestionLike = Regex.IsMatch(sanitized, @"(吗|么|呢|是不是|能不能|要不要|为什么|怎么|啥|什么)$") || sanitized.Contains("？") || sanitized.Contains("?");
            analysis.IsLongFreeformSpeech = sanitized.Length > 28 && !analysis.IsReminderCandidate && !analysis.HasTimeExpression && !analysis.HasStrongAction;

            if (sanitized.Length < 4 || sanitized.Length > 48)
            {
                return analysis;
            }

            if (NoisePhrases.Contains(sanitized))
            {
                return analysis;
            }

            if (Regex.IsMatch(sanitized, @"^[\p{P}\p{S}\d\s]+$"))
            {
                return analysis;
            }

            double score = 0;
            if (analysis.HasStrongAction) score += 0.45;
            if (analysis.HasTaskSignal) score += 0.30;
            if (analysis.HasTimeExpression) score += 0.20;
            if (analysis.IsReminderCandidate) score += 0.20;
            if (analysis.Keywords.Count >= 2) score += 0.15;
            if (analysis.Keywords.Count == 1 && (analysis.HasStrongAction || analysis.HasTaskSignal)) score += 0.08;
            if (analysis.IsQuestionLike) score -= 0.22;
            if (analysis.IsLongFreeformSpeech) score -= 0.28;
            if (analysis.Keywords.Count == 0) score -= 0.35;

            analysis.StructureScore = Math.Max(0, Math.Min(1, score));

            if (analysis.Keywords.Count == 1 && !analysis.HasStrongAction && !analysis.HasTaskSignal)
            {
                return analysis;
            }

            if (!analysis.HasStrongAction && !analysis.HasTaskSignal && !analysis.HasTimeExpression && !analysis.IsReminderCandidate)
            {
                return analysis;
            }

            analysis.IsMeaningfulTask = analysis.StructureScore >= 0.35;
            return analysis;
        }

        public static List<string> ExtractKeywords(string text, int maxCount = 8)
        {
            string normalized = Normalize(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new List<string>();
            }

            var tokens = KeywordRegex.Matches(normalized)
                .Cast<Match>()
                .Select(match => match.Value.Trim())
                .Where(token => token.Length >= 2)
                .Where(token => !token.All(char.IsDigit))
                .Where(token => !NoiseKeywords.Contains(token))
                .ToList();

            foreach (string action in StrongActionWords)
            {
                if (normalized.IndexOf(action, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tokens.Add(action);
                }
            }

            return tokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxCount))
                .ToList();
        }
    }
}
