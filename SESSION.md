# SESSION.md（追加式，每次更新只加不删）

## [2026-09-22 ~20:10 +08:00] 智能引导域定时器收口（MainWindow 拆分路线图收尾）
- 新增 GuidanceScheduler（与 ReminderService/SyncScheduler 同构的定时器宿主）：
  收口 _smartSystemTimer（场景触发+目标调适+战略导航）与 _taskReminderTimer
  （自适应调参+陈旧任务提醒+卡住检测）两个散落 DispatcherTimer。
- 修掉一个真实崩溃面：旧 TaskReminderTimer_Tick 是 **async void 且无 try/catch**——
  一次 tick 异常直冲 Dispatcher 即崩应用。宿主 SafeTick/SafeTickAsync 统一异常隔离
  （单次失败记日志、定时器存活），窗口只提供 tick 委托。
- MainWindow 六处改动：字段×2 收敛为 _guidanceScheduler、构造（先建再配）、
  两个 tick 改为纯任务体（SmartSystemTickBody / TaskReminderTickBodyAsync）、
  关闭清理并入宿主 Dispose。间隔维持旧硬编码 5 分钟（NormalizeIntervalMinutes 可兜底）。
- 新增 7 项契约测试（GuidanceSchedulerTests）：异常隔离同步/异步两路、
  同步段抛出不外抛、null tick 安全、Dispose 后配置抛、双 Dispose 幂等、间隔规范化。
- 验证：MSBuild 0 错；vstest 194 项 / 192 通过 / 0 失败 / 2 跳过（+7）；
  真实日志字节数前后一致；改动文件 U+FFFD 均为 0。
- 路线图状态：拆 MainWindow 的定时器部分全部完成（数据层 QuadrantStore →
  ReminderService/SyncScheduler → GuidanceScheduler），剩纯 UI 动效定时器留在窗口（按设计）。

## [2026-09-22 ~19:45 +08:00] 稳定性与工程卫生：LLM 重试 + 测试日志隔离 + 更新版本解析
- 背景：用户暂无法实测录音链路，转做可单测验证的路线图遗留项。
- 1) LLM 瞬时失败重试（路线图第 3 项遗留，此前完全没有 retry——会议 60s/次的精修、
  停止后的行动项抽取遇到一次网络抖动就整次丢失）：
  - 新增 LlmRetryPolicy（纯函数可测）：按 LlmService「失败返回 Error 字符串」的契约做文本分类——
    超时/断连/限流/5xx 重试（含中文网络异常文案「发送请求时出错」）；取消/鉴权401/403/解析/配置不重试；
    未知错误保守不重试；**正常内容哪怕含「超时」字样也绝不重试**（防重复计费）。
  - 退避 1s/2s/4s（上限8s+确定性抖动）；等待期取消立即返回（保持不抛异常契约）。
    LlmService.GetCompletionAsync 包装原单次执行为 GetCompletionOnceAsync + ExecuteAsync 重试。
    配置 LlmRetryMaxAttempts（默认3，钳1..6）。既有测试的 mock 错误均为解析类 → 不受重试影响。
- 2) 测试污染用户真实日志（%AppData%\TimeTask\logs\voice-runtime.log 混入大量单测条目，实测抓到）：
  - VoiceRuntimeLog.DetourTo(path) 重定向 + TimeHostSetup（改名 TestHostSetup）[AssemblyInitialize]
    全程序集重定向到 %TEMP%\TimeTask.Tests\logs。
  - **踩坑自纠**：首版 Detour 测试 finally 里 DetourTo(null)，把后面执行的测试类打回真实日志
    （RecordingRetention/ReminderSync 泄漏，靠「真实日志字节数前后一致」验证抓出）——
    finally 必须恢复到 TestHostSetup.TestLogPath。最终验证：全量 187 项跑完真实日志逐字节不变。
- 3) AutoUpdateService.ParseVersion 加固：原严格 Version.TryParse，tag 稍不规范（release-2.0.1、
  「TimeTask v1.2（2026-09-22）」、v1.2.3-beta.1）就抛「无法解析版本号」（用户日志实际发生过）。
  改为正则提取首个 x.y[.z[.r]] + 单数字退化，提取不到才 null。
- 顺手：修 LlmServiceTests 的 CS8602 可空警告。
- 验证：MSBuild 0 错 0 警告（CS8602 消除）；vstest 187 项 / 185 通过 / 0 失败 / 2 跳过（+25 新增）；
  真实日志隔离字节数证明。全部改动文件 U+FFFD 扫描为 0。
- 教训：全局单例状态（日志重定向）的测试，清理逻辑必须恢复到「全局初始态」而非默认值——
  测试间执行顺序不可假设。

## [2026-09-22 ~19:00 +08:00] 修复「一直提示高精度模型准备中」
- 根因（读 voice-runtime.log 定位）：`FunAsrRuntimeManager` 的策略是
  `preferPrebuiltRuntime=True + allowOnlineInstallFallback=False` —— 只认
  data\funasr-runtime-bundle.zip 预置包（本机没有）→ 永远返回
  `prebuilt-runtime-not-ready:bundle-not-found`。而本机 python3.12 + funasr + torch
  早已装好、SenseVoiceSmall 模型也早已缓存（~/.cache/modelscope/models/iic--SenseVoiceSmall，
  注意是 models\ 不是 hub\），旧接入把 manager 当唯一入口，绕过了完全可用的本机环境。
- 修复（重写 FunAsrEngine 的启动链）：
  1. python 候选按序尝试：manager 已就绪的预置包 → FunAsrPythonExe 配置 → PATH python → py 启动器；
     依赖缺失时脚本数秒内退出，自动换下一个候选（自探测，无需预先 pip 检查）。
  2. 就绪等待超时**不再杀进程**：首次模型下载不中断，本轮会话回落 Vosk、worker 继续后台准备，
     之后会话直接复用（watcher 模式，单挂起读取避免 StreamReader 并发读）。
  3. `IsRunning` 语义收紧 = 「进程活着且已 ready」（此前进程活着就算 running，ready 前发请求会
     与 ready 行错位）；识别请求前置 `_workerReady` 守卫。
  4. 应用启动即预热引擎（不等第一场录音才下载模型）。
  5. 脚本路径多级探测：CWD → exe 目录 → exe 上两级（开发布局 bin\Debug）→ exe\scripts\。
  6. 回落文案区分「首次准备中（约230MB，仅需一次）」与「暂不可用」（用 IsPreparing）。
- 实测验证（本机）：
  - worker --server 5 秒内输出 {"ok":true,"event":"ready"}（模型从缓存加载，无需下载）；
  - 一次性模式端到端识别 1 秒静音 WAV → {"ok":true,"text":"嗯。","confidence":0.65}，
    python→funasr→SenseVoice→JSON 协议全链路通。
  - 回归：MSBuild 0 错；vstest 162 项 / 160 过 / 0 败 / 2 跳过。
- 下一步：用户重启应用实测——启动日志应出现「FunASR worker 就绪：python=…」，
  录音时托盘显示「实时转写就绪」且无回落提示。
- 教训：环境探测类逻辑不能「只认一种部署形态」——预置包、本机 pip 环境都是合法形态，
  应按可用性依次尝试；就绪等待超时 ≠ 失败，长耗时初始化（下载/加载）绝不能被超时杀掉。

## [2026-09-22 ~19:30 +08:00] 高精度识别引擎接入（用户反馈：识别能力太弱）
- 目标：Vosk 小模型（40MB）精度不足是收件箱质量差的根因——把 FunASR(SenseVoiceSmall)
  接到主录音链路 ConversationCaptureService，Vosk 降为回落引擎。
- 已完成：
  - 新增 `FunAsrEngine.cs`：常驻 worker 封装（scripts/funasr_asr.py --server，stdin/stdout
    JSON 行协议）；复用 FunAsrRuntimeManager 运行环境引导与 ProcessUtils.KillTree；
    读写超时即重启 worker 自愈；含纯函数助手（JSON 解析/WAV 头构造/分段决策/RMS）。
  - `ConversationCaptureService`：`ConversationCaptureAsrEngine` = auto（默认，优先 FunASR、
    60s 未就绪回落 Vosk，本次录音不受影响）/ funasr（不回落）/ vosk。
    - 泵输出单点分发 FeedAsr：FunAsr 模式喂分段缓冲（12s 硬上限 + 尾部静音≥1s 提前切段，
      ≥3s 起切），临时 WAV 后台识别，结果走 AddTurn（会议状态机/行动抽取链路完全复用）。
    - 引擎未就绪期间音频进既有 pending 缓冲，就绪后回灌（两引擎共用，pre-roll 不丢首字）。
    - 分段缓冲 15 分钟硬上限防 OOM；停止时冲洗剩余分段 + 有界等在途识别（≤15s）。
    - `_turns` 改为每会话换新列表引用（不再是 Clear）：停止后仍在途的识别结果写入旧列表
      （快照持同一引用），新旧会话转写绝不串台。
  - 状态文案引擎中立化（“高精度识别启动中…/实时转写就绪”，不再露 ASR 字样）。
  - 新增 10 项引擎契约测试（选择解析/JSON 行协议/WAV 头/分段决策/RMS）。
  - 回归：MSBuild 0 错；vstest **162 项 / 160 过 / 0 败 / 2 跳过**。
- 环境现状（本机）：python 3.12 + funasr 已装（旧管线实验时备好）；SenseVoiceSmall 模型
  未缓存——首次录音会自动从 modelscope 下载约 230MB（国内源，一两分钟），之后常驻秒开。
- 关键决策：
  - 没走「换 Vosk 大模型」路线：SpeechModelManager 下载无断点续传（FileMode.Create 每次
    重试从头截断），1.4GB 在国内网络基本下不完；且大模型精度仍不及 SenseVoice。
  - worker 跨会话常驻（模型只加载一次），只在服务 Dispose 停机；识别请求串行化（_ioLock）。
  - auto 回落只降级当前会话：运行环境继续后台准备，之后的录音自动升级，无需用户干预。
- 下一步：用户实测——录一场真实会议，确认托盘出现「高精度识别就绪」、转写质量明显提升、
  停止后收件箱行动项数量/质量改善。若接受良好，后续可考虑把旧管线 EnhancedAudioCaptureService
  也切到 FunAsrEngine（消重复）。
- 相关文件：`FunAsrEngine.cs`、`ConversationCaptureService.cs`、`App.config`
  （新增 ConversationCaptureAsrEngine）、`TimeTask.Tests/FunAsrEngineTests.cs`
- 阻塞/风险：模型首次下载期间该场录音回落 Vosk（预期行为）；UI 实测待用户。

## [2026-09-22 ~18:00 +08:00] 收件箱减负 + 录音保留策略（用户反馈轮）
- 目标：解决用户两条反馈——①录音后收件箱太繁琐/概念不懂/不知下一步；②录音无限堆积是隐患。
- 已完成（ActionInboxWindow.xaml/.cs 重排 + RecordingRetention.cs 新增）：
  - 收件箱改为「引导式」：首屏只有一句话结论（时长/是否转成文字/找到几件事）+
    行动项表格 + 一个绿色主按钮「把 N 件事加入任务列表」；勾选数实时联动按钮文案。
  - 全部高级内容折叠并用大白话：「听录音/找录音文件」「文字都说了什么（全文）」
    「会议纪要」；列头改「加入/事项/放到哪/提醒我」；去掉置信度列与
    ASR/LLM/「LLM上下文精炼」等术语；三按钮（全部接受/接受选中/忽略）收敛为
    主按钮+「本次不加了」；成功后不再弹确认框直接关窗。
  - 空态：人话原因卡 + 两个引导按钮（听录音 / 手动补记），不再是一段术语墙。
  - 录音保留：新增 RecordingRetention（默认保留 7 天，App.config
    ConversationCaptureRetentionDays 可调，0=永久保留）；启动时与每场录音结束后
    后台清理过期会话目录与散落 recording_*.wav；收件箱录音区显示保留策略说明。
  - 新增 5 项保留策略契约测试（只清过期/0=禁用/根缺失不抛/报告计数/配置兜底）。
  - 回归：MSBuild 0 错；vstest **152 项 / 150 过 / 0 败 / 2 跳过**。
- 关键决策：
  - 清理判龄用条目 LastWriteTime（正在写的会话永不过期，无并发风险）；只扫
    Recordings 根目录一层，不递归碰用户其他文件；非 recording_*.wav 文件绝不清。
  - 主按钮语义 = 勾选即所见即所得（默认全勾），消除「全部接受 vs 接受选中」歧义。
- 下一步：用户实测新收件箱（录一场→停止→首屏一眼看懂→点绿按钮→主窗口象限顶部可见）；
  之后按 DESIGN_REVIEW 路线图继续（LLM 重试策略 → 音频管线合并）。
- 相关文件：`ActionInboxWindow.xaml(.cs)`、`RecordingRetention.cs`、`App.xaml.cs`、
  `App.config`（新增 ConversationCaptureRetentionDays）、`TimeTask.Tests/RecordingRetentionTests.cs`
- 阻塞/风险：无。UI 视觉效果需用户实测确认。

## [2026-09-22 ~17:00 +08:00] DSH 接力轮：现场核验 + 定时器宿主收敛
- 目标：理解 TimeTask 现状、分析既往改进、按 docs/DESIGN_REVIEW.md 路线图继续。
- 已完成：
  - 现场重建（.workbuddy/memory 三份记忆 + git + DESIGN_REVIEW），交叉验证构建链：
    MSBuild 0 错，vstest 133 项 / 131 过 / 2 跳过（与记忆声称一致）。
  - 检查点提交 `1f65d0a`：把此前两轮全部未入库工作（66 文件，+6381/−937，
    含 AtomicFile/QuadrantStore/ConversationCaptureService 等新文件）保护进版本库；
    同时把 `.workbuddy/` 加进 .gitignore（会话记忆不入公开仓库）。
  - **MainWindow 拆分第二步（本轮主线）**：定时器收敛为两个宿主——
    - 新增 `ReminderService.cs`：到期评估纯函数 `ReminderEvaluator.Evaluate`
      （口径 = IsActive 且 ReminderTime ≤ now，象限顺序即优先级）+ 模态弹窗互斥
      （TryBeginDialog/EndDialog；到期提醒与 ShowFriendlyReminder 智能提醒共用互斥不变量保留）。
    - 新增 `SyncScheduler.cs`：团队同步/知识同步/知识同步防抖三个 DispatcherTimer 收口，
      配置语义可测（区间规范化）、TriggerKnowledgeDebounce 内部跨线程 marshal。
    - MainWindow：删 4 个定时器字段与 `_reminderDialogActive`；`ReminderTimer_Tick`
      改为 `ReminderService_DueRemindersRaised`（展示逻辑留在窗口）；
      `SyncTimer_Tick` → 无参 `TeamSyncTick`；关闭清理/LLM 设置重初始化路径统一走宿主。
    - 新增 `TimeTask.Tests/ReminderSyncHostTests.cs`：14 项契约测试。
  - 回归：MSBuild 0 错；vstest **147 项 / 145 过 / 0 败 / 2 跳过**（唯一警告为既有 CS8602）。
  - DESIGN_REVIEW.md 路线图第 1 项标记定时器收敛完成；本文件即心跳日志。
- 关键决策：
  - 先提交检查点再动重构（42 文件未入库太危险）。
  - 路线图多项中选「定时器收敛」而非「合并音频管线」：录音链路刚经多轮用户实测修复稳定，
    无 UI 实测手段时不冒进重构主链路。
  - 互斥状态放 ReminderService 而非窗口字段：两条提醒路径（到期/智能）必须共享同一互斥。
- 下一步（按路线图收益/风险排序）：
  1. 用户 UI 实测 2026-09-22 轮的三项验证（连录两场/Ctrl+Alt+R 面板/快速连点），见 .workbuddy/memory/2026-09-22.md。
  2. LlmService 剩余：重试策略统一（当前完全没有 retry）。
  3. 合并音频管线（IAsrEngine 化，风险最高，建议在 UI 实测通过后再动）。
  4. 智能引导定时器随 SmartGuidance 一起迁出 MainWindow。
- 相关文件：`ReminderService.cs`、`SyncScheduler.cs`、`MainWindow.xaml.cs`（约 393/449/747/2515/2810/3414/3485/3559/3788/3908/6091 行附近）、`TimeTask.Tests/ReminderSyncHostTests.cs`、`docs/DESIGN_REVIEW.md`
- 阻塞/风险：无。UI 行为（提醒弹窗时序、同步节奏）无自动化覆盖，需按第 1 步实测。
- 构建链备忘：MSBuild=`D:\tools\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`；
  测试工程需先注入 `DOTNET_ROOT/DOTNET_HOST_PATH=C:\Program Files\dotnet`；
  vstest=`...\Common7\IDE\Extensions\TestPlatform\vstest.console.exe`（或直接跑 build_and_test.bat）。
