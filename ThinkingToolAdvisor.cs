using System;
using System.Collections.Generic;
using System.Linq;

namespace TimeTask
{
    public class ThinkingToolDefinition
    {
        public string SkillId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Scenario { get; set; }
    }

    public class ThinkingToolRecommendation
    {
        public string SkillId { get; set; }
        public string Title { get; set; }
        public string Why { get; set; }
        public string NextStep { get; set; }
        public double Confidence { get; set; }
    }

    public class ThinkingToolActionItem
    {
        public bool Selected { get; set; } = true;
        public string Text { get; set; }
        public string Priority { get; set; }
        public string Rationale { get; set; }
    }

    public class ThinkingToolAnalysisReport
    {
        public string SkillId { get; set; }
        public string ToolTitle { get; set; }
        public string TaskName { get; set; }
        public string Why { get; set; }
        public string Diagnostic { get; set; }
        public string Hypothesis { get; set; }
        public string DecisionRule { get; set; }
        public string ReviewPrompt { get; set; }
        public List<string> Risks { get; set; } = new List<string>();
        public List<ThinkingToolActionItem> Actions { get; set; } = new List<ThinkingToolActionItem>();
    }

    public static class ThinkingToolAdvisor
    {
        private static readonly List<ThinkingToolDefinition> Definitions = new List<ThinkingToolDefinition>
        {
            new ThinkingToolDefinition { SkillId = "decompose", Title = "任务拆解", Description = "把模糊任务拆成可执行的下一步", Scenario = "任务描述过长、启动困难、连续延期" },
            new ThinkingToolDefinition { SkillId = "focus_sprint", Title = "专注冲刺", Description = "给当前任务安排短时高专注执行", Scenario = "重要且紧急、需要立即推进关键交付" },
            new ThinkingToolDefinition { SkillId = "priority_rebalance", Title = "优先级重排", Description = "根据紧急/重要性调整顺序", Scenario = "低价值事务挤占时间、事项过多" },
            new ThinkingToolDefinition { SkillId = "risk_check", Title = "风险检查", Description = "提前检查阻塞点与失败风险", Scenario = "任务停滞、存在依赖阻塞、容易返工" },
            new ThinkingToolDefinition { SkillId = "delegate_prepare", Title = "委托准备", Description = "为委托/协作准备最小信息包", Scenario = "紧急但非核心、适合分工协作" },
            new ThinkingToolDefinition { SkillId = "clarify_goal", Title = "目标澄清", Description = "澄清任务目标、边界和完成标准", Scenario = "需求不清、目标含糊、验收标准不明" },
            new ThinkingToolDefinition { SkillId = "five_whys", Title = "5 Whys追因", Description = "连续追问“为什么”定位根因", Scenario = "故障复发、问题反复出现、只修表象" },
            new ThinkingToolDefinition { SkillId = "first_principles", Title = "第一性原理", Description = "拆解假设，回到底层约束重构方案", Scenario = "架构/方案选型、重大设计决策" },
            new ThinkingToolDefinition { SkillId = "pareto_80_20", Title = "80/20法则", Description = "优先抓住高影响的关键20%", Scenario = "时间紧、事项多、需要先拿关键结果" },
            new ThinkingToolDefinition { SkillId = "swot_scan", Title = "SWOT扫描", Description = "从优势/劣势/机会/威胁评估方案", Scenario = "战略规划、竞争评估、方向选择" },
            new ThinkingToolDefinition { SkillId = "premortem", Title = "预演复盘", Description = "先假设失败，提前补齐防线", Scenario = "重要但不紧急、准备长期推进项目" },
            new ThinkingToolDefinition { SkillId = "ooda_loop", Title = "OODA循环", Description = "观察-判断-决策-行动的快速迭代", Scenario = "高压变化快、需快速试错迭代" },
            new ThinkingToolDefinition { SkillId = "smart_goal", Title = "SMART校准", Description = "把目标校准为可衡量可达成", Scenario = "计划阶段、里程碑不清、执行漂移" },
            new ThinkingToolDefinition { SkillId = "cost_benefit", Title = "成本收益分析", Description = "对比投入、收益与机会成本", Scenario = "多方案比较、预算决策、ROI评估" }
        };

        public static IReadOnlyList<ThinkingToolDefinition> GetDefinitions()
        {
            if (!IsEnglishUi())
            {
                return Definitions;
            }

            return Definitions
                .Select(d => new ThinkingToolDefinition
                {
                    SkillId = d.SkillId,
                    Title = LocalizeToolTitle(d.SkillId, d.Title),
                    Description = LocalizeToolDescription(d.SkillId, d.Description),
                    Scenario = LocalizeToolScenario(d.SkillId, d.Scenario)
                })
                .ToList();
        }

        public static string[] GetAllowedSkillIds()
        {
            return Definitions.Select(d => d.SkillId).ToArray();
        }

        public static string GetAllowedSkillIdsCsv()
        {
            return string.Join(", ", GetAllowedSkillIds());
        }

        public static bool IsAllowedSkillId(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return false;
            }

            string normalized = skillId.Trim().ToLowerInvariant();
            return Definitions.Any(d => string.Equals(d.SkillId, normalized, StringComparison.OrdinalIgnoreCase));
        }

        public static List<ThinkingToolRecommendation> RecommendForTask(
            string taskDescription,
            string importance,
            string urgency,
            TimeSpan inactiveDuration,
            int maxTools = 3)
        {
            var recommendations = new List<ThinkingToolRecommendation>();
            string text = taskDescription ?? string.Empty;

            bool highImportance = string.Equals(importance, "High", StringComparison.OrdinalIgnoreCase);
            bool highUrgency = string.Equals(urgency, "High", StringComparison.OrdinalIgnoreCase);
            bool veryStale = inactiveDuration >= TimeSpan.FromDays(3);
            bool longText = text.Length >= 20;

            if (string.IsNullOrWhiteSpace(text) || text.Length < 8)
            {
                recommendations.Add(Create("clarify_goal", 0.80, "任务描述信息不足，先澄清目标可减少返工。", "补全目标、截止时间和验收标准。"));
            }

            if (longText || ContainsAny(text, "项目", "系统", "上线", "方案", "规划", "搭建", "改造"))
            {
                recommendations.Add(Create("decompose", 0.78, "复杂任务先拆小能显著降低启动阻力。", "拆成2-4个可在30分钟内完成的小步骤。"));
            }

            if (ContainsAny(text, "故障", "bug", "异常", "失败", "复发", "事故", "报错", "问题"))
            {
                recommendations.Add(Create("five_whys", 0.86, "先定位根因再修复，可避免反复救火。", "围绕问题连续追问5次“为什么”，写出根因链。"));
            }

            if (ContainsAny(text, "架构", "重构", "选型", "设计", "方案", "框架"))
            {
                recommendations.Add(Create("first_principles", 0.73, "涉及方案设计时，回到底层约束更稳妥。", "列出不可妥协约束，再重建可行方案。"));
            }

            if (ContainsAny(text, "评估", "比较", "选择", "预算", "投入产出", "可行性", "ROI"))
            {
                recommendations.Add(Create("cost_benefit", 0.75, "这是典型决策场景，先算账再推进。", "列出2-3个选项的成本、收益与机会成本。"));
            }

            if (ContainsAny(text, "市场", "战略", "竞争", "产品方向", "商业模式", "增长"))
            {
                recommendations.Add(Create("swot_scan", 0.70, "先完成SWOT扫描再行动，能减少盲区。", "分别写出S/W/O/T各2条，再决定策略。"));
            }

            if (highImportance && highUrgency)
            {
                recommendations.Add(Create("focus_sprint", 0.82, "高重要高紧急任务适合立即短冲刺。", "现在开始一个25分钟专注块，只推进一个关键交付物。"));
                recommendations.Add(Create("ooda_loop", 0.80, "高压场景需要快速迭代决策闭环。", "用10分钟完成一轮观察-判断-决策-行动。"));
            }
            else if (!highImportance && highUrgency)
            {
                recommendations.Add(Create("pareto_80_20", 0.79, "紧急但低重要任务应先抓关键20%。", "标出最小必要结果，其余内容延后处理。"));
                recommendations.Add(Create("delegate_prepare", 0.75, "可委托项尽量交给合适的人处理。", "写明目标、截止时间、上下文和验收标准。"));
            }
            else if (highImportance && !highUrgency)
            {
                recommendations.Add(Create("smart_goal", 0.76, "重要但不紧急任务需要明确里程碑。", "按SMART补全目标，并设置本周里程碑。"));
                recommendations.Add(Create("premortem", 0.72, "提前预演失败能显著提高成功率。", "假设任务失败，写出3个失败原因与预防动作。"));
            }
            else
            {
                recommendations.Add(Create("priority_rebalance", 0.66, "低重要低紧急任务应避免挤占时间。", "确认是否延期、合并或删除该任务。"));
            }

            if (veryStale)
            {
                recommendations.Add(Create("risk_check", 0.74, "任务已停滞，需先识别阻塞点。", "写出当前1个关键阻塞和1个解除动作。"));
            }

            return recommendations
                .Select(LocalizeRecommendation)
                .Where(r => IsAllowedSkillId(r.SkillId))
                .GroupBy(r => r.SkillId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Confidence).First())
                .OrderByDescending(r => r.Confidence)
                .Take(Math.Max(1, maxTools))
                .ToList();
        }

        public static ThinkingToolAnalysisReport AnalyzeTask(
            string taskDescription,
            string importance,
            string urgency,
            TimeSpan inactiveDuration,
            string skillId)
        {
            string safeTask = string.IsNullOrWhiteSpace(taskDescription)
                ? (IsEnglishUi() ? "(Untitled Task)" : "(未命名任务)")
                : taskDescription.Trim();
            string normalizedSkillId = string.IsNullOrWhiteSpace(skillId) ? "clarify_goal" : skillId.Trim().ToLowerInvariant();
            var recommendation = RecommendForTask(taskDescription, importance, urgency, inactiveDuration, 8)
                .FirstOrDefault(r => string.Equals(r.SkillId, normalizedSkillId, StringComparison.OrdinalIgnoreCase));
            var definition = Definitions.FirstOrDefault(d => string.Equals(d.SkillId, normalizedSkillId, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                definition = Definitions.First(d => d.SkillId == "clarify_goal");
                normalizedSkillId = definition.SkillId;
            }

            if (IsEnglishUi())
            {
                return BuildEnglishAnalysis(safeTask, normalizedSkillId, recommendation, importance, urgency, inactiveDuration);
            }

            var report = new ThinkingToolAnalysisReport
            {
                SkillId = normalizedSkillId,
                ToolTitle = definition.Title,
                TaskName = safeTask,
                Why = recommendation?.Why ?? definition.Description
            };

            bool highImportance = string.Equals(importance, "High", StringComparison.OrdinalIgnoreCase);
            bool highUrgency = string.Equals(urgency, "High", StringComparison.OrdinalIgnoreCase);
            bool stale = inactiveDuration >= TimeSpan.FromDays(3);
            string priority = highImportance && highUrgency ? "高" : (highImportance || highUrgency ? "中" : "低");

            switch (normalizedSkillId)
            {
                case "five_whys":
                    report.Diagnostic = "问题表现可能在重复出现，当前动作更像“修症状”而非“治根因”。";
                    report.Hypothesis = "流程缺口、信息丢失或质量门槛不清，导致问题反复。";
                    report.DecisionRule = "若同类问题在2周内复发>=2次，优先根因分析，不继续补丁式处理。";
                    report.Risks.Add("只修当前故障，未来仍会复发。");
                    report.Risks.Add("根因未验证就改流程，可能引入新风险。");
                    report.Actions.Add(NewAction("写出5层“为什么”链路，定位可验证根因。", "高", "先把事实链路拉直，避免主观猜测。"));
                    report.Actions.Add(NewAction("为根因制定1条预防措施与1条检测指标。", "高", "确保改动可被度量与验证。"));
                    report.Actions.Add(NewAction("安排一次24小时后复盘，确认问题未复发。", "中", "闭环验证防止回退。"));
                    report.ReviewPrompt = "复盘问题：证据是否支持根因？预防措施是否可度量？";
                    break;
                case "first_principles":
                    report.Diagnostic = "当前任务涉及方案/架构决策，可能受既有做法路径依赖影响。";
                    report.Hypothesis = "若回到底层约束重新推导，可得到更低成本或更稳方案。";
                    report.DecisionRule = "先列不可妥协约束，再比较方案；不满足约束的方案直接淘汰。";
                    report.Risks.Add("直接沿用旧方案，长期维护成本高。");
                    report.Risks.Add("忽略关键约束，导致上线后返工。");
                    report.Actions.Add(NewAction("列出3条不可妥协约束（性能/成本/时限）。", "高", "先收敛边界，再扩展方案。"));
                    report.Actions.Add(NewAction("基于约束重建2-3个候选方案并标注权衡。", "高", "用可比较结构替代主观偏好。"));
                    report.Actions.Add(NewAction("选择最优方案并写明放弃项与原因。", "中", "防止决策漂移。"));
                    report.ReviewPrompt = "复盘问题：最终方案是因为“习惯”还是“约束最优”？";
                    break;
                case "swot_scan":
                    report.Diagnostic = "该任务涉及方向性选择，单点视角容易遗漏外部威胁。";
                    report.Hypothesis = "完成SWOT后，策略优先级会更清晰。";
                    report.DecisionRule = "每一项策略必须同时说明：利用的优势、应对的威胁。";
                    report.Risks.Add("只看机会，不看能力短板。");
                    report.Risks.Add("战略动作无法落地到执行计划。");
                    report.Actions.Add(NewAction("分别列出S/W/O/T各2条，形成完整局面图。", "高", "避免单维判断。"));
                    report.Actions.Add(NewAction("从SWOT中提炼1条进攻策略和1条防守策略。", "高", "把分析转成可执行方向。"));
                    report.Actions.Add(NewAction("将策略映射到本周可执行动作。", "中", "避免“只分析不执行”。"));
                    report.ReviewPrompt = "复盘问题：策略是否同时利用优势并覆盖主要威胁？";
                    break;
                case "cost_benefit":
                    report.Diagnostic = "当前是典型多方案决策场景，缺少统一比较口径。";
                    report.Hypothesis = "量化成本/收益后，方案选择争议会降低。";
                    report.DecisionRule = "优先选择单位投入产出最高且可在时限内交付的方案。";
                    report.Risks.Add("只看短期收益，忽略维护与切换成本。");
                    report.Risks.Add("成本估算偏低导致计划失真。");
                    report.Actions.Add(NewAction("列出候选方案的直接成本、机会成本、交付周期。", "高", "保证比较维度一致。"));
                    report.Actions.Add(NewAction("为每个方案估算可量化收益与不确定性。", "高", "避免拍脑袋决策。"));
                    report.Actions.Add(NewAction("给出推荐方案并记录阈值条件（何时切换方案）。", "中", "提前设置决策护栏。"));
                    report.ReviewPrompt = "复盘问题：结果是否达到预估收益，偏差来自哪里？";
                    break;
                case "premortem":
                    report.Diagnostic = "任务具备中长期属性，前期风险识别不足会在后期集中爆发。";
                    report.Hypothesis = "先做失败预演，可显著降低执行中的阻塞率。";
                    report.DecisionRule = "若某风险影响高且可预防，必须在执行前配置防线。";
                    report.Risks.Add("关键依赖未提前确认。");
                    report.Risks.Add("关键路径缺少备选方案。");
                    report.Actions.Add(NewAction("假设任务失败，写出最可能的3个失败原因。", "高", "提前暴露脆弱点。"));
                    report.Actions.Add(NewAction("给每个失败原因配置1条预防动作与触发信号。", "高", "把风险管理变成执行动作。"));
                    report.Actions.Add(NewAction("建立每周一次风险回看机制。", "中", "持续校准而不是一次性分析。"));
                    report.ReviewPrompt = "复盘问题：失败预演中识别的风险是否被真实触发？";
                    break;
                case "ooda_loop":
                    report.Diagnostic = "场景变化快，线性计划可能跟不上外部变化。";
                    report.Hypothesis = "通过短周期OODA循环能提升反应速度与命中率。";
                    report.DecisionRule = "每轮决策不超过10分钟，行动后立即采集反馈再迭代。";
                    report.Risks.Add("观察不足就决策，导致方向偏差。");
                    report.Risks.Add("行动后不复盘，重复低效循环。");
                    report.Actions.Add(NewAction("观察：补齐当前关键事实与约束清单。", "高", "先看清局面再行动。"));
                    report.Actions.Add(NewAction("判断+决策：确定本轮唯一目标与动作。", "高", "避免多目标稀释。"));
                    report.Actions.Add(NewAction("行动后记录反馈并启动下一轮。", "高", "建立快速闭环。"));
                    report.ReviewPrompt = "复盘问题：每轮OODA是否有明确输入、输出与反馈？";
                    break;
                case "smart_goal":
                    report.Diagnostic = "任务目标可能过于抽象，导致执行时难以判断完成状态。";
                    report.Hypothesis = "按SMART重写目标后，推进效率和一致性会提升。";
                    report.DecisionRule = "目标需同时满足可衡量与有截止时间，否则不进入执行。";
                    report.Risks.Add("目标描述模糊，团队理解不一致。");
                    report.Risks.Add("没有量化标准，导致完成定义漂移。");
                    report.Actions.Add(NewAction("将目标改写为SMART格式（S/M/A/R/T）。", "高", "确保目标可执行可验收。"));
                    report.Actions.Add(NewAction("定义1-2个量化指标与达成阈值。", "高", "用结果指标驱动执行。"));
                    report.Actions.Add(NewAction("设置阶段检查点并绑定具体日期。", "中", "降低拖延和偏航风险。"));
                    report.ReviewPrompt = "复盘问题：目标是否可量化验收，是否有明确时间边界？";
                    break;
                case "pareto_80_20":
                    report.Diagnostic = "任务集合可能存在“忙但低产出”问题，需要提炼关键少数。";
                    report.Hypothesis = "先聚焦高影响20%，可以更快产出可见结果。";
                    report.DecisionRule = "优先执行对目标贡献最大的20%动作，其余动作延后。";
                    report.Risks.Add("高价值动作被杂务打断。");
                    report.Risks.Add("未定义“关键20%”导致执行分散。");
                    report.Actions.Add(NewAction("列出所有动作并按影响力排序。", "高", "建立统一优先级依据。"));
                    report.Actions.Add(NewAction("圈定前20%关键动作并冻结优先级。", "高", "减少临时切换。"));
                    report.Actions.Add(NewAction("将低影响动作延期或委托。", "中", "释放关键时间。"));
                    report.ReviewPrompt = "复盘问题：本周时间是否真的投入在关键20%上？";
                    break;
                case "decompose":
                    report.Diagnostic = stale ? "任务已停滞且粒度偏大，启动阻力明显。" : "任务复杂度较高，直接执行容易中断。";
                    report.Hypothesis = "将任务拆成30分钟级步骤后，完成率会提升。";
                    report.DecisionRule = "每个子任务必须在30分钟内可启动，并有可见交付。";
                    report.Risks.Add("拆分过粗，仍无法启动。");
                    report.Risks.Add("拆分过细，管理成本过高。");
                    report.Actions.Add(NewAction("定义本任务的最小可交付结果。", "高", "先明确终点再拆步骤。"));
                    report.Actions.Add(NewAction("拆成2-4个30分钟可完成子任务。", "高", "降低启动门槛。"));
                    report.Actions.Add(NewAction("按先后依赖安排执行顺序。", "中", "避免返工与阻塞。"));
                    report.ReviewPrompt = "复盘问题：子任务是否都具备明确输入输出？";
                    break;
                case "focus_sprint":
                    report.Diagnostic = "当前任务时效性高，适合短冲刺推进关键动作。";
                    report.Hypothesis = "25分钟专注块能快速拿到阶段性成果，缓解压力。";
                    report.DecisionRule = "冲刺期间只做单任务，结束后必须产出可见结果。";
                    report.Risks.Add("冲刺前目标不清，导致高强度低产出。");
                    report.Risks.Add("冲刺后无复盘，难以持续优化。");
                    report.Actions.Add(NewAction("设定一个25分钟冲刺目标（单一可交付）。", "高", "明确结果导向。"));
                    report.Actions.Add(NewAction("屏蔽干扰源并开始计时执行。", "高", "保障专注时段完整。"));
                    report.Actions.Add(NewAction("结束后记录产出与下一步。", "中", "维持连续推进。"));
                    report.ReviewPrompt = "复盘问题：冲刺是否输出了可交付成果？";
                    break;
                case "risk_check":
                    report.Diagnostic = stale ? "任务长时间未推进，存在明显阻塞风险。" : "任务存在潜在依赖与外部不确定性。";
                    report.Hypothesis = "提前识别风险并设置应对动作，可减少中断与返工。";
                    report.DecisionRule = "高概率或高影响风险必须绑定责任人与应对动作。";
                    report.Risks.Add("依赖方未按期响应。");
                    report.Risks.Add("关键资源冲突导致延期。");
                    report.Actions.Add(NewAction("列出3个最可能阻塞点并评估影响。", "高", "先识别再应对。"));
                    report.Actions.Add(NewAction("为前2个风险配置预案与触发阈值。", "高", "把风险管理前置。"));
                    report.Actions.Add(NewAction("设置风险检查提醒（48小时）。", "中", "保持动态监控。"));
                    report.ReviewPrompt = "复盘问题：触发了哪些风险？预案是否有效？";
                    break;
                case "delegate_prepare":
                    report.Diagnostic = "任务中存在可委托部分，但信息交接可能不充分。";
                    report.Hypothesis = "提供最小完整信息包后，协作效率会明显提升。";
                    report.DecisionRule = "委托任务必须包含目标、期限、验收标准和上下文链接。";
                    report.Risks.Add("委托信息不全导致反复沟通。");
                    report.Risks.Add("验收标准不清导致返工。");
                    report.Actions.Add(NewAction("写清委托目标、截止时间、验收标准。", "高", "降低沟通摩擦。"));
                    report.Actions.Add(NewAction("补充必要上下文与参考资料链接。", "高", "让对方可独立推进。"));
                    report.Actions.Add(NewAction("约定中间检查点与反馈方式。", "中", "减少末端风险。"));
                    report.ReviewPrompt = "复盘问题：对方是否可在不额外问询下独立执行？";
                    break;
                default:
                    report.Diagnostic = "任务描述与目标边界仍不清晰，影响执行效率。";
                    report.Hypothesis = "先澄清目标、边界和完成标准可显著减少返工。";
                    report.DecisionRule = "没有清晰完成标准时，不进入高成本执行阶段。";
                    report.Risks.Add("目标模糊导致优先级反复变更。");
                    report.Risks.Add("执行动作与目标脱节。");
                    report.Actions.Add(NewAction("补全目标、截止时间和完成标准。", "高", "统一执行预期。"));
                    report.Actions.Add(NewAction("识别任务边界：要做/不做清单。", "高", "避免范围蔓延。"));
                    report.Actions.Add(NewAction("确认第一个可执行动作。", "中", "降低启动成本。"));
                    report.ReviewPrompt = "复盘问题：团队是否对“完成”有一致定义？";
                    break;
            }

            if (highImportance && highUrgency)
            {
                report.Risks.Add("高优先级任务被非关键事项打断。");
            }
            if (stale)
            {
                report.Risks.Add("长期停滞导致任务上下文遗失。");
            }

            return report;
        }

        private static ThinkingToolRecommendation Create(string skillId, double confidence, string why, string nextStep)
        {
            var definition = Definitions.FirstOrDefault(d => string.Equals(d.SkillId, skillId, StringComparison.OrdinalIgnoreCase));
            string title = definition?.Title ?? skillId;
            return new ThinkingToolRecommendation
            {
                SkillId = skillId,
                Title = title,
                Why = why,
                NextStep = nextStep,
                Confidence = Math.Max(0, Math.Min(1, confidence))
            };
        }

        private static bool IsEnglishUi()
        {
            return I18n.CurrentCulture?.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static ThinkingToolRecommendation LocalizeRecommendation(ThinkingToolRecommendation recommendation)
        {
            if (recommendation == null || !IsEnglishUi())
            {
                return recommendation;
            }

            return new ThinkingToolRecommendation
            {
                SkillId = recommendation.SkillId,
                Confidence = recommendation.Confidence,
                Title = LocalizeToolTitle(recommendation.SkillId, recommendation.Title),
                Why = LocalizeRecommendationWhy(recommendation.SkillId, recommendation.Why),
                NextStep = LocalizeRecommendationNextStep(recommendation.SkillId, recommendation.NextStep)
            };
        }

        private static string LocalizeToolTitle(string skillId, string fallback)
        {
            if (!IsEnglishUi())
            {
                return fallback;
            }

            switch ((skillId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "decompose": return "Task Decomposition";
                case "focus_sprint": return "Focus Sprint";
                case "priority_rebalance": return "Priority Rebalance";
                case "risk_check": return "Risk Check";
                case "delegate_prepare": return "Delegation Prep";
                case "clarify_goal": return "Goal Clarification";
                case "five_whys": return "5 Whys";
                case "first_principles": return "First Principles";
                case "pareto_80_20": return "Pareto 80/20";
                case "swot_scan": return "SWOT Scan";
                case "premortem": return "Premortem";
                case "ooda_loop": return "OODA Loop";
                case "smart_goal": return "SMART Calibration";
                case "cost_benefit": return "Cost-Benefit Analysis";
                default: return fallback ?? skillId;
            }
        }

        private static string LocalizeToolDescription(string skillId, string fallback)
        {
            if (!IsEnglishUi())
            {
                return fallback;
            }

            switch ((skillId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "decompose": return "Break a vague task into executable next steps.";
                case "focus_sprint": return "Schedule a short high-focus execution block.";
                case "priority_rebalance": return "Reorder work by urgency and importance.";
                case "risk_check": return "Identify blockers and failure risks in advance.";
                case "delegate_prepare": return "Prepare a minimal handoff package for delegation.";
                case "clarify_goal": return "Clarify objective, boundary, and done criteria.";
                case "five_whys": return "Ask why repeatedly to locate root cause.";
                case "first_principles": return "Rebuild the approach from fundamental constraints.";
                case "pareto_80_20": return "Prioritize the small set that drives most impact.";
                case "swot_scan": return "Evaluate options through strengths, weaknesses, opportunities, threats.";
                case "premortem": return "Assume failure first and add preventive guardrails.";
                case "ooda_loop": return "Observe-orient-decide-act in short cycles.";
                case "smart_goal": return "Refine goals to be specific and measurable.";
                case "cost_benefit": return "Compare effort, benefit, and opportunity cost.";
                default: return fallback;
            }
        }

        private static string LocalizeToolScenario(string skillId, string fallback)
        {
            if (!IsEnglishUi())
            {
                return fallback;
            }

            switch ((skillId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "decompose": return "Long/complex tasks, hard to start, repeated delays.";
                case "focus_sprint": return "High-priority urgent work requiring immediate progress.";
                case "priority_rebalance": return "Too many tasks, low-value work steals time.";
                case "risk_check": return "Stagnation, dependency blockers, rework risk.";
                case "delegate_prepare": return "Urgent but non-core work suitable for delegation.";
                case "clarify_goal": return "Unclear requirement, vague target, weak acceptance criteria.";
                case "five_whys": return "Recurring incidents or symptoms-only fixes.";
                case "first_principles": return "Architecture or major solution decisions.";
                case "pareto_80_20": return "Tight schedule with many open items.";
                case "swot_scan": return "Strategy planning and direction choice.";
                case "premortem": return "Important long-running initiatives.";
                case "ooda_loop": return "Fast-changing high-pressure environment.";
                case "smart_goal": return "Planning phase with unclear milestones.";
                case "cost_benefit": return "Option comparison and budget/ROI decisions.";
                default: return fallback;
            }
        }

        private static string LocalizeRecommendationWhy(string skillId, string fallback)
        {
            if (!IsEnglishUi())
            {
                return fallback;
            }

            switch ((skillId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "decompose": return "This task appears complex. Breaking it down lowers startup resistance.";
                case "focus_sprint": return "This is high-priority urgent work. A short sprint is most effective now.";
                case "priority_rebalance": return "Current workload needs a clearer importance-first order.";
                case "risk_check": return "The task shows stall signals. Risk review helps unblock.";
                case "delegate_prepare": return "Part of this can be delegated with a clear handoff.";
                case "clarify_goal": return "The task statement is still ambiguous and needs clarification.";
                case "five_whys": return "Symptoms may recur unless root cause is identified.";
                case "first_principles": return "Decision quality improves by returning to core constraints.";
                case "pareto_80_20": return "You can secure progress faster by focusing on the critical few.";
                case "swot_scan": return "A structured strategic scan will reduce blind spots.";
                case "premortem": return "Failure simulation up front improves delivery reliability.";
                case "ooda_loop": return "Fast feedback cycles fit this changing context.";
                case "smart_goal": return "Clear measurable goals improve execution consistency.";
                case "cost_benefit": return "This is a tradeoff decision; quantify before committing.";
                default: return fallback;
            }
        }

        private static string LocalizeRecommendationNextStep(string skillId, string fallback)
        {
            if (!IsEnglishUi())
            {
                return fallback;
            }

            switch ((skillId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "decompose": return "Split it into 2-4 steps that each fit within 30 minutes.";
                case "focus_sprint": return "Start one 25-minute focus block for a single deliverable.";
                case "priority_rebalance": return "Rank tasks now and postpone/delete low-impact items.";
                case "risk_check": return "List one blocker and one concrete unblocking action.";
                case "delegate_prepare": return "Prepare objective, deadline, context, and acceptance criteria.";
                case "clarify_goal": return "Add objective, due date, and done criteria to the task.";
                case "five_whys": return "Write a 5-level why-chain based on evidence.";
                case "first_principles": return "List non-negotiable constraints, then rebuild options.";
                case "pareto_80_20": return "Identify the top 20% actions that drive most outcome.";
                case "swot_scan": return "Write 2 items for each S/W/O/T category.";
                case "premortem": return "Assume failure and list 3 causes with preventions.";
                case "ooda_loop": return "Run one short Observe-Orient-Decide-Act cycle now.";
                case "smart_goal": return "Rewrite as SMART and set this week's milestone.";
                case "cost_benefit": return "Compare 2-3 options by cost, benefit, and opportunity cost.";
                default: return fallback;
            }
        }

        private static ThinkingToolAnalysisReport BuildEnglishAnalysis(
            string taskName,
            string skillId,
            ThinkingToolRecommendation recommendation,
            string importance,
            string urgency,
            TimeSpan inactiveDuration)
        {
            bool highImportance = string.Equals(importance, "High", StringComparison.OrdinalIgnoreCase);
            bool highUrgency = string.Equals(urgency, "High", StringComparison.OrdinalIgnoreCase);
            bool stale = inactiveDuration >= TimeSpan.FromDays(3);

            string priority = highImportance && highUrgency ? "High" : (highImportance || highUrgency ? "Medium" : "Low");
            string title = LocalizeToolTitle(skillId, skillId);

            var report = new ThinkingToolAnalysisReport
            {
                SkillId = skillId,
                ToolTitle = title,
                TaskName = taskName,
                Why = recommendation?.Why ?? LocalizeToolDescription(skillId, "Use this tool to improve decision quality for this task."),
                Diagnostic = stale
                    ? "The task has stalled and likely needs a clearer execution structure."
                    : "The task can benefit from a more explicit execution and decision framework.",
                Hypothesis = "Applying this thinking tool now can reduce uncertainty and improve execution throughput.",
                DecisionRule = "Prefer the smallest action that creates visible progress within the next 30 minutes.",
                ReviewPrompt = "Review: Which assumption changed after applying this tool, and what is the next concrete action?"
            };

            report.Risks.Add("Action may stay abstract without explicit acceptance criteria.");
            report.Risks.Add("Context switching can dilute impact if no single next step is chosen.");
            if (stale)
            {
                report.Risks.Add("Prolonged inactivity can cause context loss and hidden rework.");
            }

            report.Actions.Add(NewAction("Define one concrete deliverable for this task.", priority, "Clear output reduces ambiguity."));
            report.Actions.Add(NewAction(LocalizeRecommendationNextStep(skillId, "Plan the immediate next step."), "High", "Fast visible progress builds momentum."));
            report.Actions.Add(NewAction("Schedule a short follow-up check and capture feedback.", "Medium", "Close the loop and prevent drift."));
            return report;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
            {
                return false;
            }

            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static ThinkingToolActionItem NewAction(string text, string priority, string rationale)
        {
            return new ThinkingToolActionItem
            {
                Text = text,
                Priority = priority,
                Rationale = rationale,
                Selected = true
            };
        }
    }
}
