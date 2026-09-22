# 修复编码事故：用 git HEAD 参照修复被双重编码+吸收损坏的字节序列
# 损坏模式：原 [3字节汉字][ASCII] 经 GBK 误解码后变成 [前2字节][0x3F]
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$Reference,
    [switch]$PreviewOnly
)

$strict = New-Object System.Text.UTF8Encoding($false, $true)
$lenient = New-Object System.Text.UTF8Encoding($false, $false)

$curBytes = [System.IO.File]::ReadAllBytes($Target)
$refBytes = [System.IO.File]::ReadAllBytes($Reference)
$refText = $lenient.GetString($refBytes)

# 1) 找出所有损坏点：严格解码失败的位置
$spots = New-Object System.Collections.Generic.List[object]
$i = 0
while ($i -lt $curBytes.Length) {
    $b = $curBytes[$i]
    if ($b -lt 0x80) { $i++; continue }
    $need = if ($b -ge 0xF0) { 3 } elseif ($b -ge 0xE0) { 2 } elseif ($b -ge 0xC0) { 1 } else { $null }
    if ($null -eq $need) {
        # 意外的 continuation 字节 => 损坏点
        $spots.Add($i); $i++; continue
    }
    $ok = $true
    for ($k = 1; $k -le $need; $k++) {
        $nb = $curBytes[$i + $k]
        if ($i + $k -ge $curBytes.Length -or $nb -lt 0x80 -or $nb -ge 0xC0) { $ok = $false; break }
    }
    if ($ok) { $i += 1 + $need; continue }
    # 损坏：lead 字节后跟的不是合法 continuation（典型为 0x3F）
    $spots.Add($i)
    # 跳过 lead+已见 partial（最少 1 字节），停在可疑字节处重新评估
    $i = $i + 1 + $k - 1
}

Write-Output "damage spots: $($spots.Count) in $Target"

if ($spots.Count -eq 0) { return }

# 2) 逐点在参照文本中定位替换内容
$repairs = @()  # @{Start; End; ReplaceBytes}
$failures = @()
foreach ($s in $spots) {
    $beforeStart = [Math]::Max(0, $s - 60)
    $beforeLen = $s - $beforeStart
    $beforeCtx = $lenient.GetString($curBytes, $beforeStart, $beforeLen)
    $tailLen = [Math]::Min(24, $beforeCtx.Length)
    $tail = $beforeCtx.Substring($beforeCtx.Length - $tailLen)

    # 损坏区段终点：向后找到第一个 0x0A 或 双空格 后的稳定点，取 24 字节上下文
    $e = $s
    while ($e -lt $curBytes.Length -and $e -lt $s + 6 -and $curBytes[$e] -ne 0x0A) { $e++ }
    $afterLen = [Math]::Min(48, $curBytes.Length - $e)
    $afterCtx = $lenient.GetString($curBytes, $e, $afterLen)
    $headLen = [Math]::Min(20, $afterCtx.Length)
    $head = $afterCtx.Substring(0, $headLen)

    $posA = $refText.LastIndexOf($tail, [StringComparison]::Ordinal)
    if ($posA -lt 0) { $failures += $s; continue }
    $posAEnd = $posA + $tail.Length
    $posB = $refText.IndexOf($head, $posAEnd, [StringComparison]::Ordinal)
    if ($posB -lt 0 -or $posB -gt $posAEnd + 40) { $failures += $s; continue }

    $replacementText = $refText.Substring($posAEnd, $posB - $posAEnd)
    $repairs += [pscustomobject]@{
        Start = $s; End = $e; Text = $replacementText
        Context = ($beforeCtx.Substring([Math]::Max(0, $beforeCtx.Length - 10)) + '[ DAMAGED ]' + $head.Substring(0, [Math]::Min(10, $head.Length)))
    }
}

foreach ($r in $repairs) { Write-Output ("repair @{0}: {1}" -f $r.Start, ($r.Text -replace "`r`n", '\n' -replace "`n", '\n')) }
foreach ($f in $failures) { Write-Output "UNMATCHED spot: $f" }

if ($PreviewOnly) { return }

# 3) 应用修复（从后往前，避免偏移失效）
$sorted = $repairs | Sort-Object { $_.Start } -Descending
foreach ($r in $sorted) {
    $repBytes = [System.Text.Encoding]::UTF8.GetBytes($r.Text)
    $newBytes = New-Object byte[] ($curBytes.Length - ($r.End - $r.Start) + $repBytes.Length)
    [Array]::Copy($curBytes, 0, $newBytes, 0, $r.Start)
    [Array]::Copy($repBytes, 0, $newBytes, $r.Start, $repBytes.Length)
    [Array]::Copy($curBytes, $r.End, $newBytes, $r.Start + $repBytes.Length, $curBytes.Length - $r.End)
    $curBytes = $newBytes
}

# 4) 校验并写回
try { [void]$strict.GetString($curBytes); Write-Output 'strict decode OK' }
catch { Write-Output "STILL INVALID after repair: $($_.Exception.Message)" }
[System.IO.File]::WriteAllBytes($Target, $curBytes)
