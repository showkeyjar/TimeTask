<#
.SYNOPSIS
  安装 git 钩子：把本仓库 .githooks/ 目录注册为 core.hooksPath（钩子随仓库版本化）。

.DESCRIPTION
  运行一次即可。安装后 pre-commit 自动执行 scripts/check_encoding.ps1 -Mode changed，
  在提交前拦截乱码文件（U+FFFD / 非法 UTF-8 / bat 中文 / ps1 缺 BOM）。
  卸载：git config --unset core.hooksPath

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install_hooks.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($PSScriptRoot)) { $repoRoot = (Get-Location).Path }
Push-Location $repoRoot
try {
    $hooksDir = '.githooks'
    if (-not (Test-Path (Join-Path $hooksDir 'pre-commit'))) {
        throw "未找到 $hooksDir\pre-commit（应在仓库根目录执行本脚本）。"
    }
    git config core.hooksPath $hooksDir
    if ($LASTEXITCODE -ne 0) { throw 'git config core.hooksPath 失败。' }
    Write-Host '[OK] git 钩子已安装（core.hooksPath=.githooks）。' -ForegroundColor Green
    Write-Host '     pre-commit 将运行编码守卫（scripts/check_encoding.ps1 -Mode changed）。'
    Write-Host '     卸载：git config --unset core.hooksPath'
}
finally { Pop-Location }
