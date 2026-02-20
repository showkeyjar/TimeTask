# Knowledge Sync & Capture Guide

## 1) Enable Obsidian task mining
Edit `App.config`:
- `KnowledgeSyncEnabled=true`
- `ObsidianVaultPath=<your obsidian vault path>`

Optional:
- `KnowledgeSyncIntervalMinutes=60`
- `ObsidianIncludeSubfolders=true`
- `ObsidianMaxFilesPerSync=200`
- `KnowledgeRealtimeWatchEnabled=true` (real-time file watch)
- `KnowledgeSyncDebounceSeconds=8`
- `KnowledgeAutoImportEnabled=true`
- `KnowledgeAutoImportMinConfidence=0.90`
- `KnowledgeAutoImportMaxPerRun=6`
- `KnowledgeSmartNotifyEnabled=true`

Then open app Settings menu:
- `📚 笔记任务同步` to run once manually.

If `ObsidianVaultPath` is empty, TimeTask will attempt to auto-discover
the last opened vault from `%APPDATA%\obsidian\obsidian.json`.
When discovered, a passive notification is shown once at startup.

## 2) Task source tracking
Imported drafts carry `SourceNotePath` and become task `SourceTaskID` in CSV as:
- `obsidian:<relative_note_path>`

This allows completion writeback to the original note.

## 2.1) Smart auto-import behavior
- High-confidence extracted tasks are auto-imported into quadrants.
- Lower-confidence tasks remain in draft queue for confirmation.
- Dedupe: existing tasks with same title + same source note are skipped.

## 3) Knowledge capture on completion
When a task is marked completed:
- A recap card is appended to local file: `data/knowledge/YYYY-MM.md`
- If writeback enabled, recap is also appended to:
  - Obsidian inbox note (`KnowledgeObsidianInboxNote`)
  - Source note (`obsidian:<path>`) when available

Managed task block in Obsidian note:

```md
<!-- TIMETASK:BEGIN -->
- [x] task title (completed: 2026-02-20)
<!-- TIMETASK:END -->
```

## 4) Config keys for capture/writeback
- `KnowledgeCaptureEnabled`
- `KnowledgeWritebackEnabled`
- `KnowledgeWritebackToSourceNote`
- `KnowledgeArtifactsPath`
- `KnowledgeObsidianInboxNote`
