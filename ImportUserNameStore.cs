using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TimeTask
{
    public class ImportUserNameStore
    {
        private readonly string _filePath;

        public ImportUserNameStore()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeTask");
            Directory.CreateDirectory(appDataPath);
            _filePath = Path.Combine(appDataPath, "import_user_profile.json");
        }

        public List<string> GetAliases()
        {
            var profile = Load();
            return profile?.Aliases?.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                   ?? new List<string>();
        }

        public List<string> GetKnownNames()
        {
            var profile = Load();
            return profile?.KnownNames?.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                   ?? new List<string>();
        }

        public void SaveAliases(List<string> aliases, bool remember)
        {
            if (!remember) return;

            var clean = aliases?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        ?? new List<string>();
            var profile = Load() ?? new ImportUserNameProfile();
            profile.Aliases = clean;

            foreach (var name in clean)
            {
                if (!profile.KnownNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    profile.KnownNames.Add(name);
                }
            }

            Save(profile);
        }

        private ImportUserNameProfile Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return new ImportUserNameProfile();
                string json = File.ReadAllText(_filePath);
                var profile = JsonSerializer.Deserialize<ImportUserNameProfile>(json);
                return profile ?? new ImportUserNameProfile();
            }
            catch
            {
                return new ImportUserNameProfile();
            }
        }

        private void Save(ImportUserNameProfile profile)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_filePath, JsonSerializer.Serialize(profile, options));
            }
            catch
            {
            }
        }
    }

    public class ImportUserNameProfile
    {
        public List<string> Aliases { get; set; } = new List<string>();
        public List<string> KnownNames { get; set; } = new List<string>();
    }
}
