# Branching Strategy (Small Team)

This repository uses a lightweight Git Flow variant designed for 3-8 collaborators.

## Branches
- `main`: production-ready code only. Every commit on this branch should be releasable.
- `develop`: integration branch for completed work. Default target for feature PRs.
- `feature/<issue-id>-<short-name>`: new features, created from `develop`.
- `fix/<issue-id>-<short-name>`: non-urgent bug fixes, created from `develop`.
- `hotfix/<issue-id>-<short-name>`: urgent production fixes, created from `main`.
- `release/<version>`: optional stabilization branch before a release (for example `release/1.4.0`).

## Naming Rules
- Use lowercase and hyphens: `feature/128-task-reminder-snooze`.
- Prefer issue-driven naming to keep traceability.
- Keep one branch focused on one task only.

## Pull Request Flow
1. Create branch from the correct base (`develop` or `main` for hotfix).
2. Commit in small, reviewable chunks.
3. Open PR early as draft for visibility.
4. Merge only after CI passes and at least one reviewer approves.
5. Use **Squash and merge** by default to keep history clean.

## Merge Directions
- `feature/*` -> `develop`
- `fix/*` -> `develop`
- `release/*` -> `main` and back-merge to `develop`
- `hotfix/*` -> `main` and back-merge to `develop`

## Protection Rules (Recommended)
- Protect `main` and `develop`.
- Require PR (no direct push).
- Require passing checks.
- Require at least 1 approval.
- Require branch to be up to date before merge.

## Suggested Team Cadence
- Daily: developers sync branches with `develop`.
- Weekly: create `release/*` only if a stabilization window is needed.
- Per task: branch lives short (ideally 1-3 days) and is deleted after merge.

## Quick Start Commands
```bash
# Start a feature
git checkout develop
git pull
git checkout -b feature/128-task-reminder-snooze

# Keep branch updated
git fetch origin
git rebase origin/develop

# Finish
git push -u origin feature/128-task-reminder-snooze
```

