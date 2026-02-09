using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Text;

namespace TimeTask
{
    /// <summary>
    /// 用户体验改进功能类
    /// 提供键盘快捷键、搜索、导出等功能
    /// </summary>
    public static class UXImprovements
    {
        private static MainWindow _mainWindow;
        
        /// <summary>
        /// 初始化UX改进功能
        /// </summary>
        public static void Initialize(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            SetupKeyboardShortcuts();
            AddSearchCapability();
        }

        #region 键盘快捷键
        /// <summary>
        /// 设置键盘快捷键
        /// </summary>
        private static void SetupKeyboardShortcuts()
        {
            if (_mainWindow == null) return;

            _mainWindow.KeyDown += MainWindow_KeyDown;
        }

        private static void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+N: 新建任务
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenAddTaskWindow();
                e.Handled = true;
            }
            // Ctrl+F: 搜索任务
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ShowSearchDialog();
                e.Handled = true;
            }
            // Ctrl+E: 导出任务
            else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ExportTasks();
                e.Handled = true;
            }
            // F5: 刷新
            else if (e.Key == Key.F5)
            {
                RefreshTasks();
                e.Handled = true;
            }
            // Escape: 清除选择
            else if (e.Key == Key.Escape)
            {
                ClearAllSelections();
                e.Handled = true;
            }
        }

        private static void OpenAddTaskWindow()
        {
            try
            {
                // 创建LlmService实例，如果MainWindow的_llmService不可访问
                var llmService = LlmService.Create();
                var addTaskWindow = new AddTaskWindow(llmService);
                addTaskWindow.Owner = _mainWindow;
                addTaskWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowErrorMessage("打开添加任务窗口失败", ex.Message);
            }
        }

        private static void RefreshTasks()
        {
            try
            {
                // 刷新所有象限的任务
                var task1 = _mainWindow.FindName("task1") as DataGrid;
                var task2 = _mainWindow.FindName("task2") as DataGrid;
                var task3 = _mainWindow.FindName("task3") as DataGrid;
                var task4 = _mainWindow.FindName("task4") as DataGrid;

                task1?.Items.Refresh();
                task2?.Items.Refresh();
                task3?.Items.Refresh();
                task4?.Items.Refresh();

                ShowSuccessMessage("任务列表已刷新");
            }
            catch (Exception ex)
            {
                ShowErrorMessage("刷新任务失败", ex.Message);
            }
        }

        private static void ClearAllSelections()
        {
            try
            {
                var task1 = _mainWindow.FindName("task1") as DataGrid;
                var task2 = _mainWindow.FindName("task2") as DataGrid;
                var task3 = _mainWindow.FindName("task3") as DataGrid;
                var task4 = _mainWindow.FindName("task4") as DataGrid;

                task1?.UnselectAll();
                task2?.UnselectAll();
                task3?.UnselectAll();
                task4?.UnselectAll();
            }
            catch (Exception ex)
            {
                ShowErrorMessage("清除选择失败", ex.Message);
            }
        }
        #endregion

        #region 搜索功能
        /// <summary>
        /// 添加搜索功能
        /// </summary>
        private static void AddSearchCapability()
        {
            // 搜索功能将通过对话框实现
        }

        /// <summary>
        /// 显示搜索对话框
        /// </summary>
        private static void ShowSearchDialog()
        {
            try
            {
                var searchDialog = new Window
                {
                    Title = "搜索任务",
                    Width = 400,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = _mainWindow,
                    ResizeMode = ResizeMode.NoResize
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // 搜索框
                var label = new Label { Content = "搜索关键词:", Margin = new Thickness(10) };
                Grid.SetRow(label, 0);
                grid.Children.Add(label);

                var searchBox = new TextBox { Margin = new Thickness(10, 0, 10, 10) };
                Grid.SetRow(searchBox, 1);
                grid.Children.Add(searchBox);

                // 按钮
                var buttonPanel = new StackPanel 
                { 
                    Orientation = Orientation.Horizontal, 
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(10)
                };

                var searchButton = new Button 
                { 
                    Content = "搜索", 
                    Width = 80, 
                    Height = 30, 
                    Margin = new Thickness(5, 0, 0, 0) 
                };
                searchButton.Click += (s, e) => {
                    SearchTasks(searchBox.Text);
                    searchDialog.Close();
                };

                var cancelButton = new Button 
                { 
                    Content = "取消", 
                    Width = 80, 
                    Height = 30, 
                    Margin = new Thickness(5, 0, 0, 0) 
                };
                cancelButton.Click += (s, e) => searchDialog.Close();

                buttonPanel.Children.Add(searchButton);
                buttonPanel.Children.Add(cancelButton);
                Grid.SetRow(buttonPanel, 2);
                grid.Children.Add(buttonPanel);

                searchDialog.Content = grid;
                searchBox.Focus();

                // 回车键搜索
                searchBox.KeyDown += (s, e) => {
                    if (e.Key == Key.Enter)
                    {
                        SearchTasks(searchBox.Text);
                        searchDialog.Close();
                    }
                };

                searchDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowErrorMessage("打开搜索对话框失败", ex.Message);
            }
        }

        /// <summary>
        /// 搜索任务
        /// </summary>
        private static void SearchTasks(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                ShowWarningMessage("请输入搜索关键词");
                return;
            }

            try
            {
                var results = new List<string>();
                var task1 = _mainWindow.FindName("task1") as DataGrid;
                var task2 = _mainWindow.FindName("task2") as DataGrid;
                var task3 = _mainWindow.FindName("task3") as DataGrid;
                var task4 = _mainWindow.FindName("task4") as DataGrid;

                // 搜索各个象限
                SearchInDataGrid(task1, "重要且紧急", keyword, results);
                SearchInDataGrid(task2, "重要不紧急", keyword, results);
                SearchInDataGrid(task3, "不重要但紧急", keyword, results);
                SearchInDataGrid(task4, "不重要不紧急", keyword, results);

                if (results.Count > 0)
                {
                    var message = $"找到 {results.Count} 个匹配的任务:\n\n" + string.Join("\n", results);
                    MessageBox.Show(message, "搜索结果", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ShowInfoMessage($"没有找到包含 \"{keyword}\" 的任务");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("搜索任务失败", ex.Message);
            }
        }

        private static void SearchInDataGrid(DataGrid dataGrid, string quadrantName, string keyword, List<string> results)
        {
            if (dataGrid?.ItemsSource == null) return;

            foreach (var item in dataGrid.ItemsSource)
            {
                // 假设任务对象有Description属性
                var description = GetTaskDescription(item);
                if (!string.IsNullOrEmpty(description) && 
                    description.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add($"[{quadrantName}] {description}");
                }
            }
        }

        private static string GetTaskDescription(object taskItem)
        {
            try
            {
                // 通过反射获取任务描述
                var descProperty = taskItem.GetType().GetProperty("Description");
                if (descProperty != null)
                {
                    return descProperty.GetValue(taskItem)?.ToString() ?? "";
                }

                // 如果没有Description属性，尝试其他常见属性
                var titleProperty = taskItem.GetType().GetProperty("Title");
                if (titleProperty != null)
                {
                    return titleProperty.GetValue(taskItem)?.ToString() ?? "";
                }

                return taskItem.ToString();
            }
            catch
            {
                return taskItem.ToString();
            }
        }
        #endregion

        #region 导出功能
        /// <summary>
        /// 导出任务到文本文件
        /// </summary>
        private static void ExportTasks()
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出任务",
                    Filter = "文本文件 (*.txt)|*.txt|CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = $"任务导出_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var extension = Path.GetExtension(saveDialog.FileName).ToLower();
                    if (extension == ".csv")
                    {
                        ExportToCSV(saveDialog.FileName);
                    }
                    else
                    {
                        ExportToText(saveDialog.FileName);
                    }
                    
                    ShowSuccessMessage($"任务已导出到: {saveDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("导出任务失败", ex.Message);
            }
        }

        private static void ExportToText(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 任务导出 ===");
            sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            ExportQuadrantToText(sb, "task1", "重要且紧急");
            ExportQuadrantToText(sb, "task2", "重要不紧急");
            ExportQuadrantToText(sb, "task3", "不重要但紧急");
            ExportQuadrantToText(sb, "task4", "不重要不紧急");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static void ExportToCSV(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("象限,任务描述,创建时间");

            ExportQuadrantToCSV(sb, "task1", "重要且紧急");
            ExportQuadrantToCSV(sb, "task2", "重要不紧急");
            ExportQuadrantToCSV(sb, "task3", "不重要但紧急");
            ExportQuadrantToCSV(sb, "task4", "不重要不紧急");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static void ExportQuadrantToText(StringBuilder sb, string gridName, string quadrantName)
        {
            var dataGrid = _mainWindow.FindName(gridName) as DataGrid;
            if (dataGrid?.ItemsSource == null) return;

            sb.AppendLine($"=== {quadrantName} ===");
            var count = 0;
            foreach (var item in dataGrid.ItemsSource)
            {
                var description = GetTaskDescription(item);
                if (!string.IsNullOrEmpty(description))
                {
                    sb.AppendLine($"{++count}. {description}");
                }
            }
            sb.AppendLine();
        }

        private static void ExportQuadrantToCSV(StringBuilder sb, string gridName, string quadrantName)
        {
            var dataGrid = _mainWindow.FindName(gridName) as DataGrid;
            if (dataGrid?.ItemsSource == null) return;

            foreach (var item in dataGrid.ItemsSource)
            {
                var description = GetTaskDescription(item);
                if (!string.IsNullOrEmpty(description))
                {
                    // CSV格式，处理包含逗号的文本
                    var csvDescription = description.Contains(",") ? $"\"{description}\"" : description;
                    sb.AppendLine($"{quadrantName},{csvDescription},{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
            }
        }
        #endregion

        #region 消息提示
        private static void ShowSuccessMessage(string message)
        {
            MessageBox.Show(message, "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void ShowInfoMessage(string message)
        {
            MessageBox.Show(message, "信息", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void ShowWarningMessage(string message)
        {
            MessageBox.Show(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void ShowErrorMessage(string title, string message)
        {
            MessageBox.Show($"{title}\n\n详细信息: {message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        #endregion

        #region 快捷键帮助
        /// <summary>
        /// 显示快捷键帮助
        /// </summary>
        public static void ShowShortcutHelp()
        {
            var helpText = @"键盘快捷键:

Ctrl+N    新建任务
Ctrl+F    搜索任务
Ctrl+E    导出任务
F5        刷新任务列表
Esc       清除所有选择

使用提示:
• 搜索功能支持模糊匹配
• 导出支持TXT和CSV格式
• 所有操作都有错误处理和提示信息";

            MessageBox.Show(helpText, "快捷键帮助", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        #endregion
    }
}