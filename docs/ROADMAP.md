# TimeTask Roadmap

This is a short-term roadmap focused on practical delivery.

## Next 30 Days
- Stabilize release packaging for every tag (`v*`) with downloadable zip assets. — **Done**: release.yml is now tag-driven (push to master runs CI only); tag/assembly version mismatch refuses to publish; CHANGELOG.md is auto-appended by the pipeline.
- Expand i18n coverage from main window to all major dialogs. — **Done**: the audit script `scripts/check_i18n.ps1` found **75 hardcoded CJK strings in 5 XAML files** and drove them to **0** via `{loc:}` + bilingual resx (all five windows migrated: TaskStatisticsWindow, LearningPlanManagerWindow, ActionInboxWindow, SetLearningPlanWindow, MainWindow; 148 new keys added to `Resources.resx` / `Resources.en-US.resx`). The scanner catches CJK anywhere in an attribute value or element body (including emoji-prefixed strings). CI enforces a ratchet (`docs/i18n-baseline.txt`): the count may only go down, and it now sits at 0. Code-behind already uses `I18n.T()` widely (250+ sites).
- Improve first-run guidance and defaults for voice + reminders.
- Add lightweight diagnostics for common setup failures. — **Done**: `TimeTask.exe --diagnostics` (optional `--quiet`) produces a full setup/config report.

## Next 60 Days
- Better onboarding flow for long-term goals and learning plans.
- Improve task decomposition quality and fallback behavior.
- Add more user-facing examples and templates.

## Community Priorities
If you want to influence priorities:
- Open a feature request in Issues.
- Vote/react on existing requests.
- Share your real usage scenario.
