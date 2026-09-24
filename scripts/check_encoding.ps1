<#
.SYNOPSIS
  TimeTask 编码守卫：拦截 U+FFFD 乱码、无效 UTF-8 序列、批处理内 CJK 等历史顽疾。

.DESCRIPTION
  本项目曾多轮出现源文件乱码（scripts/EncodingRepair*.cs、manual_repair.ps1 的由来；
  SESSION.md 每轮都要人工验证「改动文件 U+FFFD 均为 0」）。本脚本把该验证自动化，
  供本地 dev_check、git pre-commit 钩子与 GitHub Actions 复用。

  检查项（按严重度）：
    [ERROR] 文本中出现 U+FFFD（EF BF BD）—— 已损坏的替换字符
    [ERROR] 字节流不是合法 UTF-8（典型：GBK/GB2312 被当 UTF-8 保存）
    [ERROR] .bat/.cmd/.reg 含非 ASCII 字节 —— cmd 代码页下必乱码（build_and_test.bat 明确要求 ASCII only）
    [WARN ] C0 控制字符（\x00-\x08\x0B\x0C\x0E-\x1F，制表/换行/回车除外）

.PARAMETER Mode
  changed = 只检查相对 HEAD 的已改动/新增文件（默认，适合日常与钩子）
  full    = 全仓扫描（适合 CI 门禁与定期体检）

.PARAMETER Path
  仓库根目录（默认脚本所在目录的上一级）。

.PARAMETER TreatWarningAsError
  警告也按失败退出（CI 可选）。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check_encoding.ps1 -Mode full
#>
[CmdletBinding()]
param(
    [ValidateSet('changed', 'full')]
    [string]$Mode = 'changed',

    [string]$Path = '',

    [switch]$TreatWarningAsError
)

$ErrorActionPreference = 'Stop'
# 注意：$PSScriptRoot 在部分 PS 5.1 的参数默认值求值阶段为空，必须在脚本体内取。
if ([string]::IsNullOrWhiteSpace($Path)) {
    $scriptRoot = if ([string]::IsNullOrEmpty($PSScriptRoot)) { Split-Path -Parent $MyInvocation.MyCommand.Path } else { $PSScriptRoot }
    $Path = (Split-Path $scriptRoot -Parent)
}
$repoRoot = (Resolve-Path $Path).Path

# 参与扫描的文本扩展名（二进制/图片/模型一律跳过；.reg 由 regedit 导入、支持 Unicode，不需 ASCII 约束）
$script:textExt = @('.cs', '.xaml', '.md', '.json', '.xml', '.config', '.yml', '.yaml',
                    '.ps1', '.psm1', '.bat', '.cmd', '.reg', '.py', '.sql', '.resx',
                    '.csproj', '.sln', '.txt', '.gitignore', '.gitattributes', '.editorconfig', '.manifest')
# 必须保持纯 ASCII 的脚本类型（cmd.exe 代码页限制，中文必乱码）
$script:asciiOnlyExt = @('.bat', '.cmd')
# 扫描时排除的目录名（路径中任意一级命中即排除：顶层与嵌套 bin/obj 一并覆盖）
$script:excludeDirNames = @('bin', 'obj', '.git', '.vs', '.workbuddy', 'packages', 'TestResults', 'Recordings')
# 例外清单：已知含历史内容、暂不治理的文件（相对路径，/ 分隔）。目标：持续清零。
$script:allowList = @()

function Test-IsTextFile([string]$file) {
    $ext = [System.IO.Path]::GetExtension($file).ToLowerInvariant()
    if ($script:textExt -contains $ext) { return $true }
    # 无扩展名的点文件（.gitignore 等）
    $name = [System.IO.Path]::GetFileName($file)
    return ($script:textExt -contains ('.' + $name.ToLowerInvariant()))
}

function Test-IsExcluded([string]$relative) {
    $parts = $relative -split '/'
    foreach ($p in $parts) {
        if ($script:excludeDirNames -contains $p) { return $true }
    }
    return $false
}

function Get-FullScanFiles {
    Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Force |
        ForEach-Object {
            $rel = $_.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
            if (-not (Test-IsExcluded $rel)) { $_.FullName }
        }
}

function Get-ChangedScanFiles {
    # 相对 HEAD 的已改动/新增 + 未跟踪文件（不含删除）。
    # 注意：PS 5.1 在 $ErrorActionPreference='Stop' 下会把原生命令的 stderr 警告
    # （如 git 的 CRLF 提示）升级为终止错误，故此处局部放宽。
    $files = @()
    Push-Location $repoRoot
    try {
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $diff = git diff --name-only --diff-filter=ACM HEAD 2>$null
            if ($LASTEXITCODE -eq 0 -and $diff) { $files += $diff }
            $untracked = git ls-files --others --exclude-standard 2>$null
            if ($LASTEXITCODE -eq 0 -and $untracked) { $files += $untracked }
        }
        finally { $ErrorActionPreference = $prevEap }
    }
    finally { Pop-Location }
    $files | Sort-Object -Unique | ForEach-Object {
        $full = Join-Path $repoRoot ($_ -replace '/', '\')
        if (Test-IsExcluded ($_ -replace '\\', '/')) { return }
        if (Test-Path -LiteralPath $full -PathType Leaf) { $full }
    }
}

function Test-ValidUtf8([byte[]]$bytes) {
    try {
        $strict = New-Object System.Text.UTF8Encoding($false, $true)
        [void]$strict.GetString($bytes)
        return $true
    }
    catch [System.Text.DecoderFallbackException] {
        return $false
    }
}

function Invoke-ScanFile([string]$full) {
    $relative = $full.Substring($repoRoot.Length + 1).Replace('\', '/')
    $results = New-Object System.Collections.Generic.List[string]
    if ($script:allowList -contains $relative) { return ,$results }

    $bytes = [System.IO.File]::ReadAllBytes($full)
    if ($bytes.Length -eq 0) { return ,$results }

    $ext = [System.IO.Path]::GetExtension($full).ToLowerInvariant()

    # 1) .bat/.cmd 必须纯 ASCII（cmd 代码页下 CJK 必乱码；.reg 例外，regedit 支持 Unicode）
    if ($script:asciiOnlyExt -contains $ext) {
        $nonAscii = @($bytes | Where-Object { $_ -gt 0x7F })
        if ($nonAscii.Count -gt 0) {
            $results.Add("ERROR 批处理文件含非 ASCII 字节 x$($nonAscii.Count)（cmd 代码页下必乱码，请改为英文注释/文案）")
            return ,$results
        }
    }

    if (-not (Test-IsTextFile $full)) { return ,$results }

    # 2) 合法 UTF-8 校验（GBK 存盘会在这一步暴露）
    if (-not (Test-ValidUtf8 $bytes)) {
        $results.Add('ERROR 不是合法 UTF-8（疑似以 GBK/ANSI 编码保存，请用 UTF-8 重新保存）')
        return ,$results
    }

    $text = [System.Text.Encoding]::UTF8.GetString($bytes)

    # 2.5) .ps1 含非 ASCII 却无 BOM：Windows PowerShell 5.1 会按 ANSI 误读（实测复现过的坑）
    if ($ext -in '.ps1', '.psm1') {
        $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        if (-not $hasBom) {
            $hasNonAscii = $false
            foreach ($b in $bytes) { if ($b -gt 0x7F) { $hasNonAscii = $true; break } }
            if ($hasNonAscii) {
                $results.Add('ERROR PowerShell 脚本含中文/非 ASCII 但缺 UTF-8 BOM（Windows PowerShell 5.1 下必乱码解析失败）')
            }
        }
    }

    # 3) U+FFFD 替换字符（乱码已发生的铁证）
    # 注意：PowerShell 的 foreach 不逐字符迭代字符串（整串算一个标量），必须用正则计数。
    $fffdCount = [regex]::Matches($text, [string][char]0xFFFD).Count
    if ($fffdCount -gt 0) {
        $fffdIndex = $text.IndexOf([char]0xFFFD)
        $line = ($text.Substring(0, $fffdIndex) -split "`n").Count
        $results.Add("ERROR 含 U+FFFD 替换字符 x$fffdCount（首见约第 $line 行——内容已损坏，需修复来源而非提交）")
    }

    # 4) C0 控制字符（除 \t \r \n）
    $ctrlHits = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $text.Length; $i++) {
        $c = $text[$i]
        if (($c -lt 32) -and ($c -ne 9) -and ($c -ne 10) -and ($c -ne 13)) {
            $ctrlHits.Add(('0x{0:X2}' -f [int]$c))
            if ($ctrlHits.Count -ge 5) { break }
        }
    }
    if ($ctrlHits.Count -gt 0) {
        $results.Add("WARN 含控制字符 $($ctrlHits -join ',')")
    }

    return ,$results
}

# ---------- 主流程 ----------
$scopeFiles = if ($Mode -eq 'full') { Get-FullScanFiles } else { Get-ChangedScanFiles }

$errorCount = 0
$warnCount = 0
$scanned = 0

foreach ($file in $scopeFiles) {
    try {
        $issues = Invoke-ScanFile $file
    }
    catch {
        Write-Host "[SCAN-FAIL] $file : $($_.Exception.Message)"
        $errorCount++
        continue
    }
    if ($null -eq $issues -or $issues.Count -eq 0) { $scanned++; continue }
    $relative = $file.Substring($repoRoot.Length + 1)
    foreach ($issue in $issues) {
        if ($issue -like 'ERROR*') {
            Write-Host "[ENCODING] $issue :: $relative" -ForegroundColor Red
            $errorCount++
        }
        else {
            Write-Host "[ENCODING] $issue :: $relative" -ForegroundColor Yellow
            $warnCount++
        }
    }
}

$modeText = if ($Mode -eq 'full') { '全仓' } else { '变更文件' }
if ($errorCount -eq 0 -and ($warnCount -eq 0 -or -not $TreatWarningAsError)) {
    Write-Host "[OK] 编码检查通过（$modeText，U+FFFD=0、UTF-8 全合法）。" -ForegroundColor Green
    exit 0
}

if ($errorCount -gt 0) {
    Write-Host "[FAIL] 编码检查未通过：$errorCount 项错误 / $warnCount 项警告（$modeText）。" -ForegroundColor Red
}
else {
    Write-Host "[WARN] 编码检查：0 错误 / $warnCount 项警告（$modeText，按警告失败）。" -ForegroundColor Yellow
}
exit 1
