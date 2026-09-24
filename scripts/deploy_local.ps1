<#
.SYNOPSIS
  一键部署：构建 Release → 优雅停掉正在运行的实例 → 合并拷贝到目标目录 → 重启 → --diagnostics 自检。

.DESCRIPTION
  自动化 2026-09-23 手工做过的「把新构建部署到 D:\tools\TimeTask 日常实例」流程。
  安全边界：
    - 用户数据神圣不可侵犯：data\、Recordings\、logs\ 一律排除，绝不删除目标目录任何文件（无 /MIR）；
    - 停止实例先 CloseMainWindow 优雅关闭（等待 10s），超时才强杀（数据写入全部走 AtomicFile，强杀也安全）；
    - 拷贝完成后自动跑 --diagnostics --quiet 验证部署可启动且无 FAIL。
  典型用法（默认目标 = D:\tools\TimeTask）：
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\deploy_local.ps1
    powershell ... -DryRun      # 只打印将要做什么，不动任何文件/进程
    powershell ... -NoBuild     # 跳过构建，直接部署现有 bin\Release
    powershell ... -NoRestart   # 不停止/不重启实例（若目标实例在运行会直接中止，防止覆盖被锁文件）
#>
[CmdletBinding()]
param(
    [string]$TargetDir = 'D:\tools\TimeTask',

    [switch]$DryRun,

    [switch]$NoBuild,

    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($PSScriptRoot)) { $repoRoot = (Get-Location).Path }
$releaseDir = Join-Path $repoRoot 'bin\Release'

# ---------- 1) 构建（Release） ----------
if (-not $NoBuild) {
    $msbuildCandidates = @(
        $env:TIMETASK_MSBUILD,
        'D:\tools\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    $msbuild = $msbuildCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $msbuild) { throw '未找到 MSBuild.exe（可用环境变量 TIMETASK_MSBUILD 指定）。' }
    Write-Host "[1/5] 构建 Release：$msbuild"
    Push-Location $repoRoot
    try {
        & $msbuild TimeTask.csproj /p:Configuration=Release /v:m /nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败。' }
    }
    finally { Pop-Location }
    Write-Host '  [OK] 构建完成。' -ForegroundColor Green
}
else { Write-Host '[1/5] 跳过构建（-NoBuild）。' -ForegroundColor DarkGray }

$exe = Join-Path $releaseDir 'TimeTask.exe'
if (-not (Test-Path $exe)) { throw "未找到 $exe（先构建或去掉 -NoBuild）。" }

# ---------- 2) 停止正在运行的目标实例 ----------
Write-Host "[2/5] 检查目标实例：$TargetDir"
if (-not (Test-Path $TargetDir)) { throw "目标目录不存在：$TargetDir" }
$running = Get-Process -Name TimeTask -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($TargetDir, [System.StringComparison]::OrdinalIgnoreCase) }

$wasRunning = @($running).Count -gt 0
if ($wasRunning) {
    if ($NoRestart) { throw "目标实例正在运行（PID $($running.Id -join ',')）且指定了 -NoRestart，中止以防覆盖被锁文件。" }
    if ($DryRun) { Write-Host "  [DryRun] 将停止实例 PID $($running.Id -join ',')。" -ForegroundColor Yellow }
    else {
        foreach ($p in $running) {
            $null = $p.CloseMainWindow()
            Start-Sleep -Milliseconds 500
        }
        if (-not ($running | Where-Object { -not $_.HasExited })) { Write-Host '  [OK] 实例已优雅退出。' -ForegroundColor Green }
        $running | Where-Object { -not $_.HasExited } | ForEach-Object {
            if (-not $_.WaitForExit(10000)) {
                Write-Host "  [WARN] PID $($_.Id) 10s 未退出，强杀（数据写入均为原子写，安全）。" -ForegroundColor Yellow
                Stop-Process -Id $_.Id -Force
            }
        }
        Start-Sleep -Seconds 1
    }
}
else { Write-Host '  [OK] 目标实例未在运行。' -ForegroundColor Green }

# ---------- 3) 合并拷贝（绝不删除；用户数据目录排除） ----------
$excludeDirs = @('data', 'Recordings', 'logs', 'TestResults')
Write-Host "[3/5] 同步 bin\Release -> $TargetDir（排除 $($excludeDirs -join '/')，不删除任何文件）"
if ($DryRun) {
    Write-Host '  [DryRun] robocopy 预览：' -ForegroundColor Yellow
    robocopy $releaseDir $TargetDir /E /XF *.bak /XD @($excludeDirs | ForEach-Object { Join-Path $releaseDir $_ }) /L /NJH /NP
    if ($LASTEXITCODE -ge 8) { throw 'robocopy 预览失败。' }
}
else {
    robocopy $releaseDir $TargetDir /E /XF *.bak /XD @($excludeDirs | ForEach-Object { Join-Path $releaseDir $_ }) /NFL /NDL /NJH /NP
    if ($LASTEXITCODE -ge 8) { throw "robocopy 失败，退出码 $LASTEXITCODE。" }
    Write-Host "  [OK] 同步完成（robocopy=$LASTEXITCODE，0=无变化 1=有拷贝 均为成功）。" -ForegroundColor Green
}

# ---------- 4) 重启（之前在运行才重启） ----------
if ($wasRunning -and -not $NoRestart) {
    if ($DryRun) { Write-Host '  [DryRun] 将重新启动应用。' -ForegroundColor Yellow }
    else {
        Start-Process -FilePath (Join-Path $TargetDir 'TimeTask.exe')
        Start-Sleep -Seconds 3
        $again = Get-Process -Name TimeTask -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($TargetDir, [System.StringComparison]::OrdinalIgnoreCase) }
        if ($again) { Write-Host "  [OK] 应用已重启（PID $($again.Id -join ',')）。" -ForegroundColor Green }
        else { Write-Host '  [WARN] 应用未检测到重启（请手动启动或查日志）。' -ForegroundColor Yellow }
    }
}

# ---------- 5) 部署后自检 ----------
if (-not $DryRun) {
    Write-Host '[5/5] 部署后 --diagnostics 自检...'
    $deployed = Join-Path $TargetDir 'TimeTask.exe'
    $proc = Start-Process -FilePath $deployed -ArgumentList '--diagnostics', '--quiet' -PassThru -Wait
    if ($proc.ExitCode -eq 0) { Write-Host '  [OK] 自检通过（0 FAIL；WARN 详见日志目录 diagnostics-*.txt）。' -ForegroundColor Green }
    elseif ($proc.ExitCode -eq 2) { Write-Host '  [WARN] 自检发现 FAIL=2，请查看报告：D:\tools\TimeTask 旁日志目录或 %AppData%\TimeTask\logs\diagnostics-*.txt。' -ForegroundColor Yellow }
    else { Write-Host "  [FAIL] 自检异常退出（exit=$($proc.ExitCode)）。" -ForegroundColor Red; exit 1 }
}
else { Write-Host '[5/5] [DryRun] 跳过自检。' -ForegroundColor Yellow }

Write-Host '===== 部署完成 =====' -ForegroundColor Green
