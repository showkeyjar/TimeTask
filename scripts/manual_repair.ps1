# Manual semantic repairs for remaining encoding damage (explicit UTF-8 I/O only!)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$path = Join-Path (Get-Location) 'MainWindow.xaml.cs'
$text = [System.IO.File]::ReadAllText($path, $utf8)
$nl = "`r`n"

# --- 顺序替换助手：从 from 开始找 needle，替换为 replacement，返回新位置 ---
function Replace-From([string]$text, [string]$needle, [string]$replacement, [int]$from) {
    $idx = $text.IndexOf($needle, $from, [StringComparison]::Ordinal)
    if ($idx -lt 0) { throw "anchor not found: $needle" }
    $script:b.AppendLine(("replaced: " + $needle.Substring(0, [Math]::Min(30, $needle.Length))))
    return @{ Text = $text.Substring(0, $idx) + $replacement + $text.Substring($idx + $needle.Length); Pos = $idx + $replacement.Length }
}
$script:b = New-Object System.Text.StringBuilder

# --- 1) 我的新注释（ReadCsv / CsvCell）---
$r = Replace-From $text '列表里注?csv文件错误"之类?            // 占位任务' ("列表里注入`"csv文件错误`"之类的" + $nl + "            // 占位任务") 0; $text = $r.Text
$r = Replace-From $text '炸掉整个文件?            foreach' ("炸掉整个文件。" + $nl + "            foreach") 0; $text = $r.Text
$r = Replace-From $text '空格（?CSV 格式按行解析' '空格（本 CSV 格式按行解析' 0; $text = $r.Text
$r = Replace-From $text '转义?        /// </summary>' ("转义。" + $nl + "        /// </summary>") 0; $text = $r.Text

# --- 2) 注释/文案吸收换行的融合行 ---
$r = Replace-From $text '空态引导提?            UpdateQuadrantCounts' ("空态引导提示。" + $nl + "            UpdateQuadrantCounts") 0; $text = $r.Text
$r = Replace-From $text '即时生效）?                // 不强制填主题' ("即时生效）。" + $nl + "                // 不强制填主题") 0; $text = $r.Text
$r = Replace-From $text '开会也?1 次操作即开录?                svc.Start' ("开会也能 1 次操作即开录。" + $nl + "                svc.Start") 0; $text = $r.Text
$r = Replace-From $text '让人费解?        private void' ("让人费解。" + $nl + "        private void") 0; $text = $r.Text
$r = Replace-From $text '即生效?        private void' ("即生效。" + $nl + "        private void") 0; $text = $r.Text
$r = Replace-From $text '再执?            if (MessageBox.Show' ("再执行。" + $nl + "            if (MessageBox.Show") 0; $text = $r.Text
$r = Replace-From $text '统计时间?            DateTime now' ("统计时间。" + $nl + "            DateTime now") 0; $text = $r.Text
$r = Replace-From $text '淡化效?            foreach' ("淡化效果。" + $nl + "            foreach") 0; $text = $r.Text
$r = Replace-From $text '快速上?            if (emptyHint' ("快速上手。" + $nl + "            if (emptyHint") 0; $text = $r.Text
$r = Replace-From $text '这里只刷新内容?        }' ("这里只刷新内容。" + $nl + "        }") 0; $text = $r.Text

# --- 3) 语句内损坏（句号/引号/词语）---
$r = Replace-From $text '再试?, "提示"' '再试。", "提示"' 0; $text = $r.Text
$r = Replace-From $text '可忽略?;' '可忽略。";' 0; $text = $r.Text
$r = Replace-From $text '实时状态面?+ 气泡提示' '实时状态面板 + 气泡提示' 0; $text = $r.Text
$r = Replace-From $text '统一?UI 线程?---------' '统一到 UI 线程）--------' 0; $text = $r.Text
$r = Replace-From $text '已提?{total} ?;' '已提炼 {total} 条。";' 0; $text = $r.Text
$r = Replace-From $text '开录显?/ 停止收起' '开录显示 / 停止收起' 0; $text = $r.Text
$r = Replace-From $text '后补主?语' '后补主题术语' 0; $text = $r.Text
$r = Replace-From $text '（触?LostFocus' '（触发 LostFocus' 0; $text = $r.Text
$r = Replace-From $text 'return "重要不紧?;' 'return "重要不紧急";' 0; $text = $r.Text
$r = Replace-From $text 'return "不重要不紧?;' 'return "不重要不紧急";' 0; $text = $r.Text

# --- 4) 帮助文本块（git 参照确认原文为 • 列表 + "键:"）---
$r = Replace-From $text ('常用快捷?' + "`n" + '?Ctrl+N: 快速添加新任务') ('常用快捷键:' + "`n" + '• Ctrl+N: 快速添加新任务') 0; $text = $r.Text
$r = Replace-From $text '?Ctrl+F: 搜索任务' '• Ctrl+F: 搜索任务' 0; $text = $r.Text
$r = Replace-From $text '?Ctrl+S: 保存所有任?Del: 删除选中任务' ('• Ctrl+S: 保存所有任务' + "`n" + '• Del: 删除选中任务') 0; $text = $r.Text
$r = Replace-From $text '?F2: 编辑选中任务' '• F2: 编辑选中任务' 0; $text = $r.Text
$r = Replace-From $text '?Tab/Shift+Tab: 在象限间切换' '• Tab/Shift+Tab: 在象限间切换' 0; $text = $r.Text

# --- 5) 录音按钮图标（第一处=待录音⏺，第二处=录音中■）---
$recIcon = [string][char]0x23FA
$stopIcon = [string][char]0x25A0
$r = Replace-From $text 'RecordToggleIcon.Text = "?;' ('RecordToggleIcon.Text = "' + $recIcon + '";') 0; $text = $r.Text
$r = Replace-From $text 'RecordToggleIcon.Text = "?;' ('RecordToggleIcon.Text = "' + $stopIcon + '";') $r.Pos; $text = $r.Text

# --- 6) 置信度等级（0.70/0.60→高，0.45/0.40→中，其余→低）---
function Replace-ReturnAfter([string]$text, [string]$marker, [string]$level) {
    $m = $text.IndexOf($marker, [StringComparison]::Ordinal)
    if ($m -lt 0) { throw "marker not found: $marker" }
    return (Replace-From $text 'return "?;' ('return "' + $level + '";') ($m + $marker.Length))
}
$r = Replace-ReturnAfter $text 'if (confidence >= 0.70)' '高'; $text = $r.Text
$r = Replace-ReturnAfter $text 'if (confidence >= 0.45)' '中'; $text = $r.Text
$r = Replace-ReturnAfter $text 'if (expectedReward >= 0.60)' '高'; $text = $r.Text
$r = Replace-ReturnAfter $text 'if (expectedReward >= 0.40)' '中'; $text = $r.Text
# 剩余两个孤立 return "?; 分别是两个方法的兜底 → 低
$r = Replace-From $text 'return "?;' 'return "低";' 0; $text = $r.Text
$r = Replace-From $text 'return "?;' 'return "低";' 0; $text = $r.Text

# --- 校验：不应再有损坏残留 ---
$leftover = ([regex]::Matches($text, '[\u4e00-\u9fff）】」"]\?|\?["）]|\?Ctrl|\?F2|\?Del')).Count
Write-Output ("leftover suspicious: " + $leftover)

[System.IO.File]::WriteAllText($path, $text, $utf8)
Write-Output 'MainWindow.xaml.cs manual repairs written.'
Write-Output $script:b.ToString()
