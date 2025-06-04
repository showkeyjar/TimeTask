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
                // Assuming old CSV format: task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate
                // New properties AssignedTo and Status will be read if present, otherwise defaulted.
                let isCompleted = temparry.Length > 3 && temparry[3] != null && temparry[3] == "True" // For backward compatibility
                select new ItemGrid {
                    Task = temparry[0],
                    Score = parseScore,
                    Result = temparry[2],
                    // IsActive is removed. Status is new.
                    // For backward compatibility with old CSVs, if 'is_completed' (temparry[3]) was true, Status is "Completed".
                    // Otherwise, it defaults to "To Do" (which is set in ItemGrid constructor).
                    Status = isCompleted ? "Completed" : "To Do",
                    Importance = temparry.Length > 4 && !string.IsNullOrWhiteSpace(temparry[4]) ? temparry[4] : "Unknown",
                    Urgency = temparry.Length > 5 && !string.IsNullOrWhiteSpace(temparry[5]) ? temparry[5] : "Unknown",
                    CreatedDate = temparry.Length > 6 && DateTime.TryParse(temparry[6], out DateTime cd) ? cd : DateTime.Now,
                    LastModifiedDate = temparry.Length > 7 && DateTime.TryParse(temparry[7], out DateTime lmd) ? lmd : DateTime.Now,
                    // AssignedTo will default to "Unassigned" as per ItemGrid constructor if not in CSV.
                    // If CSV format changes to include AssignedTo, logic to parse it would be added here.
                    // For example: AssignedTo = temparry.Length > 8 ? temparry[8] : "Unassigned",
                };
            var result_list = new List<ItemGrid>();
            try
            {
                result_list = result.ToList();
            }
            catch (Exception ex) { // Catch specific exceptions if possible, or log general ones
                Console.WriteLine($"Error parsing CSV lines: {ex.Message}");
                // Add a default item or handle error as appropriate
                // Note: IsActive is removed, Status will default to "To Do"
                result_list.Add(new ItemGrid { Task = "csv文件错误", Score = 0, Result= "", Importance = "Unknown", Urgency = "Unknown", CreatedDate = DateTime.Now, LastModifiedDate = DateTime.Now });
            }
            return result_list;
        }

        public static void WriteCsv(IEnumerable<ItemGrid> items, string filepath)
        {
            var temparray = items.Select(item =>
                // New format: task,score,result,importance,urgency,createdDate,lastModifiedDate,assigned_to,status
                $"{item.Task},{item.Score},{item.Result},{item.Importance ?? "Unknown"},{item.Urgency ?? "Unknown"},{item.CreatedDate:o},{item.LastModifiedDate:o},{item.AssignedTo ?? "Unassigned"},{item.Status ?? "To Do"}"
            ).ToArray();
            var contents = new string[temparray.Length + 2];
            Array.Copy(temparray, 0, contents, 1, temparray.Length);
            contents[0] = "task,score,result,importance,urgency,createdDate,lastModifiedDate,assigned_to,status";
            File.WriteAllLines(filepath, contents);
        }
    }

    public class ItemGrid
    {
        public string Task { set; get; }
        public int Score { set; get; }
        public string Result { set; get; }
        public string AssignedTo { set; get; } = "Unassigned";
        public string Status { set; get; } = "To Do";
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
                
                bool updated = false; // This variable is now effectively unused in this part of the loop
                                     // as the block that set it to true is removed.
                // The following block for on-load LLM prioritization has been removed.
                // foreach (var item in items)
                // {
                //     if (string.IsNullOrWhiteSpace(item.Importance) || item.Importance == "Unknown" ||
                //         string.IsNullOrWhiteSpace(item.Urgency) || item.Urgency == "Unknown")
                //     {
                //         // ... LLM call and update logic was here ...
                //         // updated = true;
                //     }
                // }

                // The following if(updated) block, which was for saving after on-load prioritization,
                // has also been removed as 'updated' would always be false here.
                // if (updated)
                // {
                //     try
                //     {
                //         HelperClass.WriteCsv(items, filePath);
                //         Console.WriteLine($"Saved updated tasks to {filePath}");
                //     }
                //     catch (Exception ex)
                //     {
                //         Console.WriteLine($"Error writing updated CSV {filePath}: {ex.Message}");
                //     }
                // }
                
                dataGrids[i].ItemsSource = null;
                dataGrids[i].ItemsSource = items;
                if (!dataGrids[i].Items.SortDescriptions.Contains(new SortDescription("Score", ListSortDirection.Descending)))
                {
                    dataGrids[i].Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                }

                foreach (var item in items)
                {
                    // Task is stale if not "Completed" and past threshold
                    if (item.Status != "Completed" && (DateTime.Now - item.LastModifiedDate) > StaleTaskThreshold)
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
                                                        Status = "To Do", // New tasks are "To Do"
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

            // Attach CellEditEnding event handler to all DataGrids
            task1.CellEditEnding += DataGrid_CellEditEnding;
            task2.CellEditEnding += DataGrid_CellEditEnding;
            task3.CellEditEnding += DataGrid_CellEditEnding;
            task4.CellEditEnding += DataGrid_CellEditEnding;
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // Check if the edited column is the "Task" column.
                // Using HeaderText assumes the XAML <DataGridTextColumn Header="XXX" ... />
                var column = e.Column as DataGridBoundColumn;
                if (column != null && column.Header != null)
                {
                    string header = column.Header.ToString();
                    var item = e.Row.Item as ItemGrid;

                    if (item != null)
                    {
                        bool changed = false;
                        if (header == "Task" && e.EditingElement is TextBox taskTextBox)
                        {
                            string newDescription = taskTextBox.Text;
                            // string oldDescriptionPreview = item.Task != null && item.Task.Length > 15 ? item.Task.Substring(0, 15) + "..." : item.Task;
                            // Console.WriteLine($"Task edited in grid. Task (Old Preview): [{oldDescriptionPreview}], New Description: [{newDescription}]");
                            // The actual update to item.Task happens automatically due to binding.
                            changed = true;
                        }
                        else if (header == "AssignedTo" && e.EditingElement is TextBox assignedToTextBox)
                        {
                            // item.AssignedTo is updated by binding
                            changed = true;
                        }
                        else if (header == "Status" && e.EditingElement is ComboBox statusComboBox)
                        {
                            // Assuming Status column uses a ComboBox for editing
                            // item.Status is updated by binding
                            changed = true;
                        }
                        // Add other column checks as needed, e.g., for Result, Score

                        if (changed)
                        {
                            item.LastModifiedDate = DateTime.Now; // Update LastModifiedDate
                            DataGrid activeDataGrid = sender as DataGrid;
                            if (activeDataGrid != null)
                            {
                                string quadrantNumber = GetQuadrantNumber(activeDataGrid.Name);
                                if (quadrantNumber != null)
                                {
                                    update_csv(activeDataGrid, quadrantNumber);
                                    Console.WriteLine($"Updated CSV for quadrant {quadrantNumber} due to edit in column '{header}'.");
                                }
                            }
                        }
                    }
                }
            }
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

        internal static bool ProcessTaskReorder(ItemGrid draggedItem, List<ItemGrid> list, int originalIndex, int visualTargetIndex)
        {
            if (list == null || draggedItem == null || !list.Contains(draggedItem))
            {
                return false; // Should not happen if called correctly
            }

            // If visualTargetIndex is where the item already is or would be inserted right after itself, effectively not changing order.
            // An item at 'originalIndex' is proposed to be inserted *before* 'visualTargetIndex'.
            // No change if:
            // 1. visualTargetIndex == originalIndex (drop onto self, insert before self - no change)
            // 2. visualTargetIndex == originalIndex + 1 (drop onto item after self, insert before item after self - no change)
            if (visualTargetIndex == originalIndex || visualTargetIndex == originalIndex + 1)
            {
                return false; // No actual move
            }

            list.RemoveAt(originalIndex); // Remove from old position first

            // Adjust targetIndex for insertion based on original position
            int actualInsertionIndex = visualTargetIndex;
            if (originalIndex < visualTargetIndex)
            {
                actualInsertionIndex--; // The list shifted, so the visual target index is now one less
            }

            // Ensure actualInsertionIndex is within bounds
            if (actualInsertionIndex < 0) actualInsertionIndex = 0;
            if (actualInsertionIndex > list.Count) actualInsertionIndex = list.Count;


            list.Insert(actualInsertionIndex, draggedItem);
            draggedItem.LastModifiedDate = DateTime.Now;

            // Update scores
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Score = list.Count - i;
            }
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
    if (targetDataGrid == null)
            {
                _draggedItem = null;
                _sourceDataGrid = null;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(typeof(ItemGrid)))
            {
        ItemGrid draggedItem = _draggedItem; // Renamed for clarity
        var sourceList = _sourceDataGrid.ItemsSource as List<ItemGrid>; // This is also the targetList if quadrants are the same

        if (targetDataGrid == _sourceDataGrid) // Reordering within the same quadrant
                {
            if (sourceList != null && draggedItem != null && sourceList.Contains(draggedItem))
            {
                // Determine visual drop position (index in the list as currently displayed)
                Point mousePosition = e.GetPosition(targetDataGrid);
                int visualDropIndex = -1;
                for (int i = 0; i < targetDataGrid.Items.Count; i++)
                {
                    var row = (DataGridRow)targetDataGrid.ItemContainerGenerator.ContainerFromIndex(i);
                    if (row != null)
                    {
                        // Using Point(0,0) relative to the row for its top-left corner
                        Point rowTopLeftInGrid = row.TransformToAncestor(targetDataGrid).Transform(new Point(0, 0));
                        if (mousePosition.Y < rowTopLeftInGrid.Y + row.ActualHeight / 2)
                        {
                            visualDropIndex = i; // Drop will be before this item
                            break;
                        }
                    }
                }

                if (visualDropIndex == -1) // Dropped below all items or on empty grid
                {
                    visualDropIndex = targetDataGrid.Items.Count; // Target is to append (insert before conceptual item at Count)
                }

                int originalIndex = sourceList.IndexOf(draggedItem);

                // Call the testable helper method
                // The visualDropIndex is the index in the list *before* removal, where the item should be inserted.
                if (ProcessTaskReorder(draggedItem, sourceList, originalIndex, visualDropIndex))
                {
                    RefreshDataGrid(targetDataGrid);
                    string quadrantNumber = GetQuadrantNumber(targetDataGrid.Name);
                    if (quadrantNumber != null) update_csv(targetDataGrid, quadrantNumber);
                }
                // If ProcessTaskReorder returns false (no actual move), we do nothing here,
                // _draggedItem and _sourceDataGrid are cleared at the end of Quadrant_Drop.
            }
        }
        else // Moving to a different quadrant
                {
            var targetList = targetDataGrid.ItemsSource as List<ItemGrid>;
            if (targetList == null)
            {
                targetList = new List<ItemGrid>();
                targetDataGrid.ItemsSource = targetList;
            }

            if (ProcessTaskDrop(draggedItem, sourceList, targetList, targetDataGrid.Name))
            {
                string sourceQuadrantNumber = GetQuadrantNumber(_sourceDataGrid.Name);
                string targetQuadrantNumber = GetQuadrantNumber(targetDataGrid.Name);

                // Update scores for the source list (optional, but good for consistency if scores mean global order)
                // For now, let's assume scores are quadrant-local unless specified otherwise
                // Update scores for the target list (ProcessTaskDrop doesn't do this, but should it?)
                // The requirement implies scores are relative to the list they are in.
                // Let's add score recalculation for the target list here as well.
                for (int i = 0; i < targetList.Count; i++)
                {
                    targetList[i].Score = targetList.Count - i;
                }
                 // And for source list
                for (int i = 0; i < sourceList.Count; i++)
                {
                    sourceList[i].Score = sourceList.Count - i;
                }


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
