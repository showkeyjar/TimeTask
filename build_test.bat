@echo off
echo 正在测试项目编译...

REM 尝试使用dotnet build
echo 尝试使用 dotnet build...
dotnet build TimeTask.csproj --configuration Debug --verbosity minimal

if %ERRORLEVEL% EQU 0 (
    echo 编译成功！
    echo.
    echo 新功能已就绪：
    echo - 智能任务提醒系统
    echo - 现代化提醒界面
    echo - 智能象限分配
    echo - 可配置提醒参数
    echo.
    echo 请在 Visual Studio 中打开项目进行进一步测试。
) else (
    echo 编译失败，请检查以下项目：
    echo 1. 确保所有 NuGet 包已还原
    echo 2. 检查 .NET Framework 版本
    echo 3. 在 Visual Studio 中重新生成解决方案
)

pause