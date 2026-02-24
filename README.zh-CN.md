# TimeTask 任务管理助手

将任务按“重要/紧急”四象限管理，支持提醒、目标规划、语音草稿与智能建议。

[![Release](https://img.shields.io/github/v/release/showkeyjar/TimeTask)](https://github.com/showkeyjar/TimeTask/releases/latest)
[![License](https://img.shields.io/github/license/showkeyjar/TimeTask)](https://github.com/showkeyjar/TimeTask/blob/HEAD/LICENSE)

[English](README.md) | 简体中文

适用人群：希望把“任务执行 + 长期目标 + 提醒复盘”放在一个桌面工具里的 Windows 用户。

## 项目状态
- 路线图: [docs/ROADMAP.md](docs/ROADMAP.md)
- 更新日志: [CHANGELOG.md](CHANGELOG.md)
- 贡献指南: [CONTRIBUTING.md](CONTRIBUTING.md)
- 安全策略: [SECURITY.md](SECURITY.md)
- 社区行为准则: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)

## 30 秒开始使用

1. 打开 [Releases](https://github.com/showkeyjar/TimeTask/releases/latest) 下载 `TimeTask-win-x64.zip`。
2. 解压后运行 `TimeTask.exe`。
3. 首次启动后按需开启提醒、语音或 LLM 功能。

> 如果你愿意点个 Star，会帮助项目被更多同类用户看到。

**环境**：Windows + WPF（C#）

## 从源码运行（开发者）
1. 克隆仓库。
2. 使用 Visual Studio 打开 `TimeTask.sln` 并编译运行。
3. 双击 `autorun.reg` 可加入开机自启动（可选）。

## 为什么用 TimeTask
- 四象限任务管理：快速区分重要/紧急，减少“忙但无进展”。
- 任务提醒闭环：定时提醒、过期提醒与反馈数据沉淀。
- 目标联动任务：长期目标、学习计划和日常任务关联。
- 语音到任务：语音草稿可自动入象限或进入草稿箱。
- 智能建议（可选）：可接入 LLM 做任务分解和行动建议。

## 核心功能
- 四象限任务管理与快捷添加。
- 定时提醒与过期任务提醒。
- 任务分解与行动建议（可选 LLM）。
- 长期目标与学习计划。
- 语音识别生成任务草稿。
- 数据导入/导出与统计报告导出。
- Skills 管理（启用/停用、反馈统计、导入/导出）。

## 数据导入/导出
- 入口：右上角设置菜单 `📥 导入数据` / `📤 导出数据`。
- 导出文件为 JSON，包含四象限任务、长期目标与学习里程碑。
- 导入会覆盖当前任务与目标数据，请谨慎操作。

## Skills 管理
- 入口：右上角设置菜单 `Skill 管理`。
- 支持导入/导出启用的技能清单（JSON）。
- 建议先全选体验，再根据反馈调整。

## 界面截图
| 主界面 | 任务提醒 | LLM 设置 |
| --- | --- | --- |
| <img src="docs/p1.png" alt="主界面四象限任务管理" width="320" /> | <img src="docs/p3.png" alt="任务提醒设置窗口" width="320" /> | <img src="docs/p4.png" alt="LLM 设置窗口" width="320" /> |

| 目标管理 | 草稿箱 | |
| --- | --- | --- |
| <img src="docs/p2.png" alt="设置长期目标窗口" width="320" /> | <img src="docs/p5.png" alt="任务草稿窗口" width="320" /> | |

## LLM 配置（可选）
- 配置文件：`App.config` 的 `<appSettings>`。
- 关键项：`OpenAIApiKey`、`LlmProvider`、`LlmApiBaseUrl`、`LlmModelName`。
- 示例（Zhipu）：

```xml
<appSettings>
  <add key="OpenAIApiKey" value="YOUR_ZHIPU_API_KEY" />
  <add key="LlmProvider" value="zhipu" />
  <add key="LlmApiBaseUrl" value="https://open.bigmodel.cn/api/paas/v4/" />
  <add key="LlmModelName" value="glm-4" />
</appSettings>
```

## 语音与 FunASR（可选）
- 语音模型默认使用本地引擎或 FunASR 子进程。
- FunASR 可使用预置运行时包 `data/funasr-runtime-bundle.zip`。
- 语音草稿可自动入象限或进入草稿箱。

## 自动更新（新）
- 启动时会后台检查更新（可在 `App.config` 关闭）。
- 默认从 GitHub Releases 检查最新版本并下载 zip 资产。
- GitHub 配置项：
  - `AutoUpdateEnabled`：是否启用自动更新。
  - `AutoUpdateGithubOwner`：GitHub 仓库 owner。
  - `AutoUpdateGithubRepo`：GitHub 仓库名。
  - `AutoUpdateGithubAssetNameContains`：可选，按资产名关键字筛选 zip。
  - `AutoUpdateGithubIncludePrerelease`：是否允许预发布。
  - `AutoUpdateCheckTimeoutSeconds`：检查超时（秒）。
  - `AutoUpdateDownloadTimeoutSeconds`：下载超时（秒）。
- 可选兜底（不使用 GitHub 时）：
  - `AutoUpdateManifestUrl`：更新清单地址（HTTP/HTTPS）。
  - manifest JSON 示例：

```json
{
  "version": "1.1.0",
  "downloadUrl": "https://example.com/releases/TimeTask-1.1.0.zip",
  "sha256": "可选，更新包SHA256"
}
```

- 发布包要求：`downloadUrl` 指向的 zip 解压后需包含 `TimeTask.exe`（可在根目录或单层子目录中）。

## 自我进化能力（本地）
- 基于提醒反馈自动调整推荐策略。
- 仅存本地 `%AppData%/TimeTask/user-profile.json`。

## 注意事项
- 数据文件位于 `data/`，语音运行日志位于 `%AppData%/TimeTask/logs/`。
- 如需禁用 LLM 或语音功能，可在 `App.config` 中关闭对应开关。

## 参与与反馈
- 提交问题与建议: [Issues](https://github.com/showkeyjar/TimeTask/issues)
- 功能讨论: [Discussions](https://github.com/showkeyjar/TimeTask/discussions)
- 贡献指南: [CONTRIBUTING.md](CONTRIBUTING.md)
