# 任务管理助手

本程序使用C#开发，用于常驻桌面，将用户的工作待办事项分为重要紧急四象限管理

用于时刻提醒用户聚焦于重要且紧急的工作，减少工作中的干扰

## 功能

1. 任务分类

   任务分为重要紧急四象限

   重要紧急、重要不紧急、不重要紧急、不重要不紧急

   todo 需要加入LLMProvider，根据用户的描述，自动分类任务，也可通过用户历史任务分析和推测，给出更准确的分类结果

2. 任务提醒

   todo 任务提醒功能，提醒用户任务的重要性，可结合LLMProvider，根据用户的描述，自动提醒用户任务的重要性
   长期没有进度的任务，也可以帮助用户智能分析原因并做出改进（比如分解为子目标、改变目标或放弃等等）

3. 任务完成

   todo 任务完成功能，记录用户完成任务的时间

4. 任务统计

   todo 任务统计功能，统计用户完成任务的时间

5. 任务管理

   任务管理功能，管理用户的任务

## LLM Provider Configuration

This application uses Large Language Models (LLMs) to provide features like automatic task classification, smart reminders, and task decomposition. The primary supported provider is Zhipu AI, but the configuration is flexible. To enable these features, you need to configure your API key and other relevant settings.

1.  **Obtain an API key:** Obtain an API key from your chosen LLM provider (e.g., Zhipu AI).
2.  **Locate `App.config`:** Find the `App.config` file in the application's directory (usually where the `.exe` file is located).
3.  **Edit `App.config`:** Open the `App.config` file in a text editor. Look for the `<appSettings>` section. You will need to configure the following keys:
    *   `OpenAIApiKey`: Your API key for the LLM provider.
    *   `LlmProvider`: Specifies the provider (e.g., "zhipu").
    *   `LlmApiBaseUrl`: The base URL for the provider's API.
    *   `LlmModelName`: The specific model you wish to use.

    Here is an example configuration for Zhipu AI:
    ```xml
    <appSettings>
        <add key="OpenAIApiKey" value="YOUR_ZHIPU_API_KEY" />
        <add key="LlmProvider" value="zhipu" />
        <add key="LlmApiBaseUrl" value="https://open.bigmodel.cn/api/paas/v4/" />
        <add key="LlmModelName" value="glm-4" />
    </appSettings>
    ```
4.  **Replace Placeholder:** Replace `YOUR_ZHIPU_API_KEY` with your actual API key. Adjust `LlmApiBaseUrl` and `LlmModelName` if you are using a different provider or model.
5.  **Save and Restart:** Save the `App.config` file. The LLM features should be active the next time you run the application.

**Note:** If the API key or other LLM configurations are incorrect or missing, the LLM-powered features will not work as expected. You might see dummy responses (potentially indicating a configuration error), or errors logged in the application's console output.

## LLM 功能触发说明
当前版本中，以下功能会触发与大语言模型 (LLM) 的交互：
*   创建新任务时，对任务描述进行清晰度分析，并在必要时引导用户优化描述。
*   创建新任务时，根据任务描述智能推荐任务所属的分类列表。
*   程序启动时，为超过两周未更新的活动任务生成提醒信息和行动建议。
*   当旧任务的提醒中包含分解建议，并且用户同意后，尝试将任务分解为子任务。

## 主要AI功能验证说明
以下步骤可用于手动验证本程序中主要AI功能的表现。请确保在测试前已按照 "LLM Provider Configuration" 部分正确配置了API密钥等信息。

### 1. 创建新任务时的清晰度分析
**准备:**
*   确保 `App.config` 中的LLM API密钥 (`OpenAIApiKey`) 已正确配置且有效。
*   确保 `LlmApiBaseUrl` 和 `LlmModelName` 指向有效的服务和模型。

**操作:**
1.  运行应用程序。
2.  点击任意象限的空白处或通过其他方式打开“添加任务”窗口。
3.  在“任务描述”文本框中输入一个模糊的任务描述，例如：“处理项目” 或 “回顾”。
4.  点击“Add Task”按钮。

**预期结果:**
*   系统调用LLM进行清晰度分析。
*   由于描述模糊，LLM应返回 `NeedsClarification` 状态。
*   “添加任务”窗口中应显示一个“澄清问题”区域（Clarification Border），其中包含LLM提出的具体问题，例如：“请问‘处理项目’具体指的是哪个项目？主要目标是什么？”
*   “Add Task”按钮的文本应变为“Submit Clarified Task”（或类似文本，表示进入澄清模式）。
*   用户可以修改任务描述以回答LLM的问题。

**进一步测试 (可选):**
*   在澄清模式下，修改任务描述，使其更清晰（例如：“完成关于X项目的季度总结报告”）。
*   再次点击“Submit Clarified Task”按钮。
*   此时，LLM应返回 `Clear` 状态，任务被成功添加，不再出现澄清问题。
*   或者，重新打开“添加任务”窗口，输入一个清晰的任务描述，例如：“撰写关于新功能模块的详细设计文档”。
*   点击“Add Task”按钮。
*   预期LLM直接返回 `Clear` 状态，任务直接进入下一步（分类建议），不显示澄清问题。

### 2. 创建新任务时的智能分类建议
**准备:**
*   确保 `App.config` 中的LLM API密钥等配置正确有效。
*   （可选）在“添加任务”窗口的清晰度分析步骤中，确保任务描述是清晰的，或者直接提供一个清晰的任务描述。

**操作:**
1.  运行应用程序。
2.  打开“添加任务”窗口。
3.  输入一个具有明显优先级特征的任务描述。例如：
    *   高优先级示例：“修复导致系统崩溃的紧急线上bug”
    *   中等优先级示例：“准备下周客户演示的PPT”
    *   低优先级示例：“整理桌面文件和旧邮件”
4.  如果 melewati 清晰度分析步骤（即任务描述本身足够清晰，或者已经完成了澄清），观察“添加任务”窗口。

**预期结果:**
*   系统调用LLM获取任务的建议优先级（重要性和紧急性）。
*   根据LLM返回的优先级，“添加任务”窗口中的象限选择下拉框（ListSelectorComboBox）应自动选中对应的分类。例如：
    *   对于“修复导致系统崩溃的紧急线上bug”，预期选中“重要且紧急”。
    *   对于“准备下周客户演示的PPT”，预期选中“重要不紧急”或“重要且紧急”，具体取决于LLM的判断。
    *   对于“整理桌面文件和旧邮件”，预期选中“不重要不紧急”。
*   下拉框下方应显示LLM的建议文本，例如：“LLM Suggests: 重要且紧急”。

**进一步测试 (可选):**
*   尝试不同类型和紧急程度的任务描述，观察LLM建议的准确性。
*   即使用户不采纳LLM的建议，手动选择其他分类，任务也应能被正确添加到所选分类中。

### 3. 旧任务的提醒与建议生成
**准备:**
*   确保 `App.config` 中的LLM API密钥等配置正确有效。
*   手动编辑一个或多个CSV数据文件（例如 `data/1.csv`），确保其中至少有一个**活动状态** (is_completed 为 False 或空) 的任务，其 `LastModifiedDate` (最后一列) 设置为一个比当前日期早15天以上的日期。例如，如果今天是2023年10月30日，可以将日期设置为 `2023-10-01T12:00:00Z`。
    *   任务描述应尽量清晰，以便LLM生成有意义的提醒和建议。例如：“完成年度财务报告初稿”。

**操作:**
1.  保存修改后的CSV文件。
2.  运行应用程序。
3.  观察应用程序启动后的行为，以及控制台输出。

**预期结果:**
*   程序启动时，`loadDataGridView` 方法会检查任务的 `LastModifiedDate`。
*   对于满足“旧任务”条件（例如超过14天未修改）的任务，系统应调用LLM生成提醒和建议。
*   控制台应输出类似 "Task '完成年度财务报告初稿' is stale (age: X days). Generating reminder..." 的日志。
*   屏幕上应弹出一个MessageBox，显示LLM生成的关于该旧任务的提醒信息和2-3条行动建议。例如：
    *   提醒：“关注一下‘完成年度财务报告初稿’这个任务，已经X天没有更新了，进展如何？”
    *   建议1：“是否可以今天安排一些时间继续完成它？”
    *   建议2：“如果遇到困难，是否需要调整计划或寻求帮助？”

**进一步测试 (可选):**
*   修改CSV文件，将旧任务的 `LastModifiedDate` 改为最近的日期（例如昨天），然后重启程序。
*   预期该任务不再触发提醒。
*   创建多个不同描述的旧任务，观察LLM生成的提醒和建议的相关性和多样性。

### 4. 基于提醒建议的任务分解
**准备:**
*   确保 `App.config` 中的LLM API密钥等配置正确有效。
*   按照 "3. 旧任务的提醒与建议生成" 的准备步骤，创建一个旧任务。关键在于，LLM为这个旧任务生成的建议中，需要有一条是关于“任务分解”的。这可能需要调整旧任务的描述，使其听起来比较复杂或宏大，例如：“规划并执行整个新市场推广活动”。
    *   如果LLM生成的建议不包含分解相关的选项，可能需要多次尝试修改任务描述或接受当前LLM可能不会对所有任务都建议分解。

**操作:**
1.  运行应用程序，等待旧任务提醒的MessageBox弹出 (如测试用例3所述)。
2.  仔细阅读MessageBox中的建议。如果其中一条建议类似于“是否需要将此任务分解为更小的步骤？”或包含“分解”、“break it down”等关键词。
3.  点击该MessageBox上的“是”（Yes）按钮，表示同意尝试分解任务。

**预期结果:**
*   系统调用LLM对原任务描述（例如“规划并执行整个新市场推广活动”）进行分解。
*   一个新的窗口 "Decomposition Result Window" (任务分解结果窗口) 应该弹出。
*   此窗口中会显示LLM分解出的子任务列表（如果分解成功）。例如：
    *   - 市场调研与分析
    *   - 制定推广策略与预算
    *   - 准备宣传材料
    *   - 执行线上推广活动
    *   - 监控活动效果与调整
*   如果LLM认为任务不需要分解或无法分解，该窗口可能会显示相应的状态信息，例如：“Status: Sufficient” 或 “未能有效分解任务”。
*   用户可以在此窗口中选择希望实际添加的子任务。

**进一步测试 (可选):**
*   在 "Decomposition Result Window" 中，选择部分或全部子任务，并选择它们的目标象限（如果提供该功能，或默认继承父任务象限）。
*   点击确认添加。
*   验证所选的子任务是否已作为新任务添加到主界面的相应象限列表中。
*   尝试对不同复杂程度的任务触发分解，观察LLM分解质量和行为。

## 注意事项

1.读写csv

https://stackoverflow.com/questions/46062883/c-sharp-wpf-read-edit-csv

2.开机自启动

编辑 autorun.reg 里的程序路径，双击运行
