# TimeTask Roadmap

This is a short-term roadmap focused on practical delivery.

## Next 30 Days
- Stabilize release packaging for every tag (`v*`) with downloadable zip assets. — **Done**: release.yml is now tag-driven (push to master runs CI only); tag/assembly version mismatch refuses to publish; CHANGELOG.md is auto-appended by the pipeline.
- Expand i18n coverage from main window to all major dialogs. — **In progress, measurable**: audit script `scripts/check_i18n.ps1` found **75 hardcoded CJK strings in 5 XAML files** (TaskStatisticsWindow 33, LearningPlanManagerWindow 17, ActionInboxWindow 12, SetLearningPlanWindow 8, MainWindow 5). CI enforces a ratchet (baseline `docs/i18n-baseline.txt`): the number may only go down. Code-behind already uses `I18n.T()` widely (250+ sites); the `{loc:}` XAML markup extension exists but is not used yet.
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
