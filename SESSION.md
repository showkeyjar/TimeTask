# SESSION.md（追加式，每次更新只加不删）

## [2026-09-24 ~17:30 +08:00] i18n 真迁移第一批：两个窗口全 loc 化（75 → 34）
- 本批迁移（XAML 全部硬编码中文 → {loc:Loc} + 双语 resx，代码后置消息同步 I18n.T/Tf）：
  - SetLearningPlanWindow：8 处 XAML + 5 条校验消息；复用 Button_Cancel/SetGoal_TitleInputError。
  - TaskStatisticsWindow（最大户 33 处）：XAML 全量 + DetermineQuadrant 重构为返回象限号
    1..4（原中文字符串当字典键，与展示耦合），象限名复用既有 Quadrant_* 键；导出报告/
    推荐/报错文案全部走资源键；顺手修除零（UpdateTaskTypeAnalysis 空任务列表直接返回）。
  - 新增 61 个资源键 × 双语（resx 443 → 504）。
- 新增 LocalizedWindowSmokeTests：STA 线程真实实例化两个窗口（BAML 加载 + LocExtension
  ProvideValue + 构造器数据装载全链路），并断言标题解析为资源值而非键名回退。
  踩坑：窗口归 STA 线程所有，标题必须在 STA 线程内读出（跨线程访问 DispatcherObject
  抛 InvalidOperationException，首版即栽在此）。
- 修复预存损坏：resx 文件双重 BOM（此前某轮编辑把 U+FEFF 字符与编码器 BOM 叠加，
  strict XmlDocument.Load 直接报 Line 1 无效——默认 zh-CN 下 ResourceManager 从未走到
  英文资源所以无人发现；英文包此前是否受影响待 UI 验证，现已结构性修复）。
- 验证：dev_check 全绿（**210 项 / 208 过 / 0 败 / 2 跳过**，含 2 项新窗口冒烟）；
  编码门 0 错；i18n 棘轮 75 → 34（LearningPlanManager 17 / ActionInbox 12 / MainWindow 5）。
- 教训：PowerShell `script | Select-Object -First N` 会提前终止上游脚本——棘轮块在
  脚本末尾时会被截断跳过（本次基线没更新差点溜进提交），管道截取一律先存变量再筛选。

## [2026-09-24 ~15:20 +08:00] 自动化第三轮：i18n 审计棘轮 + 孤儿文件清理
- 1) scripts/check_i18n.ps1：XAML 硬编码中文审计器（ROADMAP「i18n 覆盖到全部对话框」的可度量落地）。
  发现现状：代码后置 I18n.T() 覆盖广（250+ 处），但 XAML 层 {loc:} 标记扩展 0 使用——
  XAML 里硬编码的中文永远不随语言切换。基线：**75 处 / 5 个文件**
  （TaskStatisticsWindow 33、LearningPlanManagerWindow 17、ActionInboxWindow 12、
  SetLearningPlanWindow 8、MainWindow 5）。脚本纯 ASCII（免 ps1 BOM 问题），只扫 XAML——
  .cs 里大量中文是日志/LLM 提示词等有意为之，扫代码纯噪音。
- 2) 棘轮机制（-Ratchet docs/i18n-baseline.txt）：数量只许降不许升，降了自动更新基线；
  CI 接入后 push 事件自动回提新基线（[skip ci] 防循环；PR 事件只校验不回推）。
  双向已测：74→75 FAIL（exit 1）、75==75 PASS。
- 3) 清理孤儿文件 BackupManagerWindow.xaml：有 x:Class 但代码文件不存在、不在 csproj、
  从未参与编译——git rm。
- 下一步（真正的迁移工作，非本轮范围）：按窗口逐个把 75 处硬编码中文改 {loc:Key} +
  双语 resx（TaskStatisticsWindow 收益最大）；迁移后跑 check_i18n.ps1 基线自动下降。
- 验证：YAML 复验通过；棘轮升降双向行为正确；i18n 扫描 22 个 XAML 稳定输出。

## [2026-09-24 ~15:10 +08:00] 自动化第二轮：本地部署自动化 + 冒烟门 + 发布卫生 + 工具链修补
- 1) scripts/deploy_local.ps1：自动化 2026-09-23 手工做的「部署新构建到 D:\tools\TimeTask」——
  构建 Release → 优雅停实例（CloseMainWindow→10s→强杀，数据全原子写故强杀安全）→
  robocopy 合并拷贝（排除 data/Recordings/logs/TestResults，绝不删除目标文件）→ 重启 →
  --diagnostics 自检验证。-DryRun/-NoBuild/-NoRestart 三开关；DryRun 已演练通过。
- 2) dev_check.ps1 新增 -Smoke：质量门末尾真实启动构建产物跑 --diagnostics --quiet，
  exit 0 才算过（已验证通过）——「能构建」与「能启动且自检干净」从此都是门禁。
- 3) 发布卫生（release.yml）：打包时剥离 data\strategy、data\adaptive、data\weekly_reviews、
  Recordings、logs 与全部 *.bak——本地跑过 Release exe 的开发数据（周回顾/画像/快照）
  不再可能混进发布 zip。
- 4) build_local.bat 修工具链探测（补 VS18 D:\tools 路径与常规 Program Files 路径，
  原版只探测 ProgramFiles(x86) 在本机必失败）+ 指向 dev_check.ps1。
- 5) 三个 workflow/dependabot YAML 已用 PyYAML 语法校验通过。
- 发现待用户决策：D:\tools 实例当前运行的还是 09-23 构建（不含托盘图标/显示桌面保护/
  --diagnostics），deploy_local.ps1 就绪但停-启实例需用户确认时机；本地提交未推送，
  push 后新 CI（全量测试+编码门）首跑待观察。
- 验证：dev_check -Smoke 全绿（208 项测试 / 206 过 / 0 败 / 2 跳过）；deploy DryRun 正常；
  编码门 changed 模式 0 错。

## [2026-09-24 ~15:00 +08:00] 工程自动化轮：一键质量门 + 编码守卫 + tag 驱动发布 + --diagnostics 自检
- 背景：用户要求「分析项目还有哪些工作可以自动化，尽量自动化」。审计发现每轮人工成本
  最高的四件事：①构建+测试+「U+FFFD 均为 0」+真实日志隔离验证（SESSION.md 每轮手工记录）；
  ②编码乱码反复发生（EncodingRepair*.cs×3、manual_repair.ps1 的由来）；③CHANGELOG 停更
  （2026-02-09）而 release 每次 push 都发版（build.N 噪音 tag 干扰应用内更新器）；
  ④用户排障只能口述（ROADMAP 的「轻量诊断」一直未做）。
- 1) scripts/check_encoding.ps1 编码守卫：U+FFFD / 非法 UTF-8 / bat 中文 / ps1 含中文缺 BOM
  （PS 5.1 按 ANSI 误读解析失败，本轮实测复现两次）四类检查，-Mode changed|full。
  首跑即抓到 4 处存量问题并已修复：FunAsrRuntimeManager.cs 注释「进程树」损坏×3、
  build_test.bat 中文（cmd 代码页必乱码→改写为 ASCII）、repair_encoding/manual_repair.ps1 缺 BOM、
  autorun.reg 旧键名 CCtrl→TimeTask。
- 2) scripts/dev_check.ps1 一键质量门：自动探测工具链（VS18 专属路径+常规 VS2022，
  可用 TIMETASK_MSBUILD/TIMETASK_VSTEST 覆盖）→ 构建主/测试工程（自动注入 DOTNET_ROOT）→
  vstest 全跑并解析计数 → 编码守卫全仓 → 真实日志隔离校验（应用正在运行时自动跳过该
  不变量——本轮发现用户 D:\tools\TimeTask 实例常驻写同一日志，旧检查在活机上必误报）。
- 3) git 钩子（scripts/install_hooks.ps1 + .githooks/pre-commit，core.hooksPath 方式随仓库
  版本化）：提交前自动跑编码守卫 changed 模式。
- 4) 发布流水线重构（.github/workflows/release.yml）：纯 tag(v*) 驱动（ROADMAP 明确目标），
  tag 与 AssemblyFileVersion 不一致拒发（防「忘升版本」发错包），发布后自动把
  「上一 tag..本 tag」提交清单回写 CHANGELOG.md（[skip ci] 防死循环，回写失败不标红）。
  CI（dotnet-desktop.yml）则默认跑全部测试+编码门（原测试默认关）。新增 dependabot.yml。
- 5) scripts/set_version.ps1：一次改齐 AssemblyVersion/AssemblyFileVersion + CHANGELOG 草稿段。
- 6) TimeTask.exe --diagnostics（可选 --quiet）：无 UI 自检（数据/录音目录可写、四象限 CSV
  坏行、JSON 截断/乱码、磁盘空间、关键配置键、FunASR 预置包/python 探测），报告落
  %AppData%\TimeTask\logs\diagnostics-*.txt；放在单实例互斥之前（应用运行中也可诊断）。
  本机冒烟即抓到开发数据 Q1 CSV 坏行×1。新增 9 项契约测试。
- 验证：dev_check.ps1 全链路 PASS——MSBuild 0 错；vstest 208 项 / 206 过 / 0 败 / 2 跳过；
  编码门 changed+full 双模式 0 错；--diagnostics 真实冒烟输出完整报告。
- 教训：①PS 5.1 无 BOM 的含中文 ps1 必乱码——编码守卫已把该情形定为 ERROR；
  ②PS 的 foreach 不逐字符迭代字符串（整串算一个标量），计数字符必须用 [regex]::Matches；
  ③$ErrorActionPreference='Stop' 下原生命令（git）stderr 警告会升级为终止错误，git 调用处需局部放宽。
- 下一步建议：CI 全量测试首跑观察（历史上曾因「桌面测试不稳」默认关闭，本地 206 项稳定，
  若 CI 复现不稳需按类过滤）；实际打一个 v* tag 验证发布链路端到端。

## [2026-09-22 ~20:45 +08:00] 可观测性与 UI 操作防护：Console Tee + 日志滚动 + UiSafe
- 背景：审计发现全仓 315 处 Console.WriteLine 在 WPF（无控制台）下全部丢失——
  语音/LLM 排障最关键的诊断（EnhancedAudioCaptureService/ConversationRecorder/LlmService
  的输出）恰恰全是 Console-only；另有 6 个 async void 事件处理器无异常隔离
  （全局 DispatcherUnhandledException 兜底在、不会崩，但用户看到的是无上下文弹窗，
  且 TestLlmConnection 异常后按钮永久禁用）。
- 1) Console Tee（一处改动，零调用点churn）：VoiceRuntimeLog.TeeConsoleToLog()
  把 Console.Out/Error 换成 ConsoleTeeWriter（原始流透传保持带控制台调试可见 +
  凑行落日志，stderr 按 WARN+[stderr] 记）。App 启动早期安装。防自激回环：
  VoiceRuntimeLog 的控制台回显改为写「cctor 捕获的原始流」且 Tee 安装后不回显。
- 2) 日志滚动：超过 MaxLogBytes（默认 5MB）当前内容转 .old（保留一份），常驻应用日志不再无限涨。
- 3) UiSafe.RunAsync(操作名, body)：统一包裹 6 个无隔离 async void
  （添加任务/导入草稿/测试LLM连接/快捷分解/技能节点/长期目标）——原方法体改名 Core 零正文改动；
  异常记「UI 操作失败：{操作名}」+ 友好弹窗；TestLlmConnection 额外 finally 恢复按钮（修软锁）。
- 测试注意：测试进程绝不能装 Tee（会劫持 vstest 的输出捕获），只直接测 ConsoleTeeWriter。
- 验证：MSBuild 0 错；vstest 199 项 / 197 通过 / 0 失败 / 2 跳过（+5：UiSafe×2、
  TeeWriter 拼行/stderr×2、日志滚动×1）；真实日志隔离字节数证明依旧成立。

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
