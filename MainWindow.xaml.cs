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
                    LastModifiedDate = temparry.Length > 7 && DateTime.TryParse(temparry[7], out DateTime lmd) ? lmd : DateTime.Now,
                    AssignedRole = temparry.Length > 8 && !string.IsNullOrWhiteSpace(temparry[8]) ? temparry[8] : "Unassigned"
                };
            var result_list = new List<ItemGrid>();
            try
            {
                result_list = result.ToList();
            }
            catch (Exception ex) { // Catch specific exceptions if possible, or log general ones
                Console.WriteLine($"Error parsing CSV lines: {ex.Message}");
                // Add a default item or handle error as appropriate
                result_list.Add(new ItemGrid { Task = "csv文件错误", Score = 0, Result= "", IsActive = true, Importance = "Unknown", Urgency = "Unknown", CreatedDate = DateTime.Now, LastModifiedDate = DateTime.Now, AssignedRole = "Unassigned" });
            }
            return result_list;
        }

        public static void WriteCsv(IEnumerable<ItemGrid> items, string filepath)
        {
            var temparray = items.Select(item =>
                $"{item.Task},{item.Score},{item.Result},{(item.IsActive ? "False" : "True")},{item.Importance ?? "Unknown"},{item.Urgency ?? "Unknown"},{item.CreatedDate:o},{item.LastModifiedDate:o},{item.AssignedRole ?? "Unassigned"}"
            ).ToArray();
            var contents = new string[temparray.Length + 1]; // +1 for header
            Array.Copy(temparray, 0, contents, 1, temparray.Length);
            contents[0] = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate,assignedRole";
            // Ensure the directory exists before writing
            string directoryPath = Path.GetDirectoryName(filepath);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
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
        public string AssignedRole { get; set; } = "Unassigned"; // Default value
    }

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
// using System.Threading.Tasks; // Moved to top

// Removed redundant nested namespace TimeTask
    public partial class MainWindow : Window
    {
        public List<string> UserRoles { get; set; } = new List<string> { "Manager", "Developer", "QA", "Designer" };
        public string SelectedRole { get; set; }
        private LlmService _llmService;
        private static readonly TimeSpan StaleTaskThreshold = TimeSpan.FromDays(14); // 2 weeks

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpWindowClass, string lpWindowName);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);
        const int GWL_HWNDPARENT = -8;
        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        string currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        // task1_selected_indexs, task2_selected_indexs, task3_selected_indexs, task4_selected_indexs REMOVED

        public class ItemGridComparer : IEqualityComparer<ItemGrid>
        {
            public bool Equals(ItemGrid x, ItemGrid y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                return x.Task == y.Task &&
                       x.Importance == y.Importance &&
                       x.Urgency == y.Urgency &&
                       x.CreatedDate == y.CreatedDate &&
                       x.AssignedRole == y.AssignedRole;
            }

            public int GetHashCode(ItemGrid obj)
            {
                if (obj is null) return 0;
                int hashTask = obj.Task?.GetHashCode() ?? 0;
                int hashImportance = obj.Importance?.GetHashCode() ?? 0;
                int hashUrgency = obj.Urgency?.GetHashCode() ?? 0;
                int hashCreated = obj.CreatedDate.GetHashCode();
                int hashAssignedRole = obj.AssignedRole?.GetHashCode() ?? 0;
                return hashTask ^ hashImportance ^ hashUrgency ^ hashCreated ^ hashAssignedRole;
            }
        }

        private void SaveTasksToCsv()
        {
            string tasksCsvPath = Path.Combine(currentPath, "data", "tasks.csv");
            List<ItemGrid> allTasksFromCsv = HelperClass.ReadCsv(tasksCsvPath);
            if (allTasksFromCsv == null) allTasksFromCsv = new List<ItemGrid>();

            var tasksFromUi = new List<ItemGrid>();
            if (task1.ItemsSource is IEnumerable<ItemGrid> t1) tasksFromUi.AddRange(t1);
            if (task2.ItemsSource is IEnumerable<ItemGrid> t2) tasksFromUi.AddRange(t2);
            if (task3.ItemsSource is IEnumerable<ItemGrid> t3) tasksFromUi.AddRange(t3);
            if (task4.ItemsSource is IEnumerable<ItemGrid> t4) tasksFromUi.AddRange(t4);

            var tasksToPersist = new List<ItemGrid>();

            // Add tasks from CSV that are NOT for the current role (or Unassigned if Manager)
            // These are tasks managed by other roles, or tasks that were Unassigned but current role is not Manager
            tasksToPersist.AddRange(allTasksFromCsv.Where(csvTask =>
            {
                bool isManagerHandlingUnassigned = SelectedRole == "Manager" && csvTask.AssignedRole == "Unassigned";
                bool isOwnRoleTask = csvTask.AssignedRole == SelectedRole;
                // Keep the task if it's NOT a task this role instance should be managing from the UI grids
                return !(isOwnRoleTask || isManagerHandlingUnassigned);
            }));

            // Add all tasks currently in the UI (these are the ones this role instance IS managing)
            tasksToPersist.AddRange(tasksFromUi);

            // Write the combined list, ensuring no duplicates based on ItemGridComparer
            HelperClass.WriteCsv(tasksToPersist.Distinct(new ItemGridComparer()), tasksCsvPath);
        }

        public async void loadDataGridView()
        {
            // string[] csvFiles = { "1.csv", "2.csv", "3.csv", "4.csv" }; // REMOVED
            DataGrid[] dataGrids = { task1, task2, task3, task4 }; // Still useful for clearing/sorting

            // MODIFIED: Read from single tasks.csv
            string tasksCsvPath = Path.Combine(currentPath, "data", "tasks.csv");
            List<ItemGrid> allTasks = HelperClass.ReadCsv(tasksCsvPath);
            if (allTasks == null) allTasks = new List<ItemGrid>();

            bool llmUpdatedAnyTask = false; // RENAMED from 'updated'
            // LLM loop for Importance/Urgency on allTasks (structure adopted from old loop)
            foreach (var item in allTasks)
            {
                if (string.IsNullOrWhiteSpace(item.Importance) || item.Importance == "Unknown" ||
                    string.IsNullOrWhiteSpace(item.Urgency) || item.Urgency == "Unknown")
                {
                    try
                    {
                        Console.WriteLine($"Getting priority for task: {item.Task}");
                        var (importance, urgency) = await _llmService.GetTaskPriorityAsync(item.Task);
                        item.Importance = importance;
                        item.Urgency = urgency;
                        item.LastModifiedDate = DateTime.Now;
                        llmUpdatedAnyTask = true;
                        Console.WriteLine($"Updated Task: {item.Task}, Importance: {item.Importance}, Urgency: {item.Urgency}, LastModified: {item.LastModifiedDate}");
                        await Task.Delay(500); // Adhere to rate limits
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error getting priority for task '{item.Task}': {ex.Message}");
                    }
                }
            }

            if (llmUpdatedAnyTask)
            {
                HelperClass.WriteCsv(allTasks, tasksCsvPath); // Save if LLM made changes
            }

            // LLM Reminder loop (operates on allTasks, doesn't modify them for saving here)
            // PRESERVING ORIGINAL REMINDER LOGIC BLOCK AS REQUESTED
            foreach (var item in allTasks)
            {
                if (item.IsActive && (DateTime.Now - item.LastModifiedDate) > StaleTaskThreshold)
                {
                    try
                    {
                        TimeSpan taskAge = DateTime.Now - item.LastModifiedDate;
                        Console.WriteLine($"Task '{item.Task}' is stale (age: {taskAge.Days} days). Generating reminder...");
                        var (reminder, suggestions) = await _llmService.GenerateTaskReminderAsync(item.Task, taskAge);

                        if (!string.IsNullOrWhiteSpace(reminder))
                        {
                            Console.WriteLine($"Reminder for task '{item.Task}': {reminder}");
                        }
                        if (suggestions != null && suggestions.Any())
                        {
                            Console.WriteLine($"Suggestions for task '{item.Task}':");
                            foreach (var suggestion in suggestions)
                            {
                                Console.WriteLine($"- {suggestion}");
                            }
                        }
                        await Task.Delay(500); // Adhere to rate limits for reminder generation
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error generating reminder for task '{item.Task}': {ex.Message}");
                    }
                }
            }

            // Filter for display
            List<ItemGrid> roleSpecificTasks;
            if (SelectedRole == "Manager")
            {
                roleSpecificTasks = allTasks.Where(t => t.AssignedRole == SelectedRole || t.AssignedRole == "Unassigned").ToList();
            }
            else
            {
                roleSpecificTasks = allTasks.Where(t => t.AssignedRole == SelectedRole).ToList();
            }

            // Clear and populate DataGrids
            // DataGrid[] dataGrids = { task1, task2, task3, task4 }; // Already defined above
            foreach (var dg in dataGrids) { dg.ItemsSource = null; }

            task1.ItemsSource = roleSpecificTasks.Where(t => (t.Importance == "High" || t.Importance == "高") && (t.Urgency == "High" || t.Urgency == "高")).ToList();
            task2.ItemsSource = roleSpecificTasks.Where(t => (t.Importance == "High" || t.Importance == "高") && (t.Urgency != "High" && t.Urgency != "高")).ToList();
            task3.ItemsSource = roleSpecificTasks.Where(t => (t.Importance != "High" && t.Importance != "高") && (t.Urgency == "High" || t.Urgency == "高")).ToList();
            task4.ItemsSource = roleSpecificTasks.Where(t => (t.Importance != "High" && t.Importance != "高") && (t.Urgency != "High" && t.Urgency != "高")).ToList();

            // Apply sorting
            foreach (var dg in dataGrids)
            {
                dg.Items.SortDescriptions.Clear();
                dg.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                if (dg.ItemsSource != null) // Only refresh if there's data
                {
                    dg.Items.Refresh();
                }
            }
        }

        private async void AddNewTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var addTaskWin = new AddTaskWindow(_llmService, UserRoles);
            if (addTaskWin.ShowDialog() == true && addTaskWin.NewTask != null)
            {
                string tasksCsvPath = Path.Combine(currentPath, "data", "tasks.csv");
                List<ItemGrid> allTasks = HelperClass.ReadCsv(tasksCsvPath);
                if (allTasks == null)
                {
                    allTasks = new List<ItemGrid>();
                }
                allTasks.Add(addTaskWin.NewTask);
                HelperClass.WriteCsv(allTasks, tasksCsvPath);

                loadDataGridView(); // Changed from await loadDataGridView()
            }
        }

        private async void RefreshTasksButton_Click(object sender, RoutedEventArgs e)
        {
            // Since loadDataGridView is async void, we just call it.
            // The 'async' on this handler allows for other awaitable operations if needed in the future.
            loadDataGridView();
            Console.WriteLine("Task view refresh initiated."); // Log or status bar message
        }

        public MainWindow()
        {
            InitializeComponent();
            BackupOldCsvFilesAndInitializeNewOne(); // Call the new backup and initialization method
            SelectedRole = UserRoles[0]; // Initialize SelectedRole
            _llmService = LlmService.Create(); // Instantiate LlmService
            this.Top = (double)Properties.Settings.Default.Top;
            this.Left = (double)Properties.Settings.Default.Left;

            // Populate ComboBox
            RoleSelectorComboBox.ItemsSource = UserRoles;
            RoleSelectorComboBox.SelectedItem = SelectedRole;

            loadDataGridView();
        }

        private void BackupOldCsvFilesAndInitializeNewOne()
        {
            string dataPath = Path.Combine(currentPath, "data");
            if (!Directory.Exists(dataPath))
            {
                Directory.CreateDirectory(dataPath);
            }

            string[] oldCsvFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            foreach (var oldFile in oldCsvFiles)
            {
                string oldFilePath = Path.Combine(dataPath, oldFile);
                if (File.Exists(oldFilePath))
                {
                    string backupFilePath = Path.Combine(dataPath, oldFile + ".backup");
                    try
                    {
                        File.Move(oldFilePath, backupFilePath); // Using File.Move for rename
                        Console.WriteLine($"Backed up {oldFilePath} to {backupFilePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error backing up {oldFilePath}: {ex.Message}");
                        // Decide if we should proceed or stop if a backup fails.
                        // For now, just logging and continuing.
                    }
                }
            }

            string tasksCsvPath = Path.Combine(dataPath, "tasks.csv");
            if (!File.Exists(tasksCsvPath))
            {
                try
                {
                    string header = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate,assignedRole";
                    File.WriteAllText(tasksCsvPath, header + Environment.NewLine);
                    Console.WriteLine($"Created new tasks file: {tasksCsvPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating {tasksCsvPath}: {ex.Message}");
                }
            }
        }

        private void RoleSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RoleSelectorComboBox.SelectedItem is string selectedRole)
            {
                SelectedRole = selectedRole;
                loadDataGridView();
            }
        }

        // update_csv REMOVED

        private void task1_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // task1_selected_indexs = task1.SelectedIndex; // REMOVED
            // update_csv(task1, "1"); // REMOVED
            SaveTasksToCsv();
        }

        private void task2_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // task2_selected_indexs = task2.SelectedIndex; // REMOVED
            // update_csv(task2, "2"); // REMOVED
            SaveTasksToCsv();
        }

        private void task3_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // task3_selected_indexs = task3.SelectedIndex; // REMOVED
            // update_csv(task3, "3"); // REMOVED
            SaveTasksToCsv();
        }

        private void task4_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // task4_selected_indexs = task4.SelectedIndex; // REMOVED
            // update_csv(task4, "4"); // REMOVED
            SaveTasksToCsv();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
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

            bool removed = false;
            if (task1.ItemsSource is List<ItemGrid> tasks1List && tasks1List.Remove(taskToDelete))
            {
                task1.ItemsSource = null; task1.ItemsSource = new List<ItemGrid>(tasks1List); removed = true;
            }
            else if (task2.ItemsSource is List<ItemGrid> tasks2List && tasks2List.Remove(taskToDelete))
            {
                task2.ItemsSource = null; task2.ItemsSource = new List<ItemGrid>(tasks2List); removed = true;
            }
            else if (task3.ItemsSource is List<ItemGrid> tasks3List && tasks3List.Remove(taskToDelete))
            {
                task3.ItemsSource = null; task3.ItemsSource = new List<ItemGrid>(tasks3List); removed = true;
            }
            else if (task4.ItemsSource is List<ItemGrid> tasks4List && tasks4List.Remove(taskToDelete))
            {
                task4.ItemsSource = null; task4.ItemsSource = new List<ItemGrid>(tasks4List); removed = true;
            }

            if (removed)
            {
                // Re-apply sort descriptions after ItemsSource is reset, as this might clear them.
                // Or, ensure loadDataGridView is called if appropriate, though here we only want to save.
                DataGrid[] dataGrids = { task1, task2, task3, task4 };
                foreach (var dg in dataGrids)
                {
                    if (dg.ItemsSource is List<ItemGrid> currentList && currentList != null)
                    {
                         // Ensure sort is re-applied; resetting ItemsSource might clear it.
                        var view = CollectionViewSource.GetDefaultView(dg.ItemsSource);
                        if (view != null && view.SortDescriptions.Count == 0) {
                             view.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                        } else if (view == null && !dg.Items.SortDescriptions.Any()) {
                            // Fallback if default view is null and no sort descriptions exist
                            dg.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                        }
                        // dg.Items.Refresh(); // This might also be needed
                    }
                }
                SaveTasksToCsv();
            }
        }
        // Removed del1_Click, del2_Click, del3_Click, del4_Click
    }
} // Closing brace for namespace TimeTask
