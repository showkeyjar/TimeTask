using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text.Json;

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

    /// <summary>
    /// 思维工具开关的持久化。
    /// 历史实现把用户选择写进 exe 旁的 TimeTask.exe.config：
    /// 装在 Program Files 时会静默失败（无写权限），便携盘上则污染程序目录。
    /// 现改为写入用户数据目录的 JSON（原子写 + .bak），旧 appSettings 值仅作一次迁移读取。
    /// </summary>
    public static class SkillManagementService
    {
        private const string EnabledSkillIdsKey = "EnabledSkillIds";
        private static string StatePath => AppPaths.GetDataFile("skill_settings.json");

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
                // 1) 新存储：用户数据目录 JSON（损坏自动回退 .bak）
                var stored = JsonStore.Load(StatePath, json => JsonSerializer.Deserialize<List<string>>(json));
                if (stored != null)
                {
                    return ToEnabledSet(stored);
                }

                // 2) 旧存储：exe 配置里的 appSettings（只读迁移，不再写回）
                string raw = ConfigurationManager.AppSettings[EnabledSkillIdsKey];
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var ids = raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return ToEnabledSet(ids);
                }

                // 3) 默认：全部启用
                return DefaultSet();
            }
            catch
            {
                return DefaultSet();
            }
        }

        private static HashSet<string> ToEnabledSet(IEnumerable<string> ids)
        {
            var filtered = ids
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => AllowedSkillIds.Contains(x, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count == 0)
            {
                return DefaultSet();
            }
            return new HashSet<string>(filtered, StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> DefaultSet()
        {
            return new HashSet<string>(AllowedSkillIds, StringComparer.OrdinalIgnoreCase);
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

            try
            {
                string dir = Path.GetDirectoryName(StatePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                AtomicFile.WriteAllText(StatePath, JsonSerializer.Serialize(target));
            }
            catch (Exception ex)
            {
                // 保存失败要可见：这是用户在界面上明确做的选择
                VoiceRuntimeLog.Error("保存思维工具开关失败。", ex);
                throw;
            }
        }
    }
}
