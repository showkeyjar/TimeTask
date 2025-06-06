-- Schema for TeamTasks table (PostgreSQL)
CREATE TABLE IF NOT EXISTS TeamTasks (
    TaskID SERIAL PRIMARY KEY,
    SourceTaskID VARCHAR(255) UNIQUE, -- Unique ID from the source system
    TaskDescription TEXT NOT NULL,
    TaskType VARCHAR(255),
    CreationTime TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    CompletionTime TIMESTAMP WITH TIME ZONE,
    CompletionStatus VARCHAR(50) DEFAULT 'Pending', -- e.g., 'Pending', 'InProgress', 'Completed'
    AssignedRole VARCHAR(100),
    -- LastSyncedTime might be better managed by the client application
    -- but can be included if the source system updates it.
    LastUpdatedInSourceDB TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Optional: Add an index for faster lookups on AssignedRole or Status
CREATE INDEX IF NOT EXISTS idx_teamtasks_assigned_role ON TeamTasks(AssignedRole);
CREATE INDEX IF NOT EXISTS idx_teamtasks_completion_status ON TeamTasks(CompletionStatus);
CREATE INDEX IF NOT EXISTS idx_teamtasks_creation_time ON TeamTasks(CreationTime DESC);
