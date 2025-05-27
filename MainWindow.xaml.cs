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
using System.Threading.Tasks; // Added for Task.Delay and async operations

namespace TimeTask
{
    public partial class MainWindow : Window
    {
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

        int task1_selected_indexs = -1;
        int task2_selected_indexs = -1;
        int task3_selected_indexs = -1;
        int task4_selected_indexs = -1;

        public async void loadDataGridView()
        {
            string[] csvFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            DataGrid[] dataGrids = { task1, task2, task3, task4 };

            for (int i = 0; i < csvFiles.Length; i++)
            {
                string filePath = Path.Combine(currentPath, "data", csvFiles[i]);
                List<ItemGrid> items = HelperClass.ReadCsv(filePath);

                if (items == null)
                {
                    Console.WriteLine($"Error reading CSV file: {filePath}. Or file is empty/new.");
                    items = new List<ItemGrid>(); // Ensure items is not null for further processing
                    // Optionally create a dummy item if the file was expected but missing, e.g.
                    // items.Add(new ItemGrid { Task = "Default task if CSV missing", Score = 0, Result = "", IsActive = true, Importance = "Unknown", Urgency = "Unknown" });
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
                            item.Importance = importance;
                            item.Urgency = urgency;
                            item.LastModifiedDate = DateTime.Now; // Update LastModifiedDate
                            updated = true;
                            Console.WriteLine($"Updated Task: {item.Task}, Importance: {item.Importance}, Urgency: {item.Urgency}, LastModified: {item.LastModifiedDate}");
                            await Task.Delay(500); // Adhere to rate limits
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting priority for task '{item.Task}': {ex.Message}");
                            // Keep item.Importance/Urgency as "Unknown" or their current values
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
                
                dataGrids[i].ItemsSource = null; // Clear previous items or use a more sophisticated update
                dataGrids[i].ItemsSource = items;
                if (!dataGrids[i].Items.SortDescriptions.Contains(new SortDescription("Score", ListSortDirection.Descending)))
                {
                    dataGrids[i].Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                }

                // After potential updates and before saving, check for stale tasks to generate reminders
                foreach (var item in items)
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
                // Re-save CSV if priorities were updated (reminder generation does not modify the task itself yet)
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
        }

        public MainWindow()
        {
            InitializeComponent();
            _llmService = LlmService.Create(); // Instantiate LlmService
            this.Top = (double)Properties.Settings.Default.Top;
            this.Left = (double)Properties.Settings.Default.Left;
            loadDataGridView();
        }

        private void update_csv(DataGrid dgv, string number) {
            var temp = new List<ItemGrid>();
            for (int i = 0; i < dgv.Items.Count; i++)
            {
                if (dgv.Items[i] is ItemGrid)
                    temp.Add((ItemGrid)dgv.Items[i]);
            }
            HelperClass.WriteCsv(temp, currentPath + "/data/" + number + ".csv");
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

        private void AddNewTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var addTaskWin = new AddTaskWindow(_llmService); // Pass LlmService instance
            bool? dialogResult = addTaskWin.ShowDialog();

            if (dialogResult == true && addTaskWin.NewTask != null && addTaskWin.IsTaskAdded)
            {
                ItemGrid newTask = addTaskWin.NewTask;
                int listIndex = addTaskWin.SelectedListIndex; // 0-indexed

                string filePath = Path.Combine(currentPath, "data", (listIndex + 1) + ".csv");
                List<ItemGrid> tasks = HelperClass.ReadCsv(filePath) ?? new List<ItemGrid>();
                
                tasks.Add(newTask);
                HelperClass.WriteCsv(tasks, filePath);

                DataGrid targetGrid = null;
                switch (listIndex)
                {
                    case 0: targetGrid = task1; break;
                    case 1: targetGrid = task2; break;
                    case 2: targetGrid = task3; break;
                    case 3: targetGrid = task4; break;
                }

                if (targetGrid != null)
                {
                    targetGrid.ItemsSource = null;
                    targetGrid.ItemsSource = tasks;
                    if (!targetGrid.Items.SortDescriptions.Contains(new SortDescription("Score", ListSortDirection.Descending)))
                    {
                        targetGrid.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                    }
                }
                
                MessageBox.Show($"Task '{newTask.Task}' added successfully to List {listIndex + 1}.", "Task Added", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            // No need to call loadDataGridView() anymore, as we're updating the specific list.
        }

        private void del1_Click(object sender, RoutedEventArgs e)
        {
            if (task1_selected_indexs >= 0)
            {
                var itemList = (List<ItemGrid>)task1.ItemsSource;
                itemList.RemoveAt(task1_selected_indexs);
                task1.ItemsSource = itemList;
            }
        }

        private void del2_Click(object sender, RoutedEventArgs e)
        {
            DataRowView selectedItem = task2.SelectedItem as DataRowView;
            if (selectedItem != null)
            {
                DataView dataView = task2.ItemsSource as DataView;
                dataView.Table.Rows.Remove(selectedItem.Row);
            }
        }

        private void del3_Click(object sender, RoutedEventArgs e)
        {
            DataRowView selectedItem = task3.SelectedItem as DataRowView;
            if (selectedItem != null)
            {
                DataView dataView = task3.ItemsSource as DataView;
                dataView.Table.Rows.Remove(selectedItem.Row);
            }
        }

        private void del4_Click(object sender, RoutedEventArgs e)
        {
            DataRowView selectedItem = task4.SelectedItem as DataRowView;
            if (selectedItem != null)
            {
                DataView dataView = task4.ItemsSource as DataView;
                dataView.Table.Rows.Remove(selectedItem.Row);
            }
        }
    }
}
