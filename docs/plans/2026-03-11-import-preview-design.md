# Import Preview Design (2026-03-11)

## Goal
Add a pre-import preview and selection step for project plan imports so users can review and choose tasks before they enter the draft box.

## Scope
- Applies to Word/Excel import flow from Draft Viewer.
- Shows key columns: task title, owner, start date, reminder time, confidence.
- Supports select all, select none, and import selected.

## Data Flow
1. User selects file in Draft Viewer.
2. Parser extracts candidates and LLM refines missing fields.
3. Build preview list with `IsSelectable` and `IsSelected` flags.
4. Preview window displays items and user confirms.
5. Selected items are converted to `TaskDraft` entries.

## Selection Rules
- `IsSelectable = true` if owner matches user alias or owner is empty.
- `IsSelectable = false` if owner clearly mismatches. Those rows are grayed out.
- Default selection mirrors `IsSelectable`.

## UI
- Preview window with DataGrid and checkbox column.
- Summary line: total, selectable, filtered, failed, LLM refined.
- Actions: Select All, Select None, Import Selected, Cancel.

## Error Handling
- If no candidates, show "no tasks" message.
- If user selects none, prompt to choose at least one task.
- LLM failures fall back to structured parse.

## Testing
- Excel and Word imports with mixed owners.
- Rows without owner or start date.
- LLM timeout fallback.
- Selection behaviors and disabled rows.
