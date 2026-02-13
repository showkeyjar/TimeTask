using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TimeTask
{
    /// <summary>
    /// 本地意图识别器
    /// 使用轻量规则对文本进行任务概率评分，减少误识别。
    /// </summary>
    public class IntentRecognizer
    {
        private static readonly string[] ActionWords = new[]
        {
            "做","完成","处理","解决","写","编辑","修改","检查","审核","提交","发送","回复",
            "安排","计划","准备","整理","归档","备份","安装","配置","学习","研究","阅读","查看",
            "访问","联系","打电话","开会","讨论","汇报","演示","修复","跟进","推进","确认","上线"
        };

        private static readonly string[] TaskNouns = new[]
        {
            "任务","工作","项目","方案","文档","邮件","报告","代码","bug","缺陷","问题","需求",
            "版本","会议","周报","计划","材料","排期","测试","部署","合同","发票","同步"
        };

        private static readonly string[] UrgentWords = new[]
        {
            "马上","立刻","尽快","立即","紧急","今天","现在","截止","ddl","到点","晚点前","本周内","上午","下午","今晚","明早"
        };

        private static readonly string[] NonUrgentWords = new[]
        {
            "明天","后天","下周","有空","回头","不急","以后","抽空","慢慢来","下个月","下下周"
        };

        private static readonly string[] ImportantWords = new[]
        {
            "重要","必须","关键","核心","主要","严重","优先","高优","客户","上线","风险","生产","会议","同步"
        };

        private static readonly string[] UnimportantWords = new[]
        {
            "顺便","随手","可选","次要","小事","不重要","低优","以后再说"
        };

        private static readonly string[] ExcludeWords = new[]
        {
            "天气","新闻","八卦","娱乐","游戏","电影","音乐","吃饭","睡觉","休息"
        };

        private static readonly Regex ChineseOrEnglishWordRegex = new Regex(@"[\u4e00-\u9fa5A-Za-z0-9]+", RegexOptions.Compiled);

        public bool IsPotentialTask(string text)
        {
            return ScoreTaskLikelihood(text) >= 0.55;
        }

        /// <summary>
        /// 返回 [0,1] 任务可能性评分。
        /// </summary>
        public double ScoreTaskLikelihood(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string normalized = NormalizeText(text);
            if (normalized.Length < 4)
                return 0;

            int score = 0;

            if (ContainsAny(normalized, ActionWords))
                score += 4;

            if (ContainsAny(normalized, TaskNouns))
                score += 3;

            if (ContainsAny(normalized, UrgentWords))
                score += 2;

            if (ContainsAny(normalized, ImportantWords))
                score += 2;

            if (Regex.IsMatch(normalized, @"^(提醒我|帮我|记得|待会|等会|需要|要|得|必须|应该)"))
                score += 3;

            if (Regex.IsMatch(normalized, @"(今天|明天|后天|本周|下周|周[一二三四五六日天])"))
                score += 2;

            if (Regex.IsMatch(normalized, @"(点|号|月|日|前|后|截止)"))
                score += 1;

            if (ContainsAny(normalized, ExcludeWords))
            {
                score -= 4;
            }

            if (Regex.IsMatch(normalized, @"^(好的|是的|嗯|哦|啊|呀|哈哈|收到)$"))
                score -= 5;

            if (normalized.Length > 40)
            {
                score += 1;
            }

            double normalizedScore = Math.Max(0, Math.Min(12, score)) / 12.0;
            return normalizedScore;
        }

        /// <summary>
        /// 提取任务描述（清理口语前后缀和噪声）
        /// </summary>
        public string ExtractTaskDescription(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string normalized = NormalizeText(text);
            normalized = Regex.Replace(normalized, @"^(请)?\s*(提醒我|帮我|记得)\s*", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"^(麻烦你|请|那个|就是|然后|我想|我需要)\s*", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"^(要|需要|必须|应该|得)\s*", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"(啊|呀|吧|呢|哦|啦|嘛)$", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            var matchWords = ChineseOrEnglishWordRegex.Matches(normalized).Cast<Match>().Select(m => m.Value).ToList();
            if (matchWords.Count < 2 && normalized.Length < 5)
                return null;

            return normalized.Length >= 3 ? normalized : null;
        }

        /// <summary>
        /// 快速估计重要性和紧迫性
        /// </summary>
        public (string importance, string urgency) EstimatePriority(string text)
        {
            string importance = "Medium";
            string urgency = "Medium";
            string normalized = NormalizeText(text);

            if (ContainsAny(normalized, UrgentWords) || Regex.IsMatch(normalized, @"(今天|今晚|现在|截止|下班前|半小时)"))
                urgency = "High";
            else if (ContainsAny(normalized, NonUrgentWords))
                urgency = "Low";

            if (ContainsAny(normalized, ImportantWords))
                importance = "High";
            else if (ContainsAny(normalized, UnimportantWords))
                importance = "Low";

            return (importance, urgency);
        }

        public string EstimateQuadrant(string importance, string urgency)
        {
            if (importance == "High" && urgency == "High")
                return "重要且紧急";
            if (importance == "High" && urgency == "Low")
                return "重要不紧急";
            if (importance == "Low" && urgency == "High")
                return "不重要紧急";
            return "不重要不紧急";
        }

        public bool IsReminderLike(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = NormalizeText(text);
            bool hasTime = Regex.IsMatch(normalized, @"(今天|明天|后天|本周|下周|周[一二三四五六日天]|上午|下午|晚上|明早|明晚)");
            bool hasMeeting = normalized.Contains("会议") || normalized.Contains("同步");
            bool hasReminder = normalized.Contains("提醒");

            if (hasReminder && (hasTime || hasMeeting))
                return true;

            if (hasMeeting && hasTime)
                return true;

            return false;
        }

        private static bool ContainsAny(string text, IEnumerable<string> words)
        {
            return words.Any(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string NormalizeText(string text)
        {
            string normalized = text.Trim();
            normalized = Regex.Replace(normalized, @"[，。！？,.!?;；]+", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized.Trim();
        }
    }
}
