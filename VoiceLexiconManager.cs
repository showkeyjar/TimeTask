using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TimeTask
{
    public sealed class VoiceLexiconManager
    {
        private readonly SpeechModelManager _modelManager;
        private readonly string _userLexiconPath;

        public VoiceLexiconManager(SpeechModelManager modelManager = null)
        {
            _modelManager = modelManager ?? new SpeechModelManager();

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "TimeTask");
            Directory.CreateDirectory(dir);
            _userLexiconPath = Path.Combine(dir, "voice_lexicon.txt");
        }

        public void RecordConfirmedPhrase(string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return;

            string cleaned = phrase.Trim();
            if (cleaned.Length < 2)
                return;

            var existing = LoadUserLexicon();
            if (!existing.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
            {
                existing.Add(cleaned);
                SaveUserLexicon(existing);
            }

            MergeIntoPhrasesFile(existing);
        }

        public List<string> LoadUserLexicon()
        {
            try
            {
                if (!File.Exists(_userLexiconPath))
                    return new List<string>();

                return File.ReadAllLines(_userLexiconPath)
                    .Select(l => l?.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private void SaveUserLexicon(List<string> phrases)
        {
            try
            {
                var lines = phrases
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                File.WriteAllLines(_userLexiconPath, lines);
            }
            catch { }
        }

        private void MergeIntoPhrasesFile(List<string> userPhrases)
        {
            try
            {
                string hintsPath = _modelManager.GetHintsFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(hintsPath));

                var existing = new List<string>();
                if (File.Exists(hintsPath))
                {
                    existing = File.ReadAllLines(hintsPath)
                        .Select(l => l?.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();
                }

                var merged = existing
                    .Concat(userPhrases)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(500)
                    .ToList();

                File.WriteAllLines(hintsPath, merged);
                VoiceRuntimeLog.Info($"Voice lexicon merged into phrases.txt, count={merged.Count}");
            }
            catch (Exception ex)
            {
                VoiceRuntimeLog.Error("Failed to merge voice lexicon into phrases.txt.", ex);
            }
        }
    }
}
