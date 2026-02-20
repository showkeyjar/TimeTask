using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TimeTask
{
    public sealed class ObsidianKnowledgeConnector
    {
        public string VaultPath { get; }
        public bool IncludeSubfolders { get; }
        public int MaxFilesPerSync { get; }

        public ObsidianKnowledgeConnector(string vaultPath, bool includeSubfolders, int maxFilesPerSync)
        {
            VaultPath = vaultPath;
            IncludeSubfolders = includeSubfolders;
            MaxFilesPerSync = Math.Max(10, maxFilesPerSync);
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(VaultPath) && Directory.Exists(VaultPath);
        }

        public List<ObsidianNote> GetChangedNotes(
            Dictionary<string, string> previousSignatures,
            out Dictionary<string, string> latestSignatures)
        {
            latestSignatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var changedNotes = new List<ObsidianNote>();
            if (!IsConfigured())
            {
                return changedNotes;
            }

            var files = EnumerateMarkdownFilesSafe()
                .Select(path => SafeCreateFileInfo(path))
                .Where(info => info != null)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(MaxFilesPerSync * 3)
                .ToList();

            foreach (var file in files)
            {
                string relativePath = GetRelativePath(file.FullName);
                string signature = BuildSignature(file);
                latestSignatures[relativePath] = signature;

                if (previousSignatures != null &&
                    previousSignatures.TryGetValue(relativePath, out string previous) &&
                    string.Equals(previous, signature, StringComparison.Ordinal))
                {
                    continue;
                }

                string content;
                try
                {
                    content = File.ReadAllText(file.FullName, Encoding.UTF8);
                }
                catch
                {
                    continue;
                }

                changedNotes.Add(new ObsidianNote
                {
                    RelativePath = relativePath.Replace('\\', '/'),
                    Title = Path.GetFileNameWithoutExtension(file.Name),
                    Content = content,
                    LastWriteTimeUtc = file.LastWriteTimeUtc,
                    Signature = signature
                });

                if (changedNotes.Count >= MaxFilesPerSync)
                {
                    break;
                }
            }

            return changedNotes;
        }

        private IEnumerable<string> EnumerateMarkdownFilesSafe()
        {
            if (!IncludeSubfolders)
            {
                IEnumerable<string> topFiles = Enumerable.Empty<string>();
                try
                {
                    topFiles = Directory.EnumerateFiles(VaultPath, "*.md", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    yield break;
                }

                foreach (var file in topFiles)
                {
                    yield return file;
                }
                yield break;
            }

            var pending = new Stack<string>();
            pending.Push(VaultPath);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                IEnumerable<string> files = Enumerable.Empty<string>();
                try
                {
                    files = Directory.EnumerateFiles(current, "*.md", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                IEnumerable<string> dirs = Enumerable.Empty<string>();
                try
                {
                    dirs = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                }

                foreach (var dir in dirs)
                {
                    pending.Push(dir);
                }
            }
        }

        private static FileInfo SafeCreateFileInfo(string path)
        {
            try
            {
                return new FileInfo(path);
            }
            catch
            {
                return null;
            }
        }

        private string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(VaultPath))
            {
                return fullPath ?? string.Empty;
            }

            if (fullPath.StartsWith(VaultPath, StringComparison.OrdinalIgnoreCase))
            {
                string relative = fullPath.Substring(VaultPath.Length);
                return relative.TrimStart('\\', '/');
            }

            return fullPath;
        }

        private static string BuildSignature(FileInfo file)
        {
            string raw = $"{file.LastWriteTimeUtc.Ticks}:{file.Length}";
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return Convert.ToBase64String(hash);
            }
        }
    }
}
