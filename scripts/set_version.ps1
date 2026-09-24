<#
.SYNOPSIS
  统一升版本：AssemblyInfo.cs 的 AssemblyVersion/AssemblyFileVersion 一次改齐，可选生成 CHANGELOG 草稿段。

.DESCRIPTION
  release.yml 从 Properties/AssemblyInfo.cs 的 AssemblyFileVersion 解析发布版本号，
  手工改容易漏改一处导致 tag 与实际程序集版本错位。本脚本：
    1) 校验版本号格式（x.y.z[.r]）
    2) 同时更新 AssemblyVersion 与 AssemblyFileVersion（保持一致，四段式补 .0）
    3) -Notes 时在 CHANGELOG.md 顶部插入该版本草稿段（供发布时补充内容）

  发布链路：改版本 -> commit -> push（或打 tag v*）-> release.yml 自动打包发布。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\set_version.ps1 1.3.2 -Notes "修复 FunASR 启动检测"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Notes = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($PSScriptRoot)) { $repoRoot = (Get-Location).Path }
$assemblyPath = Join-Path $repoRoot 'Properties\AssemblyInfo.cs'
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "版本号格式应为 x.y.z[.r]，收到：$Version"
}
$fourPart = $Version
if ($Version -match '^\d+\.\d+\.\d+$') { $fourPart = "$Version.0" }

# AssemblyInfo.cs 含中文注释：按 UTF-8 读写，保留原 BOM 状态，避免把编码改坏
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$raw = [System.IO.File]::ReadAllBytes($assemblyPath)
$hasBom = ($raw.Length -ge 3 -and $raw[0] -eq 0xEF -and $raw[1] -eq 0xBB -and $raw[2] -eq 0xBF)
$enc = New-Object System.Text.UTF8Encoding($hasBom)
$text = $utf8NoBom.GetString($raw)
if ($hasBom -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }

$replaced = 0
$newText = [regex]::Replace($text, '(Assembly(?:File)?Version\(")(\d+\.\d+\.\d+(?:\.\d+)?)("\))', {
    param($m) $script:replaced++; "$($m.Groups[1].Value)$fourPart$($m.Groups[3].Value)"
})

if ($replaced -lt 2) {
    throw "AssemblyInfo.cs 中应有两处版本特性（AssemblyVersion/AssemblyFileVersion），实际替换 $replaced 处。"
}
[System.IO.File]::WriteAllText($assemblyPath, $newText, $enc)
Write-Host "[OK] AssemblyInfo.cs -> $fourPart（替换 $replaced 处）。" -ForegroundColor Green

if ($Notes) {
    $today = Get-Date -Format 'yyyy-MM-dd'
    $section = "## v$Version - $today`n`n$Notes`n`n"
    $cl = [System.IO.File]::ReadAllText($changelogPath, $utf8NoBom)
    if ($cl -match ("## v$Version\b")) {
        Write-Host "[SKIP] CHANGELOG.md 已存在 v$Version 段，未重复插入。" -ForegroundColor Yellow
    }
    else {
        # 插在首个二级标题之前（紧跟主标题），保持倒序
        $anchor = [regex]::Match($cl, '(?m)^## ')
        if ($anchor.Success) {
            $idx = $anchor.Index
            $newCl = $cl.Substring(0, $idx) + $section + $cl.Substring($idx)
        }
        else { $newCl = $cl.TrimEnd() + "`n`n" + $section }
        [System.IO.File]::WriteAllText($changelogPath, $newCl, $utf8NoBom)
        Write-Host "[OK] CHANGELOG.md 已插入 v$Version 草稿段。" -ForegroundColor Green
    }
}
