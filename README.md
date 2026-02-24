# TimeTask

A Windows desktop task manager built around the Eisenhower Matrix (Important/Urgent), with reminders, long-term planning, voice capture, and optional AI assistance.

[![Release](https://img.shields.io/github/v/release/showkeyjar/TimeTask)](https://github.com/showkeyjar/TimeTask/releases/latest)
[![License](https://img.shields.io/github/license/showkeyjar/TimeTask)](https://github.com/showkeyjar/TimeTask/blob/HEAD/LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6)](https://github.com/showkeyjar/TimeTask)

English | [简体中文](README.zh-CN.md)

## Download In 30 Seconds
1. Go to [Latest Release](https://github.com/showkeyjar/TimeTask/releases/latest).
2. Download `TimeTask-win-x64.zip`.
3. Unzip and run `TimeTask.exe`.

If this project is useful, please star it to help more users find it.

## Why TimeTask
- Focus by priority: manage tasks in 4 quadrants.
- Close the loop: reminders + overdue nudges + review.
- Connect goals to execution: long-term goals and learning plans.
- Capture quickly: voice to task draft.
- Optional AI support: decomposition and suggestions.

## Main Features
- Four-quadrant task management and quick add.
- Reminder scheduling and overdue alerts.
- Task decomposition and action suggestions (optional LLM).
- Long-term goals and learning plan management.
- Voice recognition to task drafts.
- Data import/export (JSON).
- Skill management (enable/disable, import/export).

## Screenshots
| Main | Reminder | LLM Settings |
| --- | --- | --- |
| <img src="docs/p1.png" alt="Main task matrix view" width="320" /> | <img src="docs/p3.png" alt="Reminder settings window" width="320" /> | <img src="docs/p4.png" alt="LLM settings window" width="320" /> |

| Goals | Drafts | |
| --- | --- | --- |
| <img src="docs/p2.png" alt="Long-term goal settings" width="320" /> | <img src="docs/p5.png" alt="Task draft window" width="320" /> | |

## Build From Source
- Environment: Windows + Visual Studio + .NET Framework 4.7.2 (WPF).
- Open `TimeTask.sln`.
- Build and run.

## Optional Configuration
- LLM settings in `App.config` (`OpenAIApiKey`, `LlmProvider`, `LlmApiBaseUrl`, `LlmModelName`).
- Voice/FunASR can use local runtime bundle: `data/funasr-runtime-bundle.zip`.
- Auto update can check GitHub Releases on startup.

## Contributing
- Bug reports and ideas: [Issues](https://github.com/showkeyjar/TimeTask/issues)
- Feature discussions: [Discussions](https://github.com/showkeyjar/TimeTask/discussions)
- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)

## License
MIT - see [LICENSE](LICENSE).

