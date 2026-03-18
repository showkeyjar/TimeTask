using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TimeTask
{
    /// <summary>
    /// 任务草稿查看窗口
    /// </summary>
    public partial class DraftViewerWindow : Window
    {
        private readonly TaskDraftManager _draftManager;
        private readonly LlmService _llmService;
        private bool _isClosed;
        private bool _isImporting;

        public DraftViewerWindow(TaskDraftManager draftManager, LlmService llmService)
        {
            InitializeComponent();
            _draftManager = draftManager ?? throw new ArgumentNullException(nameof(draftManager));
            _llmService = llmService;
            Closed += (_, __) => _isClosed = true;

            LoadDrafts();
        }

        private void LoadDrafts()
        {
            var drafts = _draftManager.GetUnprocessedDrafts();
            DraftsDataGrid.ItemsSource = drafts;
            UpdateCount(drafts.Count);
        }

        private void UpdateCount(int count)
        {
            DraftCountText.Text = I18n.Tf("DraftViewer_CountFormat", count);
        }

        private void AddToTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDrafts = DraftsDataGrid.SelectedItems.Cast<TaskDraft>().ToList();
            if (selectedDrafts == null || selectedDrafts.Count == 0)
            {
                MessageBox.Show(I18n.T("DraftViewer_SelectDraftFirst"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AddDraftsToQuadrants(selectedDrafts);
        }

        private void AddAllButton_Click(object sender, RoutedEventArgs e)
        {
            var drafts = _draftManager.GetUnprocessedDrafts();
            if (drafts == null || drafts.Count == 0)
            {
                MessageBox.Show(I18n.T("DraftViewer_NoDraftToAdd"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(I18n.Tf("DraftViewer_ConfirmAddAllFormat", drafts.Count), I18n.T("Title_Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            AddDraftsToQuadrants(drafts);
        }

        private void AddDraftsToQuadrants(List<TaskDraft> drafts)
        {
            if (drafts == null || drafts.Count == 0) return;

            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dataDir = System.IO.Path.Combine(baseDir, "data");
            System.IO.Directory.CreateDirectory(dataDir);

            foreach (var draft in drafts)
            {
                if (string.IsNullOrWhiteSpace(draft?.CleanedText))
                    continue;

                int quadrantIndex = GetQuadrantIndex(draft);
                if (quadrantIndex < 0) continue;

                string csvNumber = (quadrantIndex + 1).ToString();
                string csvPath = System.IO.Path.Combine(dataDir, $"{csvNumber}.csv");
                var items = HelperClass.ReadCsv(csvPath) ?? new List<ItemGrid>();

                var newItem = new ItemGrid
                {
                    Task = draft.CleanedText,
                    Importance = draft.Importance ?? "Unknown",
                    Urgency = draft.Urgency ?? "Unknown",
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    LastModifiedDate = DateTime.Now,
                    ReminderTime = draft.ReminderTime,
                    IsActiveInQuadrant = true,
                    InactiveWarningCount = 0,
                    Result = string.Empty,
                    SourceTaskID = string.IsNullOrWhiteSpace(draft.SourceNotePath)
                        ? $"draft:{draft.Id}"
                        : $"obsidian:{draft.SourceNotePath.Replace('\\', '/')}"
                };

                items.Insert(0, newItem);
                for (int i = 0; i < items.Count; i++)
                {
                    items[i].Score = items.Count - i;
                }

                HelperClass.WriteCsv(items, csvPath);

                _draftManager.MarkAsProcessed(draft.Id);
                try
                {
                    var lexicon = new VoiceLexiconManager();
                    lexicon.RecordConfirmedPhrase(draft.CleanedText);
                }
                catch { }
            }

            if (mainWindow != null)
            {
                mainWindow.loadDataGridView();
            }

            LoadDrafts();
        }

        private int GetQuadrantIndex(TaskDraft draft)
        {
            string q = draft.EstimatedQuadrant?.Trim();
            if (string.Equals(q, "重要且紧急", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(q, "重要不紧急", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(q, "不重要紧急", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(q, "不重要不紧急", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(q, "Important & Urgent", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(q, "Important & Not Urgent", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(q, "Not Important & Urgent", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(q, "Not Important & Not Urgent", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(q, I18n.T("Quadrant_ImportantUrgent"), StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(q, I18n.T("Quadrant_ImportantNotUrgent"), StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(q, I18n.T("Quadrant_NotImportantUrgent"), StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(q, I18n.T("Quadrant_NotImportantNotUrgent"), StringComparison.OrdinalIgnoreCase)) return 3;

            if (draft.Importance == "High" && draft.Urgency == "High") return 0;
            if (draft.Importance == "High" && draft.Urgency == "Low") return 1;
            if (draft.Importance == "Low" && draft.Urgency == "High") return 2;
            if (draft.Importance == "Low" && draft.Urgency == "Low") return 3;
            return -1;
        }

        private void IgnoreButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDraft = DraftsDataGrid.SelectedItem as TaskDraft;
            if (selectedDraft == null)
            {
                MessageBox.Show(I18n.T("DraftViewer_SelectOneFirst"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(I18n.Tf("DraftViewer_ConfirmIgnoreFormat", selectedDraft.CleanedText), I18n.T("Title_Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _draftManager.DeleteDraft(selectedDraft.Id);
                LoadDrafts();
            }
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            var drafts = _draftManager.GetUnprocessedDrafts();
            if (drafts.Count == 0)
            {
                MessageBox.Show(I18n.T("DraftViewer_NoDraftToClear"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(I18n.Tf("DraftViewer_ConfirmClearAllFormat", drafts.Count), I18n.T("Title_Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _draftManager.ClearAll();
                LoadDrafts();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isImporting)
            {
                return;
            }

            var openDialog = new OpenFileDialog
            {
                Filter = I18n.T("DraftViewer_Import_Filter"),
                Multiselect = false
            };

            if (openDialog.ShowDialog() != true)
            {
                return;
            }

            var nameStore = new ImportUserNameStore();
            var aliases = nameStore.GetAliases();
            if (aliases.Count == 0)
            {
                var nameDialog = new ImportUserNamePromptWindow(nameStore.GetKnownNames());
                var safeOwner = GetSafeOwner();
                if (safeOwner != null)
                {
                    nameDialog.Owner = safeOwner;
                }
                if (nameDialog.ShowDialog() != true)
                {
                    return;
                }
                aliases = nameDialog.GetAliases();
                if (aliases.Count == 0)
                {
                    MessageBox.Show(I18n.T("DraftViewer_ImportNeedName"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                nameStore.SaveAliases(aliases, nameDialog.RememberChoice);
            }

            ImportResult result;
            ImportPreviewResult preview;
            _isImporting = true;
            SetImportBusy(true, I18n.T("DraftViewer_ImportProgressPreparing"));
            try
            {
                var service = new ImportPlanTaskService(_llmService);
                var progress = new Progress<ImportProgress>(UpdateImportProgress);
                preview = await service.BuildPreviewAsync(openDialog.FileName, aliases, progress);
                if (_isClosed)
                {
                    return;
                }
                SetImportBusy(false);
                if (preview.TotalCandidates == 0 || preview.Items.Count == 0)
                {
                    MessageBox.Show(I18n.T("DraftViewer_ImportNoTasks"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var previewWindow = new ImportPreviewWindow(preview);
                var safeOwner = GetSafeOwner();
                if (safeOwner != null)
                {
                    previewWindow.Owner = safeOwner;
                }
                if (previewWindow.ShowDialog() != true)
                {
                    return;
                }

                var selectedItems = previewWindow.GetSelectedItems();
                if (selectedItems.Count == 0)
                {
                    MessageBox.Show(I18n.T("DraftViewer_ImportPreviewNoneSelected"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                result = service.ImportPreviewToDrafts(selectedItems, _draftManager);
                result.Filtered = preview.Filtered;
                result.LlmUsed = preview.LlmUsed;
                result.TotalCandidates = preview.TotalCandidates;
            }
            catch (Exception ex)
            {
                if (!_isClosed)
                {
                    SetImportBusy(false);
                }
                MessageBox.Show(I18n.Tf("DraftViewer_ImportFailedFormat", ex.Message), I18n.T("Title_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                _isImporting = false;
                SetImportBusy(false);
            }

            LoadDrafts();

            if (result.Imported == 0 && result.Filtered > 0)
            {
                MessageBox.Show(I18n.T("DraftViewer_ImportAllFiltered"), I18n.T("Title_Prompt"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show(I18n.Tf("DraftViewer_ImportResultFormat", result.Imported, result.Filtered, result.Failed, result.LlmUsed), I18n.T("Title_Done"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private Window GetSafeOwner()
        {
            if (!_isClosed && IsLoaded)
            {
                return this;
            }

            var active = Application.Current?.Windows?.OfType<Window>()
                .FirstOrDefault(w => w != null && w.IsLoaded);
            if (active != null)
            {
                return active;
            }

            var main = Application.Current?.MainWindow;
            if (main != null && main.IsLoaded)
            {
                return main;
            }

            return null;
        }

        private void UpdateImportProgress(ImportProgress progress)
        {
            if (progress == null || _isClosed || !IsLoaded)
            {
                return;
            }

            string status;
            bool indeterminate = true;
            double? value = null;
            double? max = null;

            switch (progress.Stage)
            {
                case ImportProgressStage.Preparing:
                    status = I18n.T("DraftViewer_ImportProgressPreparing");
                    break;
                case ImportProgressStage.Extracting:
                    status = I18n.T("DraftViewer_ImportProgressExtracting");
                    break;
                case ImportProgressStage.Parsing:
                    if (progress.Total > 0)
                    {
                        status = I18n.Tf("DraftViewer_ImportProgressParsingFormat", progress.Current, progress.Total);
                        indeterminate = false;
                        value = progress.Current;
                        max = progress.Total;
                    }
                    else
                    {
                        status = I18n.T("DraftViewer_ImportProgressParsing");
                    }
                    break;
                case ImportProgressStage.Completed:
                    status = I18n.T("DraftViewer_ImportProgressCompleted");
                    break;
                default:
                    status = I18n.T("DraftViewer_ImportProgressPreparing");
                    break;
            }

            SetImportBusy(true, status, value, max, indeterminate);
        }

        private void SetImportBusy(bool isBusy, string status = null, double? value = null, double? max = null, bool indeterminate = true)
        {
            if (_isClosed || !IsLoaded)
            {
                return;
            }

            ImportProgressOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            if (!string.IsNullOrWhiteSpace(status))
            {
                ImportProgressText.Text = status;
            }

            ImportProgressBar.IsIndeterminate = indeterminate;
            if (!indeterminate && value.HasValue && max.HasValue)
            {
                ImportProgressBar.Minimum = 0;
                ImportProgressBar.Maximum = max.Value <= 0 ? 1 : max.Value;
                ImportProgressBar.Value = Math.Max(0, value.Value);
            }

            DraftsDataGrid.IsEnabled = !isBusy;
            ImportButton.IsEnabled = !isBusy;
            AddToTaskButton.IsEnabled = !isBusy;
            AddAllButton.IsEnabled = !isBusy;
            IgnoreButton.IsEnabled = !isBusy;
            ClearAllButton.IsEnabled = !isBusy;
        }
    }
}
