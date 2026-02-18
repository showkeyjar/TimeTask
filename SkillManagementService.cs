using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace TimeTask
{
    public class SkillDefinition
    {
        public string SkillId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool Enabled { get; set; }
    }

    public static class SkillManagementService
    {
        private const string EnabledSkillIdsKey = "EnabledSkillIds";

        public static readonly string[] AllowedSkillIds = new[]
        {
            "decompose", "focus_sprint", "priority_rebalance", "risk_check", "delegate_prepare", "clarify_goal"
        };

        public static List<SkillDefinition> GetSkillDefinitions()
        {
            var enabled = LoadEnabledSkillIds();
            var map = new Dictionary<string, (string title, string desc)>(StringComparer.OrdinalIgnoreCase)
            {
                ["decompose"] = ("任务拆解", "把模糊任务拆成可执行的下一步"),
                ["focus_sprint"] = ("专注冲刺", "给当前任务安排短时高专注执行"),
                ["priority_rebalance"] = ("优先级重排", "根据紧急/重要性调整顺序"),
                ["risk_check"] = ("风险检查", "提前检查阻塞点与失败风险"),
                ["delegate_prepare"] = ("委托准备", "为委托/协作准备最小信息包"),
                ["clarify_goal"] = ("目标澄清", "澄清任务目标、边界和完成标准")
            };

            return AllowedSkillIds
                .Select(id => new SkillDefinition
                {
                    SkillId = id,
                    Title = map.TryGetValue(id, out var v) ? v.title : id,
                    Description = map.TryGetValue(id, out var vv) ? vv.desc : id,
                    Enabled = enabled.Contains(id)
                })
                .ToList();
        }

        public static HashSet<string> LoadEnabledSkillIds()
        {
            try
            {
                string raw = ConfigurationManager.AppSettings[EnabledSkillIdsKey];
                if (string.IsNullOrWhiteSpace(raw))
                    return new HashSet<string>(AllowedSkillIds, StringComparer.OrdinalIgnoreCase);

                var ids = raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => AllowedSkillIds.Contains(x, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
                if (set.Count == 0)
                {
                    return new HashSet<string>(AllowedSkillIds, StringComparer.OrdinalIgnoreCase);
                }
                return set;
            }
            catch
            {
                return new HashSet<string>(AllowedSkillIds, StringComparer.OrdinalIgnoreCase);
            }
        }

        public static List<LlmSkillRecommendation> FilterEnabled(List<LlmSkillRecommendation> skills)
        {
            if (skills == null || skills.Count == 0)
                return new List<LlmSkillRecommendation>();

            var enabled = LoadEnabledSkillIds();
            return skills
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.SkillId))
                .Where(s => enabled.Contains(s.SkillId.Trim()))
                .ToList();
        }

        public static void SaveEnabledSkillIds(IEnumerable<string> enabledIds)
        {
            var target = (enabledIds ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => AllowedSkillIds.Contains(x, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (target.Count == 0)
            {
                target = new List<string>(AllowedSkillIds);
            }

            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = config.AppSettings.Settings;
            string value = string.Join(",", target);
            if (settings[EnabledSkillIdsKey] == null)
            {
                settings.Add(EnabledSkillIdsKey, value);
            }
            else
            {
                settings[EnabledSkillIdsKey].Value = value;
            }

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }
}
