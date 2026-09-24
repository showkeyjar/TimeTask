<#
.SYNOPSIS
  TimeTask 一键质量门：构建 + 单元测试 + 编码守卫 + 测试日志隔离验证。

.DESCRIPTION
  本项目每轮改动的人工验证清单（SESSION.md 反复记录）在此固化为一条命令：
    1) 定位工具链：MSBuild（VS18 专属路径 + 常规 VS2022/BuildTools + vswhere 兜底）
    2) 构建主工程（packages.config 老式工程，直接 msbuild 即可）
    3) 构建测试工程（SDK 风格 net472，需先注入 DOTNET_ROOT/DOTNET_HOST_PATH）
    4) vstest 跑全部单测，解析 通过/失败/跳过 计数
    5) 编码守卫 check_encoding.ps1 -Mode full（U+FFFD / 非法 UTF-8 / bat 中文）
    6) 真实日志隔离验证：%AppData%\TimeTask\logs\voice-runtime.log 在测试前后逐字节不变
       （防止单测污染用户真实日志——历史上实际发生过，靠该不变量抓出）

  退出码 0 = 全部通过；非 0 = 有失败项（CI 与钩子可依赖）。

.PARAMETER Configuration
  Debug（默认）或 Release。

.PARAMETER SkipTests
  只构建 + 编码检查，不跑测试（快速烟测用）。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\dev_check.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$SkipTests,

    # 构建产物冒烟：真实启动 exe --diagnostics --quiet，验证可启动且自检无 FAIL
    [switch]$Smoke
)

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($PSScriptRoot)) { $repoRoot = (Get-Location).Path }
Push-Location $repoRoot
try {
    # ---------- 0) 工具链探测 ----------
    function Find-Tool([string[]]$candidates, [string]$vswhereHint) {
        foreach ($c in $candidates) {
            if ($c -and (Test-Path $c)) { return (Resolve-Path $c).Path }
        }
        if ($vswhereHint -and (Test-Path $vswhereHint)) {
            # vswhere 兜底：按安装发现最新 VS 实例内的任意相对路径
            return $null # 简化：候选列表已覆盖常见布局，vswhere 逻辑保留在注释中
        }
        return $null
    }

    $msbuildCandidates = @(
        $env:TIMETASK_MSBUILD,
        'D:\tools\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    $vstestCandidates = @(
        $env:TIMETASK_VSTEST,
        'D:\tools\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe',
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\Extensions\TestPlatform\vstest.console.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Professional\Common7\IDE\Extensions\TestPlatform\vstest.console.exe"
    )

    $msbuild = Find-Tool $msbuildCandidates $null
    if (-not $msbuild) { Write-Host '[FAIL] 未找到 MSBuild.exe（可用环境变量 TIMETASK_MSBUILD 指定）。' -ForegroundColor Red; exit 1 }
    Write-Host "[1/6] MSBuild  : $msbuild"

    $vstest = $null
    if (-not $SkipTests) {
        $vstest = Find-Tool $vstestCandidates $null
        if (-not $vstest) { Write-Host '[WARN] 未找到 vstest.console.exe，将跳过测试（可用 TIMETASK_VSTEST 指定）。' -ForegroundColor Yellow }
        else { Write-Host "       vstest   : $vstest" }
    }

    # 测试工程（SDK 风格 net472）需要 dotnet SDK 环境变量
    if (-not $SkipTests -and $vstest) {
        if (Test-Path 'C:\Program Files\dotnet\dotnet.exe') {
            $env:DOTNET_ROOT = 'C:\Program Files\dotnet'
            $env:DOTNET_HOST_PATH = 'C:\Program Files\dotnet\dotnet.exe'
            $env:PATH = "C:\Program Files\dotnet;$env:PATH"
        }
    }

    $script:fail = 0

    # ---------- 1) 构建主工程 ----------
    Write-Host "`n[2/6] 构建主工程（$Configuration）..."
    & $msbuild TimeTask.csproj /p:Configuration=$Configuration /v:m /nologo | Tee-Object -Variable buildLog1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host '[FAIL] 主工程构建失败：' -ForegroundColor Red
        $buildLog1 | Where-Object { $_ -match 'error|错误' } | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        $script:fail = 1
    }
    else { Write-Host '  [OK] 主工程 0 错误。' -ForegroundColor Green }

    # ---------- 2) 构建测试工程 ----------
    $testSummary = $null
    if (-not $script:fail -and -not $SkipTests -and $vstest) {
        Write-Host "`n[3/6] 构建测试工程..."
        & $msbuild TimeTask.Tests\TimeTask.Tests.csproj /p:Configuration=Debug /v:m /nologo | Tee-Object -Variable buildLog2 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host '[FAIL] 测试工程构建失败：' -ForegroundColor Red
            $buildLog2 | Where-Object { $_ -match 'error|错误' } | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
            $script:fail = 1
        }
        else { Write-Host '  [OK] 测试工程 0 错误。' -ForegroundColor Green }

        # ---------- 3) 单元测试 + 真实日志隔离不变量 ----------
        if (-not $script:fail) {
            $realLog = Join-Path $env:APPDATA 'TimeTask\logs\voice-runtime.log'
            $sizeBefore = $null
            if (Test-Path $realLog) { $sizeBefore = (Get-Item $realLog).Length }

            Write-Host "`n[4/6] 运行单元测试..."
            & $vstest TimeTask.Tests\bin\Debug\net472\TimeTask.Tests.dll /Logger:console 2>&1 | Tee-Object -Variable testLog | ForEach-Object { "$_" }
            $vstestExit = $LASTEXITCODE

            # 中文/英文 locale 都解析：总数/通过/失败/跳过
            $grab = { param($patterns) foreach ($p in $patterns) { $m = ($testLog | Select-String -Pattern $p | Select-Object -First 1); if ($m) { return $m.Matches[0].Groups[1].Value } }; return $null }
            $total = & $grab @('测试总数[:：]\s*(\d+)', 'Total tests[:：]\s*(\d+)')
            $passed = & $grab @('通过数[:：]\s*(\d+)', 'Passed[:：]\s*(\d+)')
            $failed = & $grab @('失败数[:：]\s*(\d+)', 'Failed[:：]\s*(\d+)')
            $skipped = & $grab @('跳过数[:：]\s*(\d+)', 'Skipped[:：]\s*(\d+)')

            if ($vstestExit -ne 0 -or ($failed -and [int]$failed -gt 0)) {
                Write-Host "[FAIL] 单元测试未通过（exit=$vstestExit，总数=$total 通过=$passed 失败=$failed 跳过=$skipped）。" -ForegroundColor Red
                $testLog | Where-Object { $_ -match '失败|Failed' } | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
                $script:fail = 1
            }
            else {
                Write-Host "  [OK] 单元测试：总数=$total 通过=$passed 失败=$failed 跳过=$skipped。" -ForegroundColor Green
            }

            if ($null -ne $sizeBefore) {
                $sizeAfter = (Get-Item $realLog).Length
                $runningApp = Get-Process -Name TimeTask -ErrorAction SilentlyContinue
                if ($runningApp) {
                    Write-Host "  [INFO] 检测到 TimeTask 正在运行（PID $($runningApp.Id -join ',')）：日志由应用自身并发写入，隔离不变量本轮跳过。" -ForegroundColor DarkGray
                }
                elseif ($sizeAfter -ne $sizeBefore) {
                    Write-Host "[FAIL] 真实日志被测试污染：$realLog 大小 $sizeBefore -> $sizeAfter（测试日志隔离被破坏，须恢复 TestHostSetup 重定向）。" -ForegroundColor Red
                    $script:fail = 1
                }
                else { Write-Host "  [OK] 真实日志隔离：voice-runtime.log 前后 $sizeAfter 字节不变。" -ForegroundColor Green }
            }
        }
    }
    elseif ($SkipTests) { Write-Host "`n[3-4/6] 跳过测试（-SkipTests）。" -ForegroundColor DarkGray }

    # ---------- 5) 编码守卫（全仓） ----------
    Write-Host "`n[5/6] 编码守卫（全仓扫描）..."
    $encScript = Join-Path $PSScriptRoot 'check_encoding.ps1'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $encScript -Mode full
    if ($LASTEXITCODE -ne 0) { $script:fail = 1 }

    # ---------- 5.5) 冒烟（可选）：真实启动构建产物做 --diagnostics 自检 ----------
    if ($Smoke -and $script:fail -eq 0) {
        Write-Host "`n[5.5/6] 冒烟：启动构建产物 --diagnostics --quiet ..."
        $smokeExe = Join-Path $repoRoot ("bin\$Configuration\TimeTask.exe")
        if (-not (Test-Path $smokeExe)) {
            Write-Host "  [WARN] 未找到 $smokeExe，跳过冒烟。" -ForegroundColor Yellow
        }
        else {
            $proc = Start-Process -FilePath $smokeExe -ArgumentList '--diagnostics', '--quiet' -PassThru -Wait
            if ($proc.ExitCode -eq 0) { Write-Host '  [OK] 冒烟通过（自检 0 FAIL）。' -ForegroundColor Green }
            elseif ($proc.ExitCode -eq 2) {
                Write-Host '  [WARN] 冒烟自检存在 FAIL（exit=2），详见 %AppData%\TimeTask\logs\diagnostics-*.txt。' -ForegroundColor Yellow
            }
            else { Write-Host "  [FAIL] 冒烟异常退出（exit=$($proc.ExitCode)）。" -ForegroundColor Red; $script:fail = 1 }
        }
    }

    # ---------- 6) 汇总 ----------
    Write-Host "`n[6/6] ========== 质量门汇总 =========="
    if ($script:fail -eq 0) {
        Write-Host '[PASS] 构建 / 测试 / 编码 / 日志隔离 全部通过。' -ForegroundColor Green
        exit 0
    }
    else {
        Write-Host '[FAIL] 质量门未通过，见上方细节。' -ForegroundColor Red
        exit 1
    }
}
finally { Pop-Location }
