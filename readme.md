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

## 注意事项

1.读写csv

https://stackoverflow.com/questions/46062883/c-sharp-wpf-read-edit-csv

2.开机自启动

编辑 autorun.reg 里的程序路径，双击运行
