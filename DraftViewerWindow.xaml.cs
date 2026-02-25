using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TimeTask
{
    /// <summary>
    /// 任务草稿查看窗口
    /// </summary>
    public partial class DraftViewerWindow : Window
    {
        private readonly TaskDraftManager _draftManager;

        public DraftViewerWindow(TaskDraftManager draftManager)
        {
            InitializeComponent();
            _draftManager = draftManager ?? throw new ArgumentNullException(nameof(draftManager));

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
    }
}
