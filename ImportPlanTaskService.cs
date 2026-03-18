using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.HSSF.UserModel;
using NPOI.XWPF.UserModel;

namespace TimeTask
{
    public class RawTaskCandidate
    {
        public string RawText { get; set; }
        public string Title { get; set; }
        public string Owner { get; set; }
        public DateTime? StartDate { get; set; }
        public string Stage { get; set; }
        public double Confidence { get; set; }
        public string Source { get; set; }
        public string ContextText { get; set; }
    }

    public class ParsedImportTask
    {
        public string Title { get; set; }
        public string Owner { get; set; }
        public DateTime? StartDate { get; set; }
        public double Confidence { get; set; }
        public string Evidence { get; set; }
        public bool UsedLlm { get; set; }
    }

    public class ImportResult
    {
        public int TotalCandidates { get; set; }
        public int Imported { get; set; }
        public int Filtered { get; set; }
        public int Failed { get; set; }
        public int LlmUsed { get; set; }
        public int UnassignedCount { get; set; }
    }

    public class ImportPreviewItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public bool IsSelectable { get; set; }
        public string Title { get; set; }
        public string Owner { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? ReminderTime { get; set; }
        public double Confidence { get; set; }
        public string RawText { get; set; }
        public string Source { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public class ImportPreviewResult
    {
        public List<ImportPreviewItem> Items { get; set; } = new List<ImportPreviewItem>();
        public int TotalCandidates { get; set; }
        public int Failed { get; set; }
        public int LlmUsed { get; set; }
        public int Filtered { get; set; }
    }

    public enum ImportProgressStage
    {
        Preparing,
        Extracting,
        Parsing,
        Completed
    }

    public class ImportProgress
    {
        public ImportProgressStage Stage { get; set; }
        public int Current { get; set; }
        public int Total { get; set; }
    }

    public class ImportPlanTaskService
    {
        private readonly LlmService _llmService;
        private static readonly string[] TitleHeaders = { "任务", "事项", "工作", "Task", "Name", "Activity" };
        private static readonly string[] StartHeaders = { "开始", "开始日期", "开始时间", "Start", "Start Date" };
        private static readonly string[] OwnerHeaders = { "负责人", "Owner", "执行人", "资源", "Resource", "责任人" };
        private static readonly string[] StageHeaders = { "阶段", "里程碑", "Summary", "Milestone", "阶段/里程碑" };

        public ImportPlanTaskService(LlmService llmService)
        {
            _llmService = llmService;
        }

        public async Task<ImportPreviewResult> BuildPreviewAsync(string filePath, List<string> nameAliases, IProgress<ImportProgress> progress = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath is required");
            }

            progress?.Report(new ImportProgress { Stage = ImportProgressStage.Preparing });
            progress?.Report(new ImportProgress { Stage = ImportProgressStage.Extracting });
            var candidates = await Task.Run(() => ExtractCandidates(filePath));
            var preview = new ImportPreviewResult { TotalCandidates = candidates.Count };
            progress?.Report(new ImportProgress { Stage = ImportProgressStage.Parsing, Current = 0, Total = candidates.Count });

            int index = 0;
            foreach (var candidate in candidates)
            {
                try
                {
                    var parsed = await ParseCandidateAsync(candidate);
                    if (parsed == null || string.IsNullOrWhiteSpace(parsed.Title))
                    {
                        preview.Failed++;
                        continue;
                    }

                    if (parsed.UsedLlm)
                    {
                        preview.LlmUsed++;
                    }

                    bool matched = IsOwnerMatch(parsed.Owner, nameAliases);
                    if (!matched)
                    {
                        preview.Filtered++;
                    }

                    var reminderTime = parsed.StartDate?.AddDays(-3);
                    preview.Items.Add(new ImportPreviewItem
                    {
                        Title = parsed.Title,
                        Owner = parsed.Owner,
                        StartDate = parsed.StartDate,
                        ReminderTime = reminderTime,
                        Confidence = parsed.Confidence,
                        RawText = candidate.RawText,
                        Source = candidate.Source,
                        IsSelectable = matched,
                        IsSelected = matched
                    });
                }
                catch
                {
                    preview.Failed++;
                }

                index++;
                progress?.Report(new ImportProgress
                {
                    Stage = ImportProgressStage.Parsing,
                    Current = index,
                    Total = candidates.Count
                });
            }

            progress?.Report(new ImportProgress
            {
                Stage = ImportProgressStage.Completed,
                Current = candidates.Count,
                Total = candidates.Count
            });
            return preview;
        }

        public async Task<ImportResult> ImportToDraftsAsync(string filePath, List<string> nameAliases, TaskDraftManager draftManager)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath is required");
            }
            if (draftManager == null)
            {
                throw new ArgumentNullException(nameof(draftManager));
            }

            var candidates = ExtractCandidates(filePath);
            var result = new ImportResult { TotalCandidates = candidates.Count };

            foreach (var candidate in candidates)
            {
                try
                {
                    var parsed = await ParseCandidateAsync(candidate);
                    if (parsed == null || string.IsNullOrWhiteSpace(parsed.Title))
                    {
                        result.Failed++;
                        continue;
                    }
                    if (parsed.UsedLlm)
                    {
                        result.LlmUsed++;
                    }

                    if (!IsOwnerMatch(parsed.Owner, nameAliases))
                    {
                        result.Filtered++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(parsed.Owner))
                    {
                        result.UnassignedCount++;
                    }

                    var reminderTime = parsed.StartDate?.AddDays(-3);
                    var draft = new TaskDraft
                    {
                        RawText = candidate.RawText ?? parsed.Title,
                        CleanedText = parsed.Title,
                        Confidence = parsed.Confidence,
                        ReminderTime = reminderTime,
                        ReminderHintText = parsed.StartDate.HasValue ? $"start:{parsed.StartDate:yyyy-MM-dd}" : "start:unknown",
                        EstimatedQuadrant = I18n.T("Quadrant_ImportantNotUrgent"),
                        Importance = "High",
                        Urgency = "Low",
                        Source = "import"
                    };

                    draftManager.AddDraft(draft);
                    result.Imported++;
                }
                catch
                {
                    result.Failed++;
                }
            }

            return result;
        }

        public ImportResult ImportPreviewToDrafts(List<ImportPreviewItem> selectedItems, TaskDraftManager draftManager)
        {
            if (draftManager == null)
            {
                throw new ArgumentNullException(nameof(draftManager));
            }

            var result = new ImportResult
            {
                TotalCandidates = selectedItems?.Count ?? 0
            };

            if (selectedItems == null || selectedItems.Count == 0)
            {
                return result;
            }

            foreach (var item in selectedItems)
            {
                try
                {
                    var draft = new TaskDraft
                    {
                        RawText = item.RawText ?? item.Title,
                        CleanedText = item.Title,
                        Confidence = item.Confidence,
                        ReminderTime = item.ReminderTime,
                        ReminderHintText = item.StartDate.HasValue ? $"start:{item.StartDate:yyyy-MM-dd}" : "start:unknown",
                        EstimatedQuadrant = I18n.T("Quadrant_ImportantNotUrgent"),
                        Importance = "High",
                        Urgency = "Low",
                        Source = "import"
                    };

                    draftManager.AddDraft(draft);
                    result.Imported++;
                }
                catch
                {
                    result.Failed++;
                }
            }

            return result;
        }

        private async Task<ParsedImportTask> ParseCandidateAsync(RawTaskCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            var parsed = new ParsedImportTask
            {
                Title = candidate.Title,
                Owner = candidate.Owner,
                StartDate = candidate.StartDate,
                Confidence = candidate.Confidence,
                Evidence = candidate.RawText
            };

            bool needLlm = parsed.Confidence < 0.6 ||
                           string.IsNullOrWhiteSpace(parsed.Title) ||
                           string.IsNullOrWhiteSpace(parsed.Owner) ||
                           !parsed.StartDate.HasValue;

            if (needLlm && _llmService != null)
            {
                var llmParsed = await _llmService.ParseImportTaskAsync(candidate.RawText, candidate.ContextText);
                if (llmParsed != null)
                {
                    parsed.Title = string.IsNullOrWhiteSpace(parsed.Title) ? llmParsed.Title : parsed.Title;
                    parsed.Owner = string.IsNullOrWhiteSpace(parsed.Owner) ? llmParsed.Owner : parsed.Owner;
                    parsed.StartDate = parsed.StartDate ?? llmParsed.StartDate;
                    parsed.Confidence = Math.Max(parsed.Confidence, llmParsed.Confidence);
                }
                parsed.Confidence = Math.Min(1.0, Math.Max(parsed.Confidence, 0.45));
                parsed.UsedLlm = true;
            }

            return parsed;
        }

        private static bool IsOwnerMatch(string owner, List<string> aliases)
        {
            if (aliases == null || aliases.Count == 0)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(owner))
            {
                return true;
            }

            string normalizedOwner = NormalizeName(owner);
            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;
                if (normalizedOwner.Contains(NormalizeName(alias)))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }
            var sb = new StringBuilder();
            foreach (char c in input.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static List<RawTaskCandidate> ExtractCandidates(string filePath)
        {
            string ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            if (ext == ".xlsx" || ext == ".xls")
            {
                return ExtractFromExcel(filePath);
            }
            if (ext == ".docx")
            {
                return ExtractFromWord(filePath);
            }
            return new List<RawTaskCandidate>();
        }

        private static List<RawTaskCandidate> ExtractFromExcel(string filePath)
        {
            var list = new List<RawTaskCandidate>();
            using (var stream = File.OpenRead(filePath))
            {
                IWorkbook workbook = filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
                    ? (IWorkbook)new HSSFWorkbook(stream)
                    : new XSSFWorkbook(stream);

                var sheet = workbook.NumberOfSheets > 0 ? workbook.GetSheetAt(0) : null;
                if (sheet == null) return list;

                int headerRowIndex = FindHeaderRow(sheet);
                var headerRow = headerRowIndex >= 0 ? sheet.GetRow(headerRowIndex) : null;
                var map = headerRow != null ? MapHeaders(headerRow) : new HeaderMap();

                int startRow = headerRowIndex >= 0 ? headerRowIndex + 1 : sheet.FirstRowNum;
                for (int i = startRow; i <= sheet.LastRowNum; i++)
                {
                    var row = sheet.GetRow(i);
                    if (row == null) continue;

                    string title = map.TitleIndex >= 0 ? GetCellString(row.GetCell(map.TitleIndex)) : GetRowJoinedText(row);
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    string owner = map.OwnerIndex >= 0 ? GetCellString(row.GetCell(map.OwnerIndex)) : null;
                    DateTime? startDate = map.StartIndex >= 0 ? GetCellDate(row.GetCell(map.StartIndex)) : null;
                    string stage = map.StageIndex >= 0 ? GetCellString(row.GetCell(map.StageIndex)) : null;

                    double confidence = 0.5;
                    if (!string.IsNullOrWhiteSpace(title)) confidence += 0.2;
                    if (!string.IsNullOrWhiteSpace(owner)) confidence += 0.1;
                    if (startDate.HasValue) confidence += 0.2;

                    list.Add(new RawTaskCandidate
                    {
                        RawText = GetRowJoinedText(row),
                        Title = title,
                        Owner = owner,
                        StartDate = startDate,
                        Stage = stage,
                        Confidence = Math.Min(1.0, confidence),
                        Source = "excel",
                        ContextText = stage
                    });
                }
            }
            return list;
        }

        private static List<RawTaskCandidate> ExtractFromWord(string filePath)
        {
            var list = new List<RawTaskCandidate>();
            using (var stream = File.OpenRead(filePath))
            {
                var doc = new XWPFDocument(stream);

                foreach (var table in doc.Tables)
                {
                    if (table.Rows.Count == 0) continue;

                    var headerRow = table.Rows[0];
                    var map = MapHeaders(headerRow);

                    for (int i = 1; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        string title = map.TitleIndex >= 0 ? GetCellString(row.GetCell(map.TitleIndex)) : GetRowJoinedText(row);
                        if (string.IsNullOrWhiteSpace(title)) continue;

                        string owner = map.OwnerIndex >= 0 ? GetCellString(row.GetCell(map.OwnerIndex)) : null;
                        DateTime? startDate = map.StartIndex >= 0 ? ParseDateFromText(GetCellString(row.GetCell(map.StartIndex))) : null;
                        string stage = map.StageIndex >= 0 ? GetCellString(row.GetCell(map.StageIndex)) : null;

                        double confidence = 0.45;
                        if (!string.IsNullOrWhiteSpace(title)) confidence += 0.25;
                        if (!string.IsNullOrWhiteSpace(owner)) confidence += 0.1;
                        if (startDate.HasValue) confidence += 0.2;

                        list.Add(new RawTaskCandidate
                        {
                            RawText = GetRowJoinedText(row),
                            Title = title,
                            Owner = owner,
                            StartDate = startDate,
                            Stage = stage,
                            Confidence = Math.Min(1.0, confidence),
                            Source = "word-table",
                            ContextText = stage
                        });
                    }
                }

                string currentStage = null;
                foreach (var para in doc.Paragraphs)
                {
                    string text = para?.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    if (IsStageTitle(para, text))
                    {
                        currentStage = text;
                        continue;
                    }

                    if (!LooksLikeTaskLine(text)) continue;

                    list.Add(new RawTaskCandidate
                    {
                        RawText = text,
                        Title = text,
                        Owner = null,
                        StartDate = ParseDateFromText(text),
                        Stage = currentStage,
                        Confidence = 0.4,
                        Source = "word-paragraph",
                        ContextText = currentStage
                    });
                }
            }
            return list;
        }

        private static bool LooksLikeTaskLine(string text)
        {
            if (text.StartsWith("-") || text.StartsWith("•") || text.StartsWith("·"))
            {
                return true;
            }
            if (text.Contains("负责") || text.Contains("完成") || text.Contains("提交"))
            {
                return true;
            }
            return text.Length >= 4 && text.Length <= 60;
        }

        private static bool IsStageTitle(XWPFParagraph para, string text)
        {
            if (!string.IsNullOrWhiteSpace(para.Style) && para.Style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (text.EndsWith("阶段") || text.EndsWith("里程碑") || text.EndsWith(":") || text.EndsWith("："))
            {
                return true;
            }
            return false;
        }

        private static int FindHeaderRow(ISheet sheet)
        {
            int maxScan = Math.Min(sheet.LastRowNum, 12);
            for (int i = sheet.FirstRowNum; i <= maxScan; i++)
            {
                var row = sheet.GetRow(i);
                if (row == null) continue;
                var text = GetRowJoinedText(row);
                if (ContainsHeader(text, TitleHeaders) || ContainsHeader(text, StartHeaders) || ContainsHeader(text, OwnerHeaders))
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool ContainsHeader(string text, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var key in keywords)
            {
                if (text.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private class HeaderMap
        {
            public int TitleIndex { get; set; } = -1;
            public int StartIndex { get; set; } = -1;
            public int OwnerIndex { get; set; } = -1;
            public int StageIndex { get; set; } = -1;
        }

        private static HeaderMap MapHeaders(IRow row)
        {
            var map = new HeaderMap();
            if (row == null) return map;

            int start = Math.Max(0, (int)row.FirstCellNum);
            for (int i = start; i < (int)row.LastCellNum; i++)
            {
                string text = GetCellString(row.GetCell(i));
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (map.TitleIndex < 0 && ContainsHeader(text, TitleHeaders)) map.TitleIndex = i;
                if (map.StartIndex < 0 && ContainsHeader(text, StartHeaders)) map.StartIndex = i;
                if (map.OwnerIndex < 0 && ContainsHeader(text, OwnerHeaders)) map.OwnerIndex = i;
                if (map.StageIndex < 0 && ContainsHeader(text, StageHeaders)) map.StageIndex = i;
            }

            return map;
        }

        private static HeaderMap MapHeaders(XWPFTableRow row)
        {
            var map = new HeaderMap();
            if (row == null) return map;
            for (int i = 0; i < row.GetTableCells().Count; i++)
            {
                string text = GetCellString(row.GetCell(i));
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (map.TitleIndex < 0 && ContainsHeader(text, TitleHeaders)) map.TitleIndex = i;
                if (map.StartIndex < 0 && ContainsHeader(text, StartHeaders)) map.StartIndex = i;
                if (map.OwnerIndex < 0 && ContainsHeader(text, OwnerHeaders)) map.OwnerIndex = i;
                if (map.StageIndex < 0 && ContainsHeader(text, StageHeaders)) map.StageIndex = i;
            }
            return map;
        }

        private static string GetRowJoinedText(IRow row)
        {
            if (row == null) return null;
            var parts = new List<string>();
            int start = Math.Max(0, (int)row.FirstCellNum);
            for (int i = start; i < (int)row.LastCellNum; i++)
            {
                var cell = row.GetCell(i);
                var text = GetCellString(cell);
                if (!string.IsNullOrWhiteSpace(text)) parts.Add(text.Trim());
            }
            return string.Join(" | ", parts);
        }

        private static string GetRowJoinedText(XWPFTableRow row)
        {
            if (row == null) return null;
            var parts = row.GetTableCells()
                .Select(c => c?.GetText()?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            return string.Join(" | ", parts);
        }

        private static string GetCellString(NPOI.SS.UserModel.ICell cell)
        {
            if (cell == null) return null;
            if (cell.CellType == CellType.Formula)
            {
                return cell.CachedFormulaResultType == CellType.Numeric
                    ? cell.NumericCellValue.ToString(CultureInfo.InvariantCulture)
                    : cell.ToString();
            }
            return cell.ToString();
        }

        private static string GetCellString(XWPFTableCell cell)
        {
            return cell?.GetText();
        }

        private static DateTime? GetCellDate(NPOI.SS.UserModel.ICell cell)
        {
            if (cell == null) return null;
            if (cell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(cell))
            {
                return cell.DateCellValue;
            }

            string text = GetCellString(cell);
            return ParseDateFromText(text);
        }

        private static DateTime? ParseDateFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (DateTime.TryParse(text, out DateTime parsed))
            {
                return parsed.Date;
            }
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double oa) && oa > 20000 && oa < 60000)
            {
                try
                {
                    return DateUtil.GetJavaDate(oa).Date;
                }
                catch
                {
                }
            }
            return null;
        }
    }
}
