using System;
using System.Collections.Generic;

namespace TimeTask
{
    public sealed class ObsidianNote
    {
        public string RelativePath { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string Signature { get; set; }
    }

    public sealed class ExtractedTaskCandidate
    {
        public string Title { get; set; }
        public string Priority { get; set; }
        public DateTime? DueAt { get; set; }
        public double Confidence { get; set; }
        public string SourcePath { get; set; }
        public string EvidenceLine { get; set; }
    }

    public sealed class KnowledgeSyncResult
    {
        public int NotesScanned { get; set; }
        public int TaskCandidates { get; set; }
        public int DraftsAdded { get; set; }
        public int AutoImported { get; set; }
        public int DuplicatesSkipped { get; set; }
        public int FailedNotes { get; set; }
        public List<TaskDraft> NewDrafts { get; } = new List<TaskDraft>();
        public List<string> Errors { get; } = new List<string>();

        public string BuildSummaryText()
        {
            return $"扫描笔记 {NotesScanned}，提取候选任务 {TaskCandidates}，新增草稿 {DraftsAdded}，自动入象限 {AutoImported}，跳过重复 {DuplicatesSkipped}，失败 {FailedNotes}";
        }
    }

    public sealed class KnowledgeSyncState
    {
        public DateTime LastRunUtc { get; set; } = DateTime.MinValue;
        public Dictionary<string, string> NoteSignatures { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
