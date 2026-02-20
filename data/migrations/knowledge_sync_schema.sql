-- TimeTask Knowledge Sync SQLite Schema (MVP)
-- Designed for local-first storage and auditability.

CREATE TABLE IF NOT EXISTS notes (
    id TEXT PRIMARY KEY,
    source TEXT NOT NULL,
    source_note_id TEXT,
    path TEXT NOT NULL,
    title TEXT NOT NULL,
    content TEXT,
    content_hash TEXT,
    updated_at TEXT,
    indexed_at TEXT
);

CREATE TABLE IF NOT EXISTS tasks (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    description TEXT,
    status TEXT NOT NULL DEFAULT 'inbox',
    priority INTEGER NOT NULL DEFAULT 3,
    due_at TEXT,
    project TEXT,
    source_note_id TEXT,
    confidence REAL,
    assignee TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS task_events (
    id TEXT PRIMARY KEY,
    task_id TEXT NOT NULL,
    event_type TEXT NOT NULL,
    before_json TEXT,
    after_json TEXT,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS knowledge_artifacts (
    id TEXT PRIMARY KEY,
    type TEXT NOT NULL,
    title TEXT NOT NULL,
    body TEXT NOT NULL,
    linked_task_ids TEXT,
    source_note_ids TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sync_state (
    connector TEXT PRIMARY KEY,
    cursor TEXT,
    last_sync_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_notes_source_updated ON notes(source, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_tasks_status_priority ON tasks(status, priority);
CREATE INDEX IF NOT EXISTS idx_task_events_task_time ON task_events(task_id, created_at DESC);
