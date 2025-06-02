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

## API Key Configuration

This application uses Large Language Models (LLMs) from OpenAI to provide features like automatic task classification, smart reminders, and task decomposition. To enable these features, you need to configure your OpenAI API key.

1.  **Obtain an API key:** Sign up or log into your [OpenAI account](https://platform.openai.com/signup) to get your API key.
2.  **Locate `App.config`:** Find the `App.config` file in the application's directory (usually where the `.exe` file is located).
3.  **Edit `App.config`:** Open the `App.config` file in a text editor. Look for the `<appSettings>` section and the following line within it:
    ```xml
    <add key="OpenAIApiKey" value="YOUR_API_KEY_GOES_HERE" />
    ```
4.  **Replace Placeholder:** Replace `YOUR_API_KEY_GOES_HERE` with your actual OpenAI API key. For example:
    ```xml
    <add key="OpenAIApiKey" value="sk-yourActualApiKeyGoesHere12345" />
    ```
5.  **Save and Restart:** Save the `App.config` file. The LLM features should be active the next time you run the application.

**Note:** If the API key is not configured or is invalid, the LLM-powered features will not work as expected. You might see dummy responses, or errors indicated in the application's console output or logs (if implemented).

## 注意事项

1.读写csv

https://stackoverflow.com/questions/46062883/c-sharp-wpf-read-edit-csv

2.开机自启动

编辑 autorun.reg 里的程序路径，双击运行
