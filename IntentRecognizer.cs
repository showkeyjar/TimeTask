using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TimeTask
{
    /// <summary>
    /// 本地意图识别器
    /// 在调用 LLM 之前，先用简单的规则判断内容是否可能是"任务"
    /// 避免对每一句话都调用 LLM，节省资源
    /// </summary>
    public class IntentRecognizer
    {
        // 任务相关的关键词模式
        private static readonly string[] TaskPatterns = new[]
        {
            // 动作词开头
            @"^(需要|要|得|应该|必须|得|赶紧|快点|马上|立刻|明天|后天|今天|这周|下周|这月|本月)",
            // 任务动作词
            @"\b(做|完成|处理|解决|写|编辑|修改|检查|审核|提交|发送|回复|安排|计划|准备|整理|归档|备份|安装|配置|学习|研究|阅读|查看|访问|联系|打电话|开会|讨论|汇报|演示|展示)\b",
            // 时间紧迫词
            @"\b(马上|立刻|尽快|尽快|今天|明天|后天|周五|周一|本周|下周|月底| deadline|截止)\b",
            // 任务标记词
            @"\b(任务|事|东西|工作|项目|问题|bug|错误|需求|功能)\b"
        };

        // 排除模式（非任务内容）
        private static readonly string[] ExcludePatterns = new[]
        {
            @"^(好的|是的|嗯|哦|啊|呀|哎|哈|嘿嘿|哈哈)",
            @"\b(天气|新闻|八卦|娱乐|游戏|电影|音乐|吃饭|睡觉|休息)\b",
            @"^.{1,3}$" // 太短的句子
        };

        /// <summary>
        /// 判断文本内容是否可能是"任务相关"
        /// </summary>
        /// <param name="text">用户语音转文字后的内容</param>
        /// <returns>true 表示可能是任务，false 表示可能不是</returns>
        public bool IsPotentialTask(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim().ToLower();

            // 排除太短的句子
            if (text.Length < 5)
                return false;

            // 检查排除模式
            foreach (var pattern in ExcludePatterns)
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                    return false;
            }

            // 检查任务模式 - 满足至少2个条件即可
            int matchCount = 0;

            foreach (var pattern in TaskPatterns)
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                    {
                    matchCount++;
                    // 动作词权重更高
                    if (pattern.Contains(@"\b(做|完成|处理|解决|写|编辑|修改|检查|审核|提交|发送|回复|安排|计划|准备|整理|归档|备份|安装|配置|学习|研究|阅读|查看|访问|联系|打电话|开会|讨论|汇报|演示|展示)\b"))
                    {
                        matchCount++; // 动作词权重+1
                    }
                }
            }

            return matchCount >= 2;
        }

        /// <summary>
        /// 提取任务描述（简单清理）
        /// </summary>
        public string ExtractTaskDescription(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // 清理语气词和无关内容
            text = Regex.Replace(text, @"^(好的|是的|嗯|哦|啊|呀|哎|哈|嘿嘿|哈哈|对了|那个|呃|嗯嗯)\s*", "", RegexOptions.IgnoreCase);
            text = text.Trim();

            // 移除句末语气词
            text = Regex.Replace(text, @"(啊|呀|吧|呢|哦|啦)$", "", RegexOptions.IgnoreCase);
            text = text.Trim();

            return text.Length >= 3 ? text : null;
        }

        /// <summary>
        /// 快速判断内容优先级（基于关键词）
        /// </summary>
        public (string importance, string urgency) EstimatePriority(string text)
        {
            string importance = "Medium";
            string urgency = "Medium";

            // 紧迫性判断
            if (Regex.IsMatch(text, @"\b(马上|立刻|尽快|紧急|马上|立即|立刻|今天|现在|立刻)\b", RegexOptions.IgnoreCase))
                urgency = "High";
            else if (Regex.IsMatch(text, @"\b(明天|后天|下周|这周|本周|不急|有空|回头|以后|有空再说)\b", RegexOptions.IgnoreCase))
                urgency = "Low";

            // 重要性判断
            if (Regex.IsMatch(text, @"\b(重要|必须|应该|关键|核心|主要|紧急|重大|严重)\b", RegexOptions.IgnoreCase))
                importance = "High";
            else if (Regex.IsMatch(text, @"\b(次要|小|简单|随手|顺便|有空)\b", RegexOptions.IgnoreCase))
                importance = "Low";

            return (importance, urgency);
        }
    }
}
