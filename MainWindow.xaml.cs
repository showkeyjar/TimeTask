using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media; // Added for VisualTreeHelper
using System.Threading.Tasks; // Added for Task.Delay and async/await
using KanbanApp; // Required for KanbanBoardView

namespace TimeTask
{

    public static class HelperClass
    {

        public static List<ItemGrid> ReadCsv(string filepath)
        {
            if (!File.Exists(filepath))
            {
                return null;
            }
            int parseScore = 0;
            var allLines = File.ReadAllLines(filepath).Where(arg => !string.IsNullOrWhiteSpace(arg));
            var result =
                from line in allLines.Skip(1).Take(allLines.Count() - 1) // Skip header row
                let temparry = line.Split(',')
                let parse = int.TryParse(temparry[1], out parseScore)
                let isCompleted = temparry.Length > 3 && temparry[3] != null && temparry[3] == "True"
                select new ItemGrid {
                    Task = temparry[0],
                    Score = parseScore,
                    Result = temparry[2],
                    IsActive = !isCompleted,
                    Importance = temparry.Length > 4 && !string.IsNullOrWhiteSpace(temparry[4]) ? temparry[4] : "Unknown",
                    Urgency = temparry.Length > 5 && !string.IsNullOrWhiteSpace(temparry[5]) ? temparry[5] : "Unknown",
                    CreatedDate = temparry.Length > 6 && DateTime.TryParse(temparry[6], out DateTime cd) ? cd : DateTime.Now,
                    LastModifiedDate = temparry.Length > 7 && DateTime.TryParse(temparry[7], out DateTime lmd) ? lmd : DateTime.Now,
                    KanbanStage = temparry.Length > 8 && !string.IsNullOrWhiteSpace(temparry[8]) ? temparry[8] : "Backlog",
                    KanbanOrder = temparry.Length > 9 && int.TryParse(temparry[9], out int ko) ? ko : 0
                };
            var result_list = new List<ItemGrid>();
            try
            {
                result_list = result.ToList();
            }
            catch (Exception ex) {
                Console.WriteLine($"Error parsing CSV lines in file {filepath}: {ex.Message}");
                result_list.Add(new ItemGrid { Task = $"csv文件错误: {filepath}", Score = 0, Result= "", IsActive = true, Importance = "Unknown", Urgency = "Unknown", CreatedDate = DateTime.Now, LastModifiedDate = DateTime.Now, KanbanStage="Backlog", KanbanOrder=0 });
            }
            return result_list;
        }

        public static void WriteCsv(IEnumerable<ItemGrid> items, string filepath)
        {
            var temparray = items.Select(item =>
                $"{item.Task},{item.Score},{item.Result},{(item.IsActive ? "False" : "True")},{item.Importance ?? "Unknown"},{item.Urgency ?? "Unknown"},{item.CreatedDate:o},{item.LastModifiedDate:o},{item.KanbanStage ?? "Backlog"},{item.KanbanOrder}"
            ).ToArray();
            var contents = new string[temparray.Length + 1]; // Adjusted size
            contents[0] = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate,kanbanStage,kanbanOrder"; // Header
            Array.Copy(temparray, 0, contents, 1, temparray.Length);

            try
            {
                File.WriteAllLines(filepath, contents);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing CSV file {filepath}: {ex.Message}");
                // Optionally, re-throw or handle more gracefully depending on application requirements
            }
        }
    }

    public class ItemGrid
    {
        public string Task { set; get; }
        public int Score { set; get; }
        public string Result { set; get; }
        /// <summary>
        /// Gets or sets a value indicating whether the task is active.
        /// True if the task is pending, False if it's completed.
        /// </summary>
        public bool IsActive { set; get; }
        public string Importance { set; get; } = "Unknown";
        public string Urgency { set; get; } = "Unknown";
        public DateTime CreatedDate { set; get; } = DateTime.Now;
        public DateTime LastModifiedDate { set; get; } = DateTime.Now;
        public string KanbanStage { get; set; } = "Backlog"; // Added default
        public int KanbanOrder { get; set; } = 0; // Added default
    }

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private LlmService _llmService;
        private bool _llmConfigErrorDetectedInLoad = false;
        private static readonly TimeSpan StaleTaskThreshold = TimeSpan.FromDays(14);

        private ItemGrid _draggedItem;
        private DataGrid _sourceDataGrid;

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpWindowClass, string lpWindowName);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);
        const int GWL_HWNDPARENT = -8;
        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        internal string currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        int task1_selected_indexs = -1;
        int task2_selected_indexs = -1;
        int task3_selected_indexs = -1;
        int task4_selected_indexs = -1;

        public async void loadDataGridView()
        {
            string configErrorSubstring = "LLM dummy response (Configuration Error: API key missing or placeholder)";
            _llmConfigErrorDetectedInLoad = false;

            string[] csvFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            DataGrid[] dataGrids = { task1, task2, task3, task4 };

            for (int i = 0; i < csvFiles.Length; i++)
            {
                string filePath = Path.Combine(currentPath, "data", csvFiles[i]);
                List<ItemGrid> items = HelperClass.ReadCsv(filePath);

                if (items == null)
                {
                    Console.WriteLine($"Error reading CSV file: {filePath}. Or file is empty/new.");
                    items = new List<ItemGrid>(); // Ensure items is not null
                }
                
                bool updated = false;
                
                dataGrids[i].ItemsSource = null;
                dataGrids[i].ItemsSource = items;
                if (!dataGrids[i].Items.SortDescriptions.Contains(new SortDescription("Score", ListSortDirection.Descending)))
                {
                    dataGrids[i].Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                }

                foreach (var item in items) // Iterate safely
                {
                    if (item.IsActive && (DateTime.Now - item.LastModifiedDate) > StaleTaskThreshold)
                    {
                        try
                        {
                            TimeSpan taskAge = DateTime.Now - item.LastModifiedDate;
                            Console.WriteLine($"Task '{item.Task}' is stale (age: {taskAge.Days} days). Generating reminder...");
                            var (reminder, suggestions) = await _llmService.GenerateTaskReminderAsync(item.Task, taskAge);

                            if (!_llmConfigErrorDetectedInLoad && reminder != null && reminder.Contains(configErrorSubstring))
                            {
                                _llmConfigErrorDetectedInLoad = true;
                            }

                            if (!string.IsNullOrWhiteSpace(reminder) || (suggestions != null && suggestions.Any()))
                            {
                                string[] decompositionKeywords = { "break it down", "decompose", "smaller pieces", "sub-tasks", "subtasks" };
                                string decompositionSuggestion = null;
                                List<string> otherSuggestions = new List<string>();

                                if (suggestions != null)
                                {
                                    foreach (var s in suggestions)
                                    {
                                        if (decompositionKeywords.Any(keyword => s.ToLowerInvariant().Contains(keyword)))
                                        {
                                            decompositionSuggestion = s;
                                        }
                                        else
                                        {
                                            otherSuggestions.Add(s);
                                        }
                                    }
                                }

                                if (decompositionSuggestion != null)
                                {
                                    var questionMessageBuilder = new System.Text.StringBuilder();
                                    if (!string.IsNullOrWhiteSpace(reminder)) questionMessageBuilder.AppendLine($"Reminder: {reminder}\n");
                                    if (otherSuggestions.Any())
                                    {
                                        questionMessageBuilder.AppendLine("Other Suggestions:");
                                        foreach (var s in otherSuggestions) questionMessageBuilder.AppendLine($"- {s}");
                                        questionMessageBuilder.AppendLine();
                                    }
                                    questionMessageBuilder.AppendLine($"LLM also suggests: \"{decompositionSuggestion}\"");
                                    questionMessageBuilder.AppendLine("Would you like to attempt to break this task into smaller pieces now?");

                                    var dialogResult = MessageBox.Show(this, questionMessageBuilder.ToString(), $"Action for Task: {item.Task}", MessageBoxButton.YesNo, MessageBoxImage.Question);

                                    if (dialogResult == MessageBoxResult.Yes)
                                    {
                                        var (decompositionStatus, subTaskStrings) = await _llmService.DecomposeTaskAsync(item.Task);

                                        if (decompositionStatus == DecompositionStatus.NeedsDecomposition && subTaskStrings != null && subTaskStrings.Any())
                                        {
                                            DecompositionResultWindow decompositionWindow = new DecompositionResultWindow(subTaskStrings, item.Importance, item.Urgency)
                                            {
                                                Owner = this
                                            };

                                            bool? addSubTasksDialogResult = decompositionWindow.ShowDialog();

                                            if (addSubTasksDialogResult == true && decompositionWindow.SelectedSubTasks.Any())
                                            {
                                                string parentImportance = decompositionWindow.ParentImportance;
                                                string parentUrgency = decompositionWindow.ParentUrgency;
                                                int targetQuadrantIndex = decompositionWindow.ParentQuadrantIndex;

                                                DataGrid targetGrid = dataGrids[targetQuadrantIndex];
                                                string targetCsvNumber = (targetQuadrantIndex + 1).ToString();

                                                var currentGridItems = targetGrid.ItemsSource as List<ItemGrid> ?? new List<ItemGrid>();

                                                int newTasksAddedCount = 0;
                                                foreach (var subTaskString in decompositionWindow.SelectedSubTasks)
                                                {
                                                    var newSubTask = new ItemGrid
                                                    {
                                                        Task = subTaskString,
                                                        Importance = parentImportance,
                                                        Urgency = parentUrgency,
                                                        Score = 0,
                                                        IsActive = true,
                                                        Result = string.Empty,
                                                        CreatedDate = DateTime.Now,
                                                        LastModifiedDate = DateTime.Now,
                                                        KanbanStage = "Backlog", // Default for new subtasks
                                                        KanbanOrder = 0 // Default order
                                                    };
                                                    currentGridItems.Add(newSubTask);
                                                    newTasksAddedCount++;
                                                }

                                                if (newTasksAddedCount > 0)
                                                {
                                                    RefreshDataGrid(targetGrid);
                                                    update_csv(targetGrid, targetCsvNumber);
                                                    MessageBox.Show(this, $"{newTasksAddedCount} new sub-task(s) added to the '{GetQuadrantName(targetQuadrantIndex)}' list.", "Sub-tasks Added", MessageBoxButton.OK, MessageBoxImage.Information);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            MessageBox.Show(this, $"Could not automatically decompose task '{item.Task}'. Status: {decompositionStatus}.", "Decomposition Result", MessageBoxButton.OK, MessageBoxImage.Warning);
                                        }
                                    }
                                }
                                else
                                {
                                    var messageBuilder = new System.Text.StringBuilder();
                                    if (!string.IsNullOrWhiteSpace(reminder)) messageBuilder.AppendLine($"Reminder: {reminder}\n");
                                    if (suggestions != null && suggestions.Any())
                                    {
                                        messageBuilder.AppendLine("Suggestions:");
                                        foreach (var s in suggestions) messageBuilder.AppendLine($"- {s}");
                                    }
                                    MessageBox.Show(this, messageBuilder.ToString(), $"Reminder for Task: {item.Task}", MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                            }
                            await Task.Delay(500);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error generating reminder for task '{item.Task}': {ex.Message}");
                        }
                    }
                }
                if (updated) // This 'updated' flag logic seems to be orphaned from previous LLM prio code.
                {
                    try
                    {
                        HelperClass.WriteCsv(items, filePath);
                        Console.WriteLine($"Saved updated tasks to {filePath} (after priority update, before reminder display).");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing updated CSV {filePath}: {ex.Message}");
                    }
                }
            }

            if (_llmConfigErrorDetectedInLoad)
            {
                MessageBox.Show(this, "During task loading, some AI assistant features may have been limited due to a configuration issue (e.g., missing or placeholder API key). Please check the application's setup if you expect full AI functionality.",
                                "LLM Configuration Issue", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SwitchViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (KanbanView.Visibility == Visibility.Visible)
            {
                KanbanView.Visibility = Visibility.Collapsed;
                QuadrantViewGrid.Visibility = Visibility.Visible;
                SwitchViewButton.Content = "Kanban View";
                loadDataGridView();
            }
            else
            {
                QuadrantViewGrid.Visibility = Visibility.Collapsed;
                KanbanView.Visibility = Visibility.Visible;
                if (KanbanView is KanbanBoardView kbv) // Simplified cast
                {
                     kbv.RefreshTasks();
                }
                else
                {
                     System.Diagnostics.Debug.WriteLine("KanbanView could not be cast to KanbanBoardView to refresh tasks.");
                }
                SwitchViewButton.Content = "Quadrant View";
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _llmService = LlmService.Create(); // Ensure LLM service is initialized
            // Load window position settings
            this.Top = Properties.Settings.Default.Top > 0 ? Properties.Settings.Default.Top : 100; // Provide default if not set
            this.Left = Properties.Settings.Default.Left > 0 ? Properties.Settings.Default.Left : 100; // Provide default

            loadDataGridView();

            task1.CellEditEnding += DataGrid_CellEditEnding;
            task2.CellEditEnding += DataGrid_CellEditEnding;
            task3.CellEditEnding += DataGrid_CellEditEnding;
            task4.CellEditEnding += DataGrid_CellEditEnding;
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                var column = e.Column as DataGridBoundColumn;
                if (column != null && column.Header != null && column.Header.ToString() == "Task")
                {
                    var item = e.Row.Item as ItemGrid;
                    if (item != null && e.EditingElement is TextBox textBox)
                    {
                        // item.Task is already updated by binding before this event for TextBox
                        // For logging, textBox.Text is the new value.
                        Console.WriteLine($"Task description may have changed. New: [{textBox.Text}] for item originally: [{item.Task}]");
                        // To be absolutely sure item.Task is updated if further logic depends on it *within this handler*:
                        // item.Task = textBox.Text; // Though usually not needed due to two-way binding by commit.
                        item.LastModifiedDate = DateTime.Now; // Update last modified date on edit

                        // Find the parent DataGrid to update the correct CSV
                        DataGrid parentGrid = FindParent<DataGrid>((DependencyObject)e.Row);
                        if(parentGrid != null)
                        {
                            string quadrantNumber = GetQuadrantNumber(parentGrid.Name);
                            if(quadrantNumber != null)
                            {
                                update_csv(parentGrid, quadrantNumber);
                            }
                        }
                    }
                }
            }
        }

        internal void update_csv(DataGrid dgv, string number, string basePath = null)
        {
            if (dgv == null) return;

            var itemsToSave = new List<ItemGrid>();
            if (dgv.ItemsSource is IEnumerable<ItemGrid> items)
            {
                itemsToSave.AddRange(items);
            }

            string dirPath = basePath ?? Path.Combine(currentPath, "data");
            if (!Directory.Exists(dirPath)) // Ensure data directory exists
            {
                try { Directory.CreateDirectory(dirPath); }
                catch (Exception ex) { Console.WriteLine($"Could not create data directory {dirPath}: {ex.Message}"); return; }
            }
            HelperClass.WriteCsv(itemsToSave, Path.Combine(dirPath, number + ".csv"));
        }

        private void task1_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // This event is often too noisy for saving. Consider saving on CellEditEnding or explicit save button.
            // task1_selected_indexs = task1.SelectedIndex;
            // update_csv(task1, "1");
        }

        private void task2_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // task2_selected_indexs = task2.SelectedIndex;
            // update_csv(task2, "2");
        }

        private void task3_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // task3_selected_indexs = task3.SelectedIndex;
            // update_csv(task3, "3");
        }

        private void task4_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // task4_selected_indexs = task4.SelectedIndex;
            // update_csv(task4, "4");
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Desktop parent attachment logic
            try
            {
                IntPtr pWnd = FindWindow("Progman", null);
                if (pWnd != IntPtr.Zero)
                {
                    pWnd = FindWindowEx(pWnd, IntPtr.Zero, "SHELLDLL_DefVIew", null);
                    if (pWnd != IntPtr.Zero)
                    {
                         pWnd = FindWindowEx(pWnd, IntPtr.Zero, "SysListView32", null);
                         if (pWnd != IntPtr.Zero)
                         {
                            IntPtr tWnd = new WindowInteropHelper(this).Handle;
                            SetParent(tWnd, pWnd);
                         }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting window parent: {ex.Message}");
            }
        }

        private void location_Save(object sender, EventArgs e)
        {
            Properties.Settings.Default.Top = this.Top;
            Properties.Settings.Default.Left = this.Left;
            Properties.Settings.Default.Save();
        }

        public void DeleteTaskRow_Click(object sender, RoutedEventArgs e)
        {
            Button deleteButton = sender as Button;
            if (deleteButton == null) return;

            ItemGrid taskToDelete = deleteButton.DataContext as ItemGrid;
            if (taskToDelete == null) return;

            DataGrid sourceGrid = null;
            string quadrantNumber = null;

            if (task1.ItemsSource is List<ItemGrid> tasks1List && tasks1List.Remove(taskToDelete)) { sourceGrid = task1; quadrantNumber = "1"; }
            else if (task2.ItemsSource is List<ItemGrid> tasks2List && tasks2List.Remove(taskToDelete)) { sourceGrid = task2; quadrantNumber = "2"; }
            else if (task3.ItemsSource is List<ItemGrid> tasks3List && tasks3List.Remove(taskToDelete)) { sourceGrid = task3; quadrantNumber = "3"; }
            else if (task4.ItemsSource is List<ItemGrid> tasks4List && tasks4List.Remove(taskToDelete)) { sourceGrid = task4; quadrantNumber = "4"; }

            if (sourceGrid != null && quadrantNumber != null)
            {
                RefreshDataGrid(sourceGrid);
                update_csv(sourceGrid, quadrantNumber);
            }
        }

        public static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            return parent ?? FindParent<T>(parentObject);
        }

        internal static string GetQuadrantNumber(string dataGridName)
        {
            switch (dataGridName)
            {
                case "task1": return "1";
                case "task2": return "2";
                case "task3": return "3";
                case "task4": return "4";
                default: return null;
            }
        }

        internal static string GetQuadrantName(int index)
        {
            switch (index)
            {
                case 0: return "Important & Urgent";
                case 1: return "Important & Not Urgent";
                case 2: return "Not Important & Urgent";
                case 3: return "Not Important & Not Urgent";
                default: return "Unknown Quadrant";
            }
        }

        private void RefreshDataGrid(DataGrid dataGrid)
        {
            if (dataGrid == null) return;
            var itemsSource = dataGrid.ItemsSource as List<ItemGrid>; // Keep as List<ItemGrid>
            dataGrid.ItemsSource = null;
            dataGrid.ItemsSource = itemsSource; // Re-assign to refresh
            if (itemsSource != null && !dataGrid.Items.SortDescriptions.Contains(new SortDescription("Score", ListSortDirection.Descending)))
            {
                dataGrid.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
            }
        }


        private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DependencyObject dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                {
                    if (dep is Button button && button.Name == "PART_DeleteButton") return;
                    dep = VisualTreeHelper.GetParent(dep);
                }

                DataGridRow row = e.Source as DataGridRow ?? FindParent<DataGridRow>((DependencyObject)e.OriginalSource);

                if (row != null && row.Item is ItemGrid item)
                {
                    _draggedItem = item;
                    _sourceDataGrid = FindParent<DataGrid>(row);
                    if (_sourceDataGrid != null)
                    {
                        DragDrop.DoDragDrop(row, _draggedItem, DragDropEffects.Move);
                    }
                }
            }
        }

        private void Task_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(ItemGrid)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void Task_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(ItemGrid)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        internal static bool ProcessTaskDrop(ItemGrid draggedItem, List<ItemGrid> sourceList, List<ItemGrid> targetList, string targetDataGridName)
        {
            if (draggedItem == null || sourceList == null || targetList == null || !sourceList.Contains(draggedItem))
            {
                return false;
            }

            sourceList.Remove(draggedItem);
            targetList.Add(draggedItem); // Add to end, then re-sort or re-score if necessary
            draggedItem.LastModifiedDate = DateTime.Now;

            string newImportance = "Unknown", newUrgency = "Unknown";
            switch (targetDataGridName)
            {
                case "task1": newImportance = "High"; newUrgency = "High"; break;
                case "task2": newImportance = "High"; newUrgency = "Low"; break;
                case "task3": newImportance = "Low"; newUrgency = "High"; break;
                case "task4": newImportance = "Low"; newUrgency = "Low"; break;
            }
            draggedItem.Importance = newImportance;
            draggedItem.Urgency = newUrgency;

            // Re-score target list (example: simple count down)
            for(int i=0; i < targetList.Count; i++) targetList[i].Score = targetList.Count - i;
            // Re-score source list
            for(int i=0; i < sourceList.Count; i++) sourceList[i].Score = sourceList.Count - i;

            return true;
        }

        internal static bool ProcessTaskReorder(ItemGrid draggedItem, List<ItemGrid> list, int originalIndex, int visualTargetIndex)
        {
            if (list == null || draggedItem == null || !list.Contains(draggedItem) || originalIndex < 0)
            {
                return false;
            }

            if (visualTargetIndex == originalIndex || visualTargetIndex == originalIndex + 1) return false; // No actual move

            list.RemoveAt(originalIndex);

            int actualInsertionIndex = visualTargetIndex;
            if (originalIndex < visualTargetIndex) actualInsertionIndex--;

            if (actualInsertionIndex < 0) actualInsertionIndex = 0;
            if (actualInsertionIndex > list.Count) actualInsertionIndex = list.Count;

            list.Insert(actualInsertionIndex, draggedItem);
            draggedItem.LastModifiedDate = DateTime.Now;

            for (int i = 0; i < list.Count; i++) list[i].Score = list.Count - i;
            return true;
        }

        private void Quadrant_Drop(object sender, DragEventArgs e)
        {
            if (_draggedItem == null || _sourceDataGrid == null)
            {
                e.Effects = DragDropEffects.None; e.Handled = true; return;
            }

            DataGrid targetDataGrid = sender as DataGrid;
            if (targetDataGrid == null)
            {
                _draggedItem = null; _sourceDataGrid = null; e.Handled = true; return;
            }

            if (e.Data.GetDataPresent(typeof(ItemGrid)))
            {
                ItemGrid currentDraggedItem = _draggedItem; // Use consistent variable
                var sourceList = _sourceDataGrid.ItemsSource as List<ItemGrid>;

                if (targetDataGrid == _sourceDataGrid)
                {
                    if (sourceList != null && currentDraggedItem != null && sourceList.Contains(currentDraggedItem))
                    {
                        Point mousePosition = e.GetPosition(targetDataGrid);
                        int visualDropIndex = -1;
                        for (int i = 0; i < targetDataGrid.Items.Count; i++)
                        {
                            var row = (DataGridRow)targetDataGrid.ItemContainerGenerator.ContainerFromIndex(i);
                            if (row != null)
                            {
                                Point rowTopLeftInGrid = row.TransformToAncestor(targetDataGrid).Transform(new Point(0, 0));
                                if (mousePosition.Y < rowTopLeftInGrid.Y + row.ActualHeight / 2)
                                {
                                    visualDropIndex = i; break;
                                }
                            }
                        }
                        if (visualDropIndex == -1) visualDropIndex = targetDataGrid.Items.Count;

                        int originalIndex = sourceList.IndexOf(currentDraggedItem);

                        if (ProcessTaskReorder(currentDraggedItem, sourceList, originalIndex, visualDropIndex))
                        {
                            RefreshDataGrid(targetDataGrid);
                            string quadrantNumber = GetQuadrantNumber(targetDataGrid.Name);
                            if (quadrantNumber != null) update_csv(targetDataGrid, quadrantNumber);
                        }
                    }
                }
                else
                {
                    var targetList = targetDataGrid.ItemsSource as List<ItemGrid> ?? new List<ItemGrid>();
                    if (!(targetDataGrid.ItemsSource is List<ItemGrid>)) targetDataGrid.ItemsSource = targetList; // Ensure it's set if new

                    if (ProcessTaskDrop(currentDraggedItem, sourceList, targetList, targetDataGrid.Name))
                    {
                        string sourceQuadrantNumber = GetQuadrantNumber(_sourceDataGrid.Name);
                        string targetQuadrantNumber = GetQuadrantNumber(targetDataGrid.Name);

                        if (sourceQuadrantNumber != null) update_csv(_sourceDataGrid, sourceQuadrantNumber);
                        if (targetQuadrantNumber != null) update_csv(targetDataGrid, targetQuadrantNumber);

                        RefreshDataGrid(_sourceDataGrid);
                        RefreshDataGrid(targetDataGrid);
                    }
                }
            }
            _draggedItem = null;
            _sourceDataGrid = null;
            e.Handled = true;
        }
    }
}
