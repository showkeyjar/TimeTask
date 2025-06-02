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
                from line in allLines.Skip(1).Take(allLines.Count() - 1)
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
                    LastModifiedDate = temparry.Length > 7 && DateTime.TryParse(temparry[7], out DateTime lmd) ? lmd : DateTime.Now
                };
            var result_list = new List<ItemGrid>();
            try
            {
                result_list = result.ToList();
            }
            catch (Exception ex) { // Catch specific exceptions if possible, or log general ones
                Console.WriteLine($"Error parsing CSV lines: {ex.Message}");
                // Add a default item or handle error as appropriate
                result_list.Add(new ItemGrid { Task = "csv文件错误", Score = 0, Result= "", IsActive = true, Importance = "Unknown", Urgency = "Unknown", CreatedDate = DateTime.Now, LastModifiedDate = DateTime.Now });
            }
            return result_list;
        }

        public static void WriteCsv(IEnumerable<ItemGrid> items, string filepath)
        {
            var temparray = items.Select(item =>
                $"{item.Task},{item.Score},{item.Result},{(item.IsActive ? "False" : "True")},{item.Importance ?? "Unknown"},{item.Urgency ?? "Unknown"},{item.CreatedDate:o},{item.LastModifiedDate:o}"
            ).ToArray();
            var contents = new string[temparray.Length + 2];
            Array.Copy(temparray, 0, contents, 1, temparray.Length);
            contents[0] = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate";
            File.WriteAllLines(filepath, contents);
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

    }

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
// using System.Threading.Tasks; // Moved to top

// Removed redundant nested namespace TimeTask
    public partial class MainWindow : Window
    {
        private LlmService _llmService;
        private bool _llmConfigErrorDetectedInLoad = false; // Flag for LLM config error during load
        private static readonly TimeSpan StaleTaskThreshold = TimeSpan.FromDays(14); // 2 weeks

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

        internal string currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); // Made internal for test access

        int task1_selected_indexs = -1;
        int task2_selected_indexs = -1;
        int task3_selected_indexs = -1;
        int task4_selected_indexs = -1;

        public async void loadDataGridView()
        {
            string configErrorSubstring = "LLM dummy response (Configuration Error: API key missing or placeholder)";
            _llmConfigErrorDetectedInLoad = false; // Initialize/reset for current load operation

            string[] csvFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            DataGrid[] dataGrids = { task1, task2, task3, task4 };

            for (int i = 0; i < csvFiles.Length; i++)
            {
                string filePath = Path.Combine(currentPath, "data", csvFiles[i]);
                List<ItemGrid> items = HelperClass.ReadCsv(filePath);

                if (items == null)
                {
                    Console.WriteLine($"Error reading CSV file: {filePath}. Or file is empty/new.");
                    items = new List<ItemGrid>();
                }
                
                bool updated = false;
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Importance) || item.Importance == "Unknown" ||
                        string.IsNullOrWhiteSpace(item.Urgency) || item.Urgency == "Unknown")
                    {
                        try
                        {
                            Console.WriteLine($"Getting priority for task: {item.Task}");
                            var (importance, urgency) = await _llmService.GetTaskPriorityAsync(item.Task);

                            if (!_llmConfigErrorDetectedInLoad &&
                                ((importance != null && importance.Contains(configErrorSubstring)) ||
                                 (urgency != null && urgency.Contains(configErrorSubstring))))
                            {
                                _llmConfigErrorDetectedInLoad = true;
                            }

                            item.Importance = importance;
                            item.Urgency = urgency;
                            item.LastModifiedDate = DateTime.Now;
                            updated = true;
                            Console.WriteLine($"Updated Task: {item.Task}, Importance: {item.Importance}, Urgency: {item.Urgency}, LastModified: {item.LastModifiedDate}");
                            await Task.Delay(500);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting priority for task '{item.Task}': {ex.Message}");
                        }
                    }
                }

                if (updated)
                {
                    try
                    {
                        HelperClass.WriteCsv(items, filePath);
                        Console.WriteLine($"Saved updated tasks to {filePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing updated CSV {filePath}: {ex.Message}");
                    }
                }
                
                dataGrids[i].ItemsSource = null;
                dataGrids[i].ItemsSource = items;
                if (!dataGrids[i].Items.SortDescriptions.Contains(new SortDescription("Score", ListSortDirection.Descending)))
                {
                    dataGrids[i].Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                }

                foreach (var item in items)
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
                                // We check the reminder string itself for the config error.
                                // If the reminder is the config error string, it's unlikely to trigger useful suggestions
                                // or decomposition prompts, but the check is placed before its content is evaluated.

                                string[] decompositionKeywords = { "break it down", "decompose", "smaller pieces", "sub-tasks", "subtasks" };
                                string decompositionSuggestion = null;
                                List<string> otherSuggestions = new List<string>();

                                if (suggestions != null)
                                {
                                    foreach (var s in suggestions)
                                    {
                                        if (decompositionKeywords.Any(keyword => s.ToLowerInvariant().Contains(keyword)))
                                        {
                                            decompositionSuggestion = s; // Store the first one found
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
                                    if (!string.IsNullOrWhiteSpace(reminder))
                                    {
                                        questionMessageBuilder.AppendLine($"Reminder: {reminder}");
                                        questionMessageBuilder.AppendLine();
                                    }
                                    if (otherSuggestions.Any())
                                    {
                                        questionMessageBuilder.AppendLine("Other Suggestions:");
                                        foreach (var s in otherSuggestions)
                                        {
                                            questionMessageBuilder.AppendLine($"- {s}");
                                        }
                                        questionMessageBuilder.AppendLine();
                                    }
                                    questionMessageBuilder.AppendLine($"LLM also suggests: \"{decompositionSuggestion}\"");
                                    questionMessageBuilder.AppendLine("Would you like to attempt to break this task into smaller pieces now?");

                                    var dialogResult = MessageBox.Show(this, questionMessageBuilder.ToString(), $"Action for Task: {item.Task}", MessageBoxButton.YesNo, MessageBoxImage.Question);

                                    if (dialogResult == MessageBoxResult.Yes)
                                    {
                                        var (decompositionStatus, subTaskStrings) = await _llmService.DecomposeTaskAsync(item.Task);
                                        // Note: Checking subTaskStrings for the specific configErrorSubstring is not straightforward here,
                                        // as DecomposeTaskAsync returns a status and a list, not the raw LLM string.
                                        // The LlmService's GetCompletionAsync would return the raw error, but ParseDecompositionResponse
                                        // would likely turn that into DecompositionStatus.Unknown.
                                        // The user would see a "Could not automatically decompose task" message in that case.
                                        // The generic _llmConfigErrorDetectedInLoad flag (if set by priority/reminder calls)
                                        // will cover notifying the user about potential underlying config issues.

                                        if (decompositionStatus == DecompositionStatus.NeedsDecomposition && subTaskStrings != null && subTaskStrings.Any())
                                        {
                                            // Current loop index 'i' corresponds to the parent task's quadrant (0-3)
                                            // Or, we can use item.Importance and item.Urgency directly
                                            DecompositionResultWindow decompositionWindow = new DecompositionResultWindow(subTaskStrings, item.Importance, item.Urgency)
                                            {
                                                Owner = this // Ensure the new window is owned by MainWindow
                                            };

                                            bool? addSubTasksDialogResult = decompositionWindow.ShowDialog();

                                            if (addSubTasksDialogResult == true && decompositionWindow.SelectedSubTasks.Any())
                                            {
                                                string parentImportance = decompositionWindow.ParentImportance;
                                                string parentUrgency = decompositionWindow.ParentUrgency;
                                                int targetQuadrantIndex = decompositionWindow.ParentQuadrantIndex; // Resolved in Decomp window

                                                DataGrid targetGrid = dataGrids[targetQuadrantIndex]; // Get the target DataGrid
                                                string targetCsvNumber = (targetQuadrantIndex + 1).ToString();

                                                var currentGridItems = targetGrid.ItemsSource as List<ItemGrid>;
                                                if (currentGridItems == null) currentGridItems = new List<ItemGrid>();

                                                int newTasksAddedCount = 0;
                                                foreach (var subTaskString in decompositionWindow.SelectedSubTasks)
                                                {
                                                    var newSubTask = new ItemGrid
                                                    {
                                                        Task = subTaskString,
                                                        Importance = parentImportance,
                                                        Urgency = parentUrgency,
                                                        Score = 0, // Default score
                                                        IsActive = true,
                                                        Result = string.Empty,
                                                        CreatedDate = DateTime.Now,
                                                        LastModifiedDate = DateTime.Now
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
                                    // If No to decomposition prompt, do nothing further for this interaction.
                                }
                                else
                                {
                                    // Original logic: No decomposition suggestion found, show reminder and all suggestions.
                                    var messageBuilder = new System.Text.StringBuilder();
                                    if (!string.IsNullOrWhiteSpace(reminder))
                                    {
                                        messageBuilder.AppendLine($"Reminder: {reminder}");
                                        messageBuilder.AppendLine();
                                    }
                                    if (suggestions != null && suggestions.Any()) // suggestions here means otherSuggestions is empty or all suggestions
                                    {
                                        messageBuilder.AppendLine("Suggestions:");
                                        foreach (var s in suggestions) // Show all original suggestions
                                        {
                                            messageBuilder.AppendLine($"- {s}");
                                        }
                                    }
                                    MessageBox.Show(this, messageBuilder.ToString(), $"Reminder for Task: {item.Task}", MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                            }
                            await Task.Delay(500); // Keep the delay whether decomposition happened or not
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error generating reminder for task '{item.Task}': {ex.Message}");
                        }
                    }
                }
                // Note: The 'updated' flag and subsequent WriteCsv call seem to be potentially duplicated.
                // The first WriteCsv inside the priority update loop saves changes.
                // This second 'updated' check might refer to a different set of updates or be redundant.
                // For this subtask, I am focusing only on adding the LLM config error checks.
                // The existing logic for when and how CSV is written is preserved.
                if (updated) 
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

            // After all files are processed, show a single notification if LLM config error was detected
            if (_llmConfigErrorDetectedInLoad)
            {
                MessageBox.Show(this, "During task loading, some AI assistant features may have been limited due to a configuration issue (e.g., missing or placeholder API key). Please check the application's setup if you expect full AI functionality.",
                                "LLM Configuration Issue", MessageBoxButton.OK, MessageBoxImage.Warning);
                // As per requirement, not resetting _llmConfigErrorDetectedInLoad here.
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _llmService = LlmService.Create();
            this.Top = (double)Properties.Settings.Default.Top;
            this.Left = (double)Properties.Settings.Default.Left;
            loadDataGridView();
        }

        internal void update_csv(DataGrid dgv, string number, string basePath = null) { // Added basePath for testing flexibility
            if (dgv == null) return; // Simplified for testing if dgv is null

            var itemsToSave = new List<ItemGrid>();
            if (dgv.ItemsSource is IEnumerable<ItemGrid> items)
            {
                itemsToSave.AddRange(items);
            }
            // In a test scenario, dgv.ItemsSource might be directly set to List<ItemGrid>
            // and dgv.Items might not be populated if the DataGrid is not rendered.
            // Iterating dgv.ItemsSource is generally more reliable for data access.

            string dirPath = basePath ?? Path.Combine(currentPath, "data");
            if (!Directory.Exists(dirPath) && basePath != null) // Create test data directory if specified and not exists
            {
                Directory.CreateDirectory(dirPath);
            }
            HelperClass.WriteCsv(itemsToSave, Path.Combine(dirPath, number + ".csv"));
        }

        private void task1_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            task1_selected_indexs = task1.SelectedIndex;
            update_csv(task1, "1");
        }

        private void task2_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            task2_selected_indexs = task2.SelectedIndex;
            update_csv(task2, "2");
        }

        private void task3_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            task3_selected_indexs = task3.SelectedIndex;
            update_csv(task3, "3");
        }

        private void task4_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            task4_selected_indexs = task4.SelectedIndex;
            update_csv(task4, "4");
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //base.OnMouseLeftButtonDown(e);
            //this.DragMove();
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            IntPtr pWnd = FindWindow("Progman", null);
            pWnd = FindWindowEx(pWnd, IntPtr.Zero, "SHELLDLL_DefVIew", null);
            pWnd = FindWindowEx(pWnd, IntPtr.Zero, "SysListView32", null);
            IntPtr tWnd = new WindowInteropHelper(this).Handle;
            SetParent(tWnd, pWnd);
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

            if (task1.ItemsSource is List<ItemGrid> tasks1List && tasks1List.Remove(taskToDelete)) sourceGrid = task1;
            else if (task2.ItemsSource is List<ItemGrid> tasks2List && tasks2List.Remove(taskToDelete)) sourceGrid = task2;
            else if (task3.ItemsSource is List<ItemGrid> tasks3List && tasks3List.Remove(taskToDelete)) sourceGrid = task3;
            else if (task4.ItemsSource is List<ItemGrid> tasks4List && tasks4List.Remove(taskToDelete)) sourceGrid = task4;

            if (sourceGrid != null)
            {
                RefreshDataGrid(sourceGrid); // Refresh the specific grid
                update_csv(sourceGrid, GetQuadrantNumber(sourceGrid.Name));
            }
        }

        public static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindParent<T>(parentObject);
        }

        internal static string GetQuadrantNumber(string dataGridName) // Made internal static for testing. Returns "1", "2", "3", "4"
        {
            switch (dataGridName)
            {
                case "task1": return "1"; // Corresponds to index 0
                case "task2": return "2"; // Corresponds to index 1
                case "task3": return "3"; // Corresponds to index 2
                case "task4": return "4"; // Corresponds to index 3
                default: return null;
            }
        }

        // Helper to get a user-friendly quadrant name from index
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
            var itemsSource = dataGrid.ItemsSource as List<ItemGrid>;
            dataGrid.ItemsSource = null;
            dataGrid.ItemsSource = itemsSource;
            if (itemsSource != null && !dataGrid.Items.SortDescriptions.Contains(new SortDescription("Score", ListSortDirection.Descending)))
            {
                dataGrid.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
            }
        }


        private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // Check if the click originated from the delete button
                DependencyObject dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                {
                    if (dep is Button button && button.Name == "PART_DeleteButton")
                    {
                        // Click was on the delete button, so don't start drag
                        return;
                    }
                    dep = VisualTreeHelper.GetParent(dep);
                }

                DataGridRow row = e.Source as DataGridRow;
                if (row == null)
                {
                    // If the source isn't the row itself (e.g. a cell), try to find the parent row.
                    // This part of your existing logic seems correct for finding the row.
                    row = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
                }

                if (row != null && row.Item is ItemGrid item)
                {
                    _draggedItem = item;
                    _sourceDataGrid = FindParent<DataGrid>(row);
                    if (_sourceDataGrid != null)
                    {
                        // Ensure that we are not trying to drag if the source was PART_DeleteButton
                        // The check above should handle this, but as a safeguard for _sourceDataGrid being set
                        // by FindParent<DataGrid>(row) even if the click was on a button within that row.
                        // However, the initial check on e.OriginalSource is more direct.
                        DragDrop.DoDragDrop(row, _draggedItem, DragDropEffects.Move);
                    }
                }
            }
        }

        private void Task_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(ItemGrid)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Task_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(ItemGrid)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        // Extracted core logic for testability
        internal static bool ProcessTaskDrop(ItemGrid draggedItem, List<ItemGrid> sourceList, List<ItemGrid> targetList, string targetDataGridName)
        {
            if (draggedItem == null || sourceList == null || targetList == null)
            {
                return false;
            }

            if (!sourceList.Contains(draggedItem))
            {
                return false;
            }

            sourceList.Remove(draggedItem);
            targetList.Add(draggedItem);
            draggedItem.LastModifiedDate = DateTime.Now;

            string newImportance = "Unknown";
            string newUrgency = "Unknown";

            switch (targetDataGridName)
            {
                case "task1": newImportance = "High"; newUrgency = "High"; break;
                case "task2": newImportance = "High"; newUrgency = "Low"; break;
                case "task3": newImportance = "Low"; newUrgency = "High"; break;
                case "task4": newImportance = "Low"; newUrgency = "Low"; break;
            }

            draggedItem.Importance = newImportance;
            draggedItem.Urgency = newUrgency;

            return true;
        }

        private void Quadrant_Drop(object sender, DragEventArgs e)
        {
            if (_draggedItem == null || _sourceDataGrid == null)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            DataGrid targetDataGrid = sender as DataGrid;
            if (targetDataGrid == null || targetDataGrid == _sourceDataGrid)
            {
                _draggedItem = null;
                _sourceDataGrid = null;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(typeof(ItemGrid)))
            {
                ItemGrid droppedItem = _draggedItem;
                var sourceList = _sourceDataGrid.ItemsSource as List<ItemGrid>;
                var targetList = targetDataGrid.ItemsSource as List<ItemGrid>;

                if (targetList == null)
                {
                    targetList = new List<ItemGrid>();
                    targetDataGrid.ItemsSource = targetList;
                }

                if (ProcessTaskDrop(droppedItem, sourceList, targetList, targetDataGrid.Name))
                {
                    string sourceQuadrantNumber = GetQuadrantNumber(_sourceDataGrid.Name);
                    string targetQuadrantNumber = GetQuadrantNumber(targetDataGrid.Name);

                    if (sourceQuadrantNumber != null) update_csv(_sourceDataGrid, sourceQuadrantNumber);
                    if (targetQuadrantNumber != null) update_csv(targetDataGrid, targetQuadrantNumber);

                    RefreshDataGrid(_sourceDataGrid);
                    RefreshDataGrid(targetDataGrid);
                }
            }
            _draggedItem = null;
            _sourceDataGrid = null;
            e.Handled = true;
        }
    }
}
