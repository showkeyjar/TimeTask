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
        public string Scenario { get; set; }
        public bool Enabled { get; set; }
    }

    public static class SkillManagementService
    {
        private const string EnabledSkillIdsKey = "EnabledSkillIds";

        public static readonly string[] AllowedSkillIds = ThinkingToolAdvisor.GetAllowedSkillIds();

        public static List<SkillDefinition> GetSkillDefinitions()
        {
            var enabled = LoadEnabledSkillIds();
            var defs = ThinkingToolAdvisor.GetDefinitions();

            return defs
                .Select(def => new SkillDefinition
                {
                    SkillId = def.SkillId,
                    Title = def.Title,
                    Description = def.Description,
                    Scenario = def.Scenario,
                    Enabled = enabled.Contains(def.SkillId)
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
