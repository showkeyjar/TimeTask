# TimeTask 设计评审与改进报告

> 评审范围：全仓代码走读（主窗口 / 数据层 / 语音-LLM 链路）+ 三轮独立子代理交叉审查。
> 本轮改进已全部落地并通过构建与测试：**121 项测试，119 通过 / 0 失败 / 2 跳过**。
> 2026-09-22 追加轮：录音会话硬化 + **定时器宿主收敛**（ReminderService / SyncScheduler），**147 项测试，145 通过 / 0 失败 / 2 跳过**。

## 一、项目现状概述

TimeTask（QuestOS）是 .NET Framework 4.7.2 上的 WPF 桌面应用：以四象限（重要×紧急）任务矩阵为核心，围绕它长出了语音采集（Vosk / FunASR / NAudio 三条管线）、LLM 集成（Betalgo OpenAI + 智谱 HTTP）、行为画像、知识同步（Obsidian）、周回顾、学习计划等子系统。数据全部落在本地 CSV / JSON。

功能密度和"个人操作系统"的野心是它的优点，也是它大部分结构问题的根源：**所有子系统都直接挂在 MainWindow 这一个类上**。

## 二、难用 / 不完善的设计（按严重度排序）

### 1. 数据层：写到一半断电 = 数据丢失（已修复）

- **四处散落的路径拼接**：`Path.Combine(exe目录, "data", …)` 出现在 MainWindow、ActionInboxWindow、TaskStatisticsWindow、DraftViewerWindow、EnhancedAudioCaptureService 等处。程序文件与用户数据混在安装目录，绿色版/安装版行为不一致。
- **17 个存储类各自 `File.WriteAllText`**：无临时文件、无备份。进程在写入中途被杀（更新、崩溃、关机）就留下半个 JSON/CSV；而 `ReadCsv` 见到残行返回 null，上层直接把整个文件当空处理——**损坏等于静默清零**。
- **CSV 读写是"自产自销"的方言**：自定义逗号拼接 + 自定义切分，遇到含逗号/换行/引号的任务文本就产生错位行，残行还会被当作真任务显示出来。

**本轮修复**：新增 `AtomicFile`（临时文件 + `File.Replace` + `.bak` 备份，全部 17 个存储类接入）；新增 `AppPaths`（exe 旁 `data\` 存在则用之，否则回落 `%AppData%\TimeTask\data`，全仓 20+ 处引用统一收口）；`ReadCsv` 重写为逐行解析，坏行跳过并记日志，不再注入假任务；CSV 单元格转义（换行→空格、逗号→`;;;`），四个象限写入全部走 `AtomicFile`。

### 2. MainWindow 巨类（6300+ 行）：结构与生命周期双重问题

- **关闭时只清理了 5/12 个定时器**：`_reminderTimer`、`taskReminderTimer`、`_draftBadgeTimer`、`_smartSystemTimer`、`_syncTimer`、`_meetingToastTimer` 泄漏；`CaptureService`（App 级单例）上挂的三个事件不退订，**每次开-关主窗口就永久泄漏一个 MainWindow 实例**（含全部控件树）。
- **写盘风暴**：四个象限的 `SelectionChanged` 每次单击都全量重写 CSV——加载时 `ItemsSource` 重置还会级联触发；`location_Save` 在拖动窗口时**每像素写一次 Settings**。
- **帮助对话框说谎**：宣传 Ctrl+S / Del / Tab 切换象限，实际只有 F1 / Ctrl+N / Ctrl+F / Ctrl+E / F5 / Escape；已实现的 Ctrl+E、F5 反而没写。

**本轮修复**：`MainWindow_Closed` 统一停掉全部 12 个定时器并退订采集事件；`SelectionChanged` 改为 400ms 防抖 + 加载期间（`_isLoadingGrids`）完全不触发；窗口位置 500ms 防抖 + 关闭时兜底刷新；**实现**了 Ctrl+S（保存全部象限）与 Del（删除选中任务，复用按钮的确认+行为记录逻辑），帮助文本与实现对齐。

### 3. 进程级健壮性缺失（已修复）

- 后台线程 / Task 的未处理异常直接闪退，用户连"发生了什么"都无法得知；没有单实例保护，双开会互相覆盖 CSV / JSON（后写者赢，丢掉对方全部改动）。

**本轮修复**：`App.OnStartup` 挂上三重兜底（`DispatcherUnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException`，统一写 `VoiceRuntimeLog` 并弹窗告知日志路径）；`Local\TimeTask_SingleInstance` 互斥量 + 重复启动友好提示。

### 4. 语音 → LLM → 任务链路

- **三条并行音频管线**（`AudioCaptureService` / `EnhancedAudioCaptureService` / `ConversationCaptureService`）职责重叠、各自维护静音检测与设备管理，启动失败时的回落逻辑互相纠缠。
- **`LlmService` 是 800+ 行的上帝类**：连接串解析、提示词模板、响应解析、意图识别混在一起；所有网络调用没有 `CancellationToken`，界面上的"取消"按钮实际取消不了任何东西。
- **API Key 明文存配置文件**，且会被日志打出来；提供商靠 URL 字符串嗅探区分。
- FunASR 以外部进程方式拉起，异常路径上存在僵尸进程。

### 5. 其他值得注意的

- `SkillManagementService` 直接写 exe 旁边的 `TimeTask.exe.config`（Program Files 下会静默失败）。
- 损坏配置 = 整个文件重置为默认，用户自定义全部丢失（无备份、无提示）。
- 国际化混用：`I18n.T(...)` 与硬编码中英文并存，同一窗口里两种语言随机出现。

## 三、本轮已落地的改进清单

| 类别 | 内容 |
|---|---|
| 测试修复 | 15 个失败测试全部修复：`ParseReminderResponse` 标签感知切片、`ParseClarityResponse`/`ParseDecompositionResponse` 部分解析回退、`TaskTextQualityHelper` 基于原始文本判断、`IntentRecognizer` 去掉"上午/下午"误判紧急、排序测试时间分辨率抖动 |
| 原子写入 | `AtomicFile` + 17 个存储类 + 4 个 CSV 写入全部接入（临时文件 + `File.Replace` + `.bak`） |
| 路径统一 | `AppPaths.DataDir` 收口全部数据目录拼接 |
| CSV 硬化 | 逐行解析、坏行跳过记日志、单元格转义 |
| 生命周期 | 12 个定时器全停、采集事件退订、关闭清理失败不再无声吞掉 |
| 写盘防抖 | 象限选择 400ms 防抖（加载期不触发）、窗口位置 500ms 防抖 + 关闭兜底 |
| 进程健壮性 | 三重全局异常兜底 + 单实例互斥 |
| 快捷键 | 实现 Ctrl+S / Del；帮助文本与实现对齐（补 Ctrl+E、F5，删掉虚假的 Tab 切换象限） |
| **损坏自动回退** | 新增 `JsonStore`：JSON 主文件损坏（截断/坏字节/解析失败）自动回退 `.bak`（AtomicFile 每次写入都会留上一代备份），13 个 JSON 存储读取点接入；`ReadCsv` 在主文件有坏行且备份更干净（好行数不减少）时回退备份——**「损坏=清零」变成「损坏=回退一代」** |
| **配置落点修正** | `SkillManagementService` 不再写 exe 旁的 `TimeTask.exe.config`（Program Files 下静默失败），改存用户数据目录 JSON，旧 appSettings 值只读迁移 |
| **密钥脱敏** | LLM 请求日志不再输出 API Key 前 5 字符，只记录是否加载与长度 |
| **僵尸进程治理** | 新增 `ProcessUtils.KillTree`（`taskkill /T /F` 递归终止进程树）：FunASR 引导 bootstrap 超时/取消、分段推理超时、常驻 worker 停止，四处全部改用进程树终止——修复取消等待时 Dispose 不杀进程导致的 python 孤儿堆积 |
| **解析器拆分** | 四个 LLM 响应解析方法拆到 `LlmResponseParsers`（纯函数、可独立测试），`LlmService` 保留转发签名兼容既有测试 |
| **韧性测试** | 新增 7 项契约测试：AtomicFile 二次写入必产 .bak、JsonStore 损坏回退/健康不回退/无文件返 null、ReadCsv 备份回退不丢好行 |
| **LLM 可取消** | `GetCompletionAsync` 新增 `CancellationToken` 重载；智谱 HTTP 路径真网络层取消；Betalgo 路径（SDK 不收 token）用 WhenAny 竞速实现调用方立即返回，被弃请求以 HttpClient 超时为上界。`ConversationCaptureService` 拆卸时取消在途精修请求（放在 Dispose 而非 Stop——停止后还有最终 NLP 抽取要用 LLM） |
| **API Key 加密存储** | 新增 `SecureApiKeyStore`（DPAPI CurrentUser + 附加熵，落用户数据目录）：首次读到配置明文自动迁入加密存储并尽力清明文；设置界面读写全走加密存储；启动 Key 检查、`LoadLlmConfig` 优先读加密副本（含「存储被删而配置只剩标记」的边界处理） |
| **提醒弹窗风暴治理** | `TaskReminderWindow` 是模态的且 `ShowDialog` 会泵消息——定时器在弹窗打开期间照样触发，N 个到期提醒会堆叠 N 个模态窗互相卡死。引入 `_reminderDialogActive` 互斥：同一时刻至多一个模态提醒窗，其余到期提醒降级为被动气泡并按 tick 节奏依次弹出 |
| **HttpClient 复用** | 智谱路径此前每次调用 `new HttpClient`——TIME_WAIT 套接字持续堆积，长会话/高频调用最终「再也连不上」。改为共享单例客户端 + 每请求认证头/超时（换 Key 后也不会串号） |
| **PromptTemplates 拆分** | 10 个提示词模板常量拆到 `LlmPromptTemplates`（纯数据），便于统一审阅措辞与后续本地化；`LlmService` 全部引用已限定 |
| **LLM 日志截断** | 智谱请求/响应正文不再全量落日志（含用户对话原文，是隐私泄露面），截断到 200/400 字符仅供排查 |
| **QuadrantStore 提取**（拆 MainWindow 第一步） | 四象限持久化语义唯一 Owner：Load/Save/InsertTop（顶部插入+全象限评分）/DeleteFromAll/Rescore；ActionInbox 接受行动项、主窗口加载与跨象限拖拽评分全部收口，消除规则漂移；支持测试注入数据目录，新增 5 项语义契约测试 |
| **定时器宿主收敛**（拆 MainWindow 第二步） | 提醒域迁入 `ReminderService`（到期评估纯函数化 `ReminderEvaluator` + 模态弹窗互斥收口，到期提醒与智能提醒共用互斥不变量保留）；同步域迁入 `SyncScheduler`（团队同步/知识同步/防抖三定时器 + 跨线程 marshal）；MainWindow 删除 4 个散落定时器字段，关闭清理/重初始化路径统一走宿主；新增 14 项契约测试 |

## 四、建议路线图（部分已实施，其余按收益/风险排序）

1. **拆 MainWindow**（最大收益，改动最大）：~~数据层第一步~~ **已完成**（`QuadrantStore`：四象限 CSV 读写/增删/评分收口）；~~定时器收敛为 ReminderService / SyncService 两个宿主~~ **已完成**（`ReminderService` + `SyncScheduler`，到期评估/弹窗互斥/防抖均有契约测试）；剩余：智能引导类定时器（`_taskReminderTimer`/`_smartSystemTimer`，逻辑与窗口交互状态耦合较深，宜连同 SmartGuidance 一起迁）与纯 UI 动效定时器（保持留在窗口），UI 只留绑定与命令。
2. **合并音频管线**：以 `ConversationCaptureService` 为唯一入口，内部组合设备管理 + ASR 引擎（Vosk/FunASR 做成可替换的 `IAsrEngine`），删除另两条管线及其回落链。*（未实施）*
3. **LlmService 拆分**：~~响应解析器拆分~~ **已完成**；~~调用可取消~~ **已完成**；~~Key 加密存储~~ **已完成**；~~HttpClient 复用~~ **已完成**；~~PromptTemplates 拆分~~ **已完成**；~~瞬时失败重试（LlmRetryPolicy：超时/断连/限流/5xx 自动重试，取消/鉴权/解析/配置不重试，正常内容绝不重试）~~ **已完成**。剩余：连接池监控（收益低，观察中）。
4. ~~**配置损坏策略**~~ **已完成**：`JsonStore` / `ReadCsv` 读取失败先尝试 `.bak` 再考虑重置，并记录警告日志。
5. **国际化收口**：新增字符串一律走 `I18n`，硬编码的逐步迁移。*（未实施）*

## 五、回归验证

- MSBuild（VS 2022，Debug）：0 错误。
- `dotnet test --no-build`：**131/133 通过，0 失败，2 跳过**（跳过项需要真实 LLM 连接串，属预期）。
  - 含 7 项数据韧性契约测试（AtomicFile 备份、JsonStore 回退、ReadCsv 备份回退不丢行）
  - 含 5 项 QuadrantStore 语义契约测试（顶部插入+评分、跨象限删除、边界校验）。
- 2026-09-22 追加轮后：**145/147 通过，0 失败，2 跳过**（新增 14 项 ReminderService/SyncScheduler 契约测试）。
