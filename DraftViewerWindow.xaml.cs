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
            DraftCountText.Text = $"({count} 个)";
        }

        private void AddToTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDraft = DraftsDataGrid.SelectedItem as TaskDraft;
            if (selectedDraft == null)
            {
                MessageBox.Show("请先选择一个草稿", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 获取主窗口实例
            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

            // 尝试获取 LLM Service
            LlmService llmService = null;
            try
            {
                var app = Application.Current as App;
                if (app != null)
                {
                    // 通过反射获取私有字段
                    var field = typeof(App).GetField("_llmService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        llmService = field.GetValue(app) as LlmService;
                    }
                }
            }
            catch { }

            if (llmService == null && mainWindow != null)
            {
                // 备用方案：直接创建（LLM功能可能不可用）
                llmService = new LlmService();
            }

            if (llmService != null)
            {
                var addTaskWindow = new AddTaskWindow(llmService);
                addTaskWindow.SetPreFilledTask(selectedDraft.CleanedText, selectedDraft.EstimatedQuadrant);
                if (mainWindow != null)
                {
                    addTaskWindow.Owner = mainWindow;
                }
                addTaskWindow.ShowDialog();

                // 如果任务被添加，标记草稿为已处理
                if (addTaskWindow.TaskAdded)
                {
                    _draftManager.MarkAsProcessed(selectedDraft.Id);
                    LoadDrafts();
                }
            }
            else
            {
                MessageBox.Show("无法获取 LLM 服务", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void IgnoreButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDraft = DraftsDataGrid.SelectedItem as TaskDraft;
            if (selectedDraft == null)
            {
                MessageBox.Show("请先选择一个草稿", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"确定要忽略草稿 \"{selectedDraft.CleanedText}\" 吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
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
                MessageBox.Show("没有草稿需要清空", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"确定要清空所有 {drafts.Count} 个草稿吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
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
