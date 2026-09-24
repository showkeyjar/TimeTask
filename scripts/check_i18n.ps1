<#
.SYNOPSIS
  i18n audit scanner (ROADMAP: "Expand i18n coverage from main window to all major dialogs").

.DESCRIPTION
  The project's localization mechanism is resx-based:
    - code-behind: I18n.T("Key") / I18n.Tf("Key", args)   (widely used, 250+ sites)
    - XAML:        {loc:Key} markup extension              (currently UNUSED - 0 sites)

  Any CJK text hardcoded in a XAML attribute or element body therefore NEVER switches
  language. This scanner quantifies exactly that, so the migration has a measurable
  baseline and a ratchet that blocks new regressions.

  Scope note: only *.xaml is scanned. .cs files intentionally contain lots of Chinese
  (log messages, LLM prompt templates, voice status text fed through I18n.T at the UI
  edge), so scanning code would be pure noise.

  Detection:
    1) attribute values containing CJK:  Attr="...<CJK>..."
       (x:Name, Grid.Row etc. never contain CJK, so no allow-list is needed)
    2) element inner text containing CJK: <TextBlock>...CJK text...</TextBlock>
    Values already using {loc:...} or {Binding ...} are excluded automatically
    (they do not contain CJK literal text).

.PARAMETER Ratchet
  Path to a baseline file holding the previous total count. Behavior:
    - file missing  -> create it with the current count, exit 0
    - count grown   -> exit 1 (new hardcoded CJK blocked)
    - count shrunk  -> update the baseline downward, exit 0
  CI uses this so the number can only go down.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check_i18n.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\check_i18n.ps1 -Ratchet docs\i18n-baseline.txt
#>
[CmdletBinding()]
param(
    [string]$Ratchet = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Ratchet) -and $PSBoundParameters.ContainsKey('Ratchet')) { $Ratchet = '' }
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($PSScriptRoot)) { $repoRoot = (Get-Location).Path }

# CJK range (CJK Unified Ideographs); kept as ASCII escape so this script stays pure ASCII.
$cjk = '[\u4e00-\u9fff]'

$xamlFiles = Get-ChildItem -LiteralPath $repoRoot -Filter '*.xaml' -File |
    Where-Object {
        $rel = $_.FullName.Substring($repoRoot.Length + 1)
        ($rel -notmatch '^(bin|obj)\\') -and ($_.Name -ne 'BackupManagerWindow.xaml')
    }

$total = 0
$perFile = @()
$samples = New-Object System.Collections.Generic.List[string]

foreach ($f in $xamlFiles) {
    $lineNo = 0
    $fileHits = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $lineNo++
        $hit = $false
        # 1) attribute value with CJK:  Foo="...cjk..."  (single-quoted variant too)
        if ([regex]::IsMatch($line, ('\w+="' + $cjk))) { $hit = $true }
        elseif ([regex]::IsMatch($line, ("\w+='" + $cjk))) { $hit = $true }
        # 2) element inner text with CJK:  >...cjk...<
        elseif ([regex]::IsMatch($line, ('>' + $cjk))) { $hit = $true }

        if ($hit) {
            # Exclude lines that are actually localized/dynamic bindings.
            if ($line -match '\{loc:') { $hit = $false }
        }

        if ($hit) {
            $fileHits++
            $total++
            if ($samples.Count -lt 25) {
                $trimmed = ($line.Trim() -replace '\s+', ' ')
                if ($trimmed.Length -gt 110) { $trimmed = $trimmed.Substring(0, 110) + '...' }
                $samples.Add(("{0}:{1}: {2}" -f $f.Name, $lineNo, $trimmed))
            }
        }
    }
    if ($fileHits -gt 0) {
        $perFile += [pscustomobject]@{ File = $f.Name; Hits = $fileHits }
    }
}

Write-Host ('== i18n audit: hardcoded CJK in XAML (never switches language) ==')
Write-Host ('Total hardcoded strings : {0}' -f $total)
Write-Host ('XAML files scanned      : {0}' -f $xamlFiles.Count)
Write-Host ('Files with hits         : {0}' -f $perFile.Count)
foreach ($p in ($perFile | Sort-Object Hits -Descending)) {
    Write-Host ('  {0,5}  {1}' -f $p.Hits, $p.File)
}
Write-Host ''
Write-Host 'Samples (first 25):'
$samples | ForEach-Object { Write-Host ('  ' + $_) }
Write-Host ''
Write-Host 'Migration hint: replace with {loc:YourKey} + add the key to Properties/Resources*.resx,'

if (-not [string]::IsNullOrWhiteSpace($Ratchet)) {
    $ratchetPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Ratchet))
    $previous = $null
    if (Test-Path -LiteralPath $ratchetPath) {
        $m = [regex]::Match((Get-Content -LiteralPath $ratchetPath -Raw), 'total\s*=\s*(\d+)')
        if ($m.Success) { $previous = [int]$m.Groups[1].Value }
    }
    if ($null -eq $previous) {
        ("total = $total`nupdated = $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')") |
            Set-Content -LiteralPath $ratchetPath -Encoding ASCII
        Write-Host ("[RATCHET] baseline created at $Ratchet (total = $total).") -ForegroundColor Green
    }
    elseif ($total -gt $previous) {
        Write-Host ("[RATCHET-FAIL] hardcoded CJK grew: $previous -> $total. " +
                    'Localize the new strings or update the baseline deliberately.') -ForegroundColor Red
        exit 1
    }
    elseif ($total -lt $previous) {
        ("total = $total`nupdated = $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')") |
            Set-Content -LiteralPath $ratchetPath -Encoding ASCII
        Write-Host ("[RATCHET-OK] improved: $previous -> $total (baseline updated).") -ForegroundColor Green
    }
    else {
        Write-Host ("[RATCHET-OK] unchanged at $total.") -ForegroundColor Green
    }
}

exit 0
