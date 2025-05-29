
# 任务管理助手

本程序使用C#开发，用于常驻桌面，将用户的工作待办事项按照重要紧急四象限进行管理，帮助用户聚焦于重要任务，提高工作效率。

## 功能特点

### 1. 四象限任务管理
- 重要且紧急：需要立即处理的任务
- 重要不紧急：需要规划的任务
- 不重要但紧急：可以委派的任务
- 不重要不紧急：可以暂缓的任务

### 2. 智能任务管理
- 自动保存任务状态
- 任务按分数自动排序
- 任务完成状态标记
- 最后修改时间记录
- AI 智能任务分类
- 任务分析建议

### 3. AI 智能功能
- 基于 OpenAI 的智能任务分类
- 任务分析建议
- 任务分解指导
- 智能提醒和建议

### 4. 用户友好的界面
- 现代化UI设计
- 清晰的任务分类
- 响应式布局，支持窗口调整
- 中文本地化界面

## 快速开始

### 系统要求
- Windows 10/11
- .NET 6.0 或更高版本
- OpenAI API 密钥 (用于智能功能)

### 安装步骤
1. 下载最新版本的程序
2. 解压到任意目录
3. 在 `App.config` 中配置 OpenAI API 密钥：
   ```xml
   <configuration>
     <appSettings>
       <add key="OpenAIApiKey" value="your-api-key-here" />
     </appSettings>
   </configuration>
   ```
4. 运行 `TimeTask.exe`

### 使用方法
1. **添加任务**：点击"添加新任务"按钮，填写任务信息并选择分类
2. **标记完成**：勾选任务前的复选框标记任务完成
3. **删除任务**：选择任务后点击"删除选中任务"按钮
4. **查看任务**：任务按分数自动排序，重要且紧急的任务会显示在顶部

## 任务分类说明

| 分类 | 说明 | 处理建议 |
|------|------|----------|
| 重要且紧急 | 需要立即处理的任务 | 立即执行 |
| 重要不紧急 | 需要规划的任务 | 制定计划 |
| 不重要但紧急 | 可以委派的任务 | 交给他人处理 |
| 不重要不紧急 | 可以暂缓的任务 | 有空再处理 |

## 高级功能

### 自动保存
- 任务数据自动保存到程序目录下的 `data` 文件夹
- 每个分类单独保存为CSV文件

### 开机自启动
1. 创建程序快捷方式
2. 将快捷方式放入启动文件夹：`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`

## 开发计划 (TODO)

### 1. 智能任务分类
- [ ] 集成LLMProvider，根据任务描述自动分类
- [ ] 基于历史任务分析，提供更准确的分类建议
- [ ] 学习用户分类习惯，提高自动分类准确率

### 2. 智能提醒功能
- [ ] 实现任务重要性提醒
- [ ] 长时间未完成任务智能分析
- [ ] 提供任务分解建议（子目标）
- [ ] 目标调整或放弃建议

### 3. 任务完成统计
- [ ] 记录任务完成时间
- [ ] 统计任务完成情况
- [ ] 生成工作效率报告

### 4. 数据备份与同步
- [ ] 实现数据自动备份
- [ ] 添加云同步功能
- [ ] 支持多设备间数据同步

## 开发

### 技术栈
- C# / WPF
- .NET 6.0
- MVVM 设计模式
- OpenAI API 集成
- 依赖注入 (Microsoft.Extensions.DependencyInjection)
- 日志记录 (Microsoft.Extensions.Logging)

### 构建说明
1. 克隆仓库
2. 使用 Visual Studio 2022 打开解决方案
3. 还原 NuGet 包
4. 配置 OpenAI API 密钥
5. 生成解决方案

### 依赖项
- [OpenAI .NET Client](https://github.com/OkGoDoIt/OpenAI-API-dotnet)
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Microsoft.Extensions.Logging.Console
- Microsoft.Extensions.Logging.Debug

## 贡献

欢迎提交 Issue 和 Pull Request

## 许可证

MIT License
