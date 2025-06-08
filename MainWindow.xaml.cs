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
using System.Globalization; // Required for CultureInfo
using System.Windows.Data; // Required for IMultiValueConverter
using System.Threading; // For Timer (though DispatcherTimer is in System.Windows.Threading)
using System.Configuration; // Required for SettingsPropertyNotFoundException

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
                    ReminderTime = temparry.Length > 8 && DateTime.TryParse(temparry[8], out DateTime rt) ? rt : (DateTime?)null
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
                $"{item.Task},{item.Score},{item.Result},{(item.IsActive ? "False" : "True")},{item.Importance ?? "Unknown"},{item.Urgency ?? "Unknown"},{item.CreatedDate:o},{item.LastModifiedDate:o},{item.ReminderTime?.ToString("o") ?? ""}"
            ).ToArray();
            var contents = new string[temparray.Length + 2];
            Array.Copy(temparray, 0, contents, 1, temparray.Length);
            contents[0] = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate,reminderTime";
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
        public DateTime? ReminderTime { get; set; } = null;
        public string TaskType { get; set; }
        public DateTime? CompletionTime { get; set; }
        public string CompletionStatus { get; set; }
        public string AssignedRole { get; set; }
        public string SourceTaskID { get; set; } // To map to SourceTaskID from the database

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
        private System.Windows.Threading.DispatcherTimer _reminderTimer;

        private DatabaseService _databaseService;
        private System.Windows.Threading.DispatcherTimer _syncTimer;
        private HashSet<string> _syncedTaskSourceIDs = new HashSet<string>();
        private bool _isFirstSyncAttempted = false; // To control initial sync message

        private ItemGrid _draggedItem;
        private DataGrid _sourceDataGrid;
        private Point? _dragStartPoint; // To store the starting point of a potential drag

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
                else
                {
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrWhiteSpace(item.SourceTaskID) && !_syncedTaskSourceIDs.Contains(item.SourceTaskID))
                        {
                            _syncedTaskSourceIDs.Add(item.SourceTaskID);
                        }
                    }
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

            // Attach CellEditEnding event handler to all DataGrids
            task1.CellEditEnding += DataGrid_CellEditEnding;
            task2.CellEditEnding += DataGrid_CellEditEnding;
            task3.CellEditEnding += DataGrid_CellEditEnding;
            task4.CellEditEnding += DataGrid_CellEditEnding;

            // Initialize and start the reminder timer
            _reminderTimer = new System.Windows.Threading.DispatcherTimer();
            _reminderTimer.Interval = TimeSpan.FromSeconds(30); // Check every 30 seconds
            _reminderTimer.Tick += ReminderTimer_Tick;
            _reminderTimer.Start();

            InitializeSyncService();
        }

        private void InitializeSyncService()
        {
            try
            {
                string connectionString = null;
                try
                {
                    connectionString = (string)Properties.Settings.Default["TeamTasksDbConnectionString"];
                }
                catch (System.Configuration.SettingsPropertyNotFoundException ex)
                {
                    Console.WriteLine($"INFO: Settings property 'TeamTasksDbConnectionString' not found during initial load. This is expected if settings haven't been saved yet. Defaulting to empty. Error: {ex.Message}");
                    connectionString = string.Empty;
                }
                catch (Exception ex) // Catch other potential issues during access
                {
                    Console.WriteLine($"ERROR: Error accessing setting 'TeamTasksDbConnectionString'. Defaulting to empty. Error: {ex.Message}");
                    connectionString = string.Empty;
                }

                bool enableSync = false;
                try
                {
                    enableSync = (bool)Properties.Settings.Default["EnableTeamSync"];
                }
                catch (System.Configuration.SettingsPropertyNotFoundException ex)
                {
                    Console.WriteLine($"INFO: Settings property 'EnableTeamSync' not found. Defaulting to false. Error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Error accessing setting 'EnableTeamSync'. Defaulting to false. Error: {ex.Message}");
                }

                if (enableSync)
                {
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        _databaseService = new DatabaseService(connectionString);
                        Console.WriteLine("DatabaseService initialized with connection string.");

                        if (_syncTimer == null)
                        {
                            _syncTimer = new System.Windows.Threading.DispatcherTimer();
                            _syncTimer.Tick += SyncTimer_Tick;
                        }

                        int syncInterval = 30; // Default interval
                        try
                        {
                            syncInterval = (int)Properties.Settings.Default["SyncIntervalMinutes"];
                            if (syncInterval <= 0) syncInterval = 30; // Ensure positive interval
                        }
                        catch (System.Configuration.SettingsPropertyNotFoundException ex)
                        {
                            Console.WriteLine($"INFO: Settings property 'SyncIntervalMinutes' not found. Defaulting to 30. Error: {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"ERROR: Error accessing setting 'SyncIntervalMinutes'. Defaulting to 30. Error: {ex.Message}");
                        }
                        _syncTimer.Interval = TimeSpan.FromMinutes(syncInterval);
                        _syncTimer.Start();
                        Console.WriteLine($"Sync timer started. Interval: {syncInterval} minutes.");

                        // Perform an initial sync immediately if enabled
                        Task.Run(() => SyncTimer_Tick(this, EventArgs.Empty));
                    }
                    else
                    {
                        Console.WriteLine("Team Sync is enabled but connection string is missing. Sync service not started.");
                        if (!_isFirstSyncAttempted)
                        {
                             // MessageBox.Show(this, "Team Task Synchronization is enabled, but the database connection string is not configured. Please configure it in Settings.", "Sync Configuration Needed", MessageBoxButton.OK, MessageBoxImage.Warning);
                            _isFirstSyncAttempted = true; // Show message only once per app load initially
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Team Sync is disabled. Sync service not started.");
                    if (_syncTimer != null)
                    {
                        _syncTimer.Stop();
                        Console.WriteLine("Sync timer stopped.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing sync service: {ex.Message}");
                MessageBox.Show(this, $"Error initializing synchronization service: {ex.Message}", "Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SyncTimer_Tick(object sender, EventArgs e)
        {
            bool enableSync = false;
            try
            {
                enableSync = (bool)Properties.Settings.Default["EnableTeamSync"];
            }
            catch (Exception ex) // Simplified catch for brevity in this specific check
            {
                Console.WriteLine($"ERROR: Error accessing 'EnableTeamSync' in SyncTimer_Tick. Defaulting to false. Error: {ex.Message}");
            }

            if (!enableSync || _databaseService == null)
            {
                if (_syncTimer != null) _syncTimer.Stop(); // Stop if disabled or service not available
                Console.WriteLine("SyncTimer_Tick: Sync disabled or DatabaseService not available. Timer stopped.");
                return;
            }

            Console.WriteLine("SyncTimer_Tick: Attempting to sync team tasks...");
            try
            {
                string userRole = string.Empty;
                try
                {
                    userRole = (string)Properties.Settings.Default["TeamRole"];
                }
                catch (Exception ex) // Simplified catch
                {
                     Console.WriteLine($"ERROR: Error accessing 'TeamRole' in SyncTimer_Tick. Defaulting to empty. Error: {ex.Message}");
                }
                List<ItemGrid> newTasks = await Task.Run(() => _databaseService.GetTeamTasks(userRole)); // Run DB call on background thread

                if (newTasks == null)
                {
                    Console.WriteLine("SyncTimer_Tick: GetTeamTasks returned null. Skipping update.");
                    if (!_isFirstSyncAttempted)
                    {
                        // MessageBox.Show(this, "Could not connect to the team task database. Please check settings and network.", "Sync Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _isFirstSyncAttempted = true;
                    }
                    return;
                }
                 _isFirstSyncAttempted = true; // Mark that an attempt was made

                int addedCount = 0;
                var task1List = task1.ItemsSource as List<ItemGrid>;
                if (task1List == null)
                {
                    // This needs to be on UI thread if we assign it back
                    await Dispatcher.InvokeAsync(() => {
                        task1List = new List<ItemGrid>();
                        task1.ItemsSource = task1List;
                    });
                }

                foreach (var dbTask in newTasks)
                {
                    if (!string.IsNullOrWhiteSpace(dbTask.SourceTaskID) && _syncedTaskSourceIDs.Contains(dbTask.SourceTaskID))
                    {
                        continue; // Skip already synced task
                    }

                    // Ensure properties are set for the quadrant
                    dbTask.Importance = "High"; // Default for "Important & Urgent"
                    dbTask.Urgency = "High";   // Default for "Important & Urgent"
                    // Score will be set on refresh/add

                    // Operations on task1List must be on UI thread
                    await Dispatcher.InvokeAsync(() =>
                    {
                        // Double check task1List is not null if it was created within Dispatcher.InvokeAsync
                        if (task1.ItemsSource is List<ItemGrid> currentList) {
                           currentList.Add(dbTask);
                            if (!string.IsNullOrWhiteSpace(dbTask.SourceTaskID))
                            {
                                _syncedTaskSourceIDs.Add(dbTask.SourceTaskID);
                            }
                            addedCount++;
                        } else {
                             // Fallback or error if task1List is unexpectedly null
                            task1List = new List<ItemGrid>();
                            task1.ItemsSource = task1List;
                            task1List.Add(dbTask);
                            if (!string.IsNullOrWhiteSpace(dbTask.SourceTaskID))
                            {
                                _syncedTaskSourceIDs.Add(dbTask.SourceTaskID);
                            }
                            addedCount++;
                            Console.WriteLine("SyncTimer_Tick: task1.ItemsSource was null and re-initialized on UI thread.");
                        }
                    });
                }

                if (addedCount > 0)
                {
                    Console.WriteLine($"SyncTimer_Tick: Added {addedCount} new tasks to 'Important & Urgent'.");
                    await Dispatcher.InvokeAsync(() =>
                    {
                        RefreshDataGrid(task1);
                        update_csv(task1, "1"); // Persist to CSV
                    });
                }
                else
                {
                    Console.WriteLine("SyncTimer_Tick: No new tasks to add.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during scheduled sync: {ex.Message}");
                // Avoid showing MessageBox repeatedly from timer tick. Log is better.
            }
        }

        private void ReminderTimer_Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            // bool changesMadeOverall = false; // To track if any CSV needs update across all grids

            DataGrid[] dataGrids = { task1, task2, task3, task4 };
            string[] csvFiles = { "1.csv", "2.csv", "3.csv", "4.csv" }; // To identify which CSV to update

            for (int i = 0; i < dataGrids.Length; i++)
            {
                DataGrid currentGrid = dataGrids[i];
                bool changesMadeInCurrentGrid = false; // Track changes for the current grid/CSV

                if (currentGrid.ItemsSource is List<ItemGrid> tasks)
                {
                    // Iterating on a copy for safe modification is complex with shared ItemGrid objects.
                    // Direct modification and then saving is simpler if UI updates are handled carefully.
                    foreach (ItemGrid task in tasks)
                    {
                        if (task.IsActive && task.ReminderTime.HasValue && task.ReminderTime.Value <= now)
                        {
                            // Display reminder
                            // Ensure MessageBox is shown on the UI thread if timer runs on a different thread.
                            // DispatcherTimer's Tick event runs on the Dispatcher's thread, so direct UI access is safe.
                            MessageBox.Show(this, $"Reminder: {task.Task}", "Task Reminder", MessageBoxButton.OK, MessageBoxImage.Information);

                            // Mark reminder as shown by clearing it
                            task.ReminderTime = null;
                            task.LastModifiedDate = now; // Update last modified date
                            changesMadeInCurrentGrid = true;
                            // changesMadeOverall = true;
                        }
                    }

                    if (changesMadeInCurrentGrid)
                    {
                        // update_csv uses the ItemsSource of the DataGrid, which is the 'tasks' list.
                        // So, modifications to 'task' objects within 'tasks' list are directly saved.
                        update_csv(currentGrid, csvFiles[i].Replace(".csv", ""));

                        // Optional: Refresh the specific DataGrid if clearing ReminderTime should reflect visually.
                        // This is important if there's a column bound to ReminderTime or its existence.
                        RefreshDataGrid(currentGrid);
                    }
                }
            }
            // No overall 'changesMadeOverall' check needed here for saving, as each grid is saved individually if changed.
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // Check if the edited column is the "Task" column.
                // Using HeaderText assumes the XAML <DataGridTextColumn Header="Task" ... />
                // A more robust way might involve checking the binding path if available,
                // but HeaderText is usually reliable for display columns.
                var column = e.Column as DataGridBoundColumn;
                if (column != null && column.Header != null && column.Header.ToString() == "Task")
                {
                    var item = e.Row.Item as ItemGrid;
                    if (item != null && e.EditingElement is TextBox textBox)
                    {
                        string newDescription = textBox.Text;
                        string oldDescriptionPreview = item.Task != null && item.Task.Length > 15 ? item.Task.Substring(0, 15) + "..." : item.Task;

                        // Note: At this point, item.Task is still the *old* value before the commit.
                        // The newDescription is what will be committed.
                        Console.WriteLine($"Task edited in grid. Task (Old Preview): [{oldDescriptionPreview}], New Description: [{newDescription}]");

                        // If you need to trigger something *after* the value has been committed to the item,
                        // you might need a different approach or event, or handle it carefully knowing item.Task isn't updated yet.
                        // For logging the edit *intent* with the new value, this is fine.
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
            // Check if the click originated from the delete button first
            if (e.OriginalSource is Button button && button.Name == "PART_DeleteButton")
            {
                // If it's the delete button, let it handle its click, don't interfere.
                // And don't treat it as a drag initiation.
                _dragStartPoint = null; // Ensure no drag starts
                _draggedItem = null;
                _sourceDataGrid = null;
                return;
            }

            // Also, ensure that we are dealing with the primary mouse button for drag
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DataGridRow row = sender as DataGridRow; // The sender is the DataGridRow
                if (row == null || !row.IsSelected)
                {
                    // If row is null, or you want to be more specific,
                    // you can try to find the parent row from e.OriginalSource as before.
                    // row = FindParent<DataGridRow>((DependencyObject)e.OriginalSource);
                }


                if (row != null && row.Item is ItemGrid item)
                {
                    _dragStartPoint = e.GetPosition(null); // Get position relative to the screen or a top-level window
                    _draggedItem = item;
                    _sourceDataGrid = FindParent<DataGrid>(row);
                    // Do NOT call DragDrop.DoDragDrop here.
                    // Do NOT set e.Handled = true here, to allow the DataGrid to process the click for focus/selection.
                }
                else
                {
                    // Click was not on a valid item or row, clear drag state
                    _dragStartPoint = null;
                    _draggedItem = null;
                    _sourceDataGrid = null;
                }
            }
            else // Not a left-button press, clear drag state
            {
                 _dragStartPoint = null;
                 _draggedItem = null;
                 _sourceDataGrid = null;
            }
        }

        private void DataGridRow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStartPoint.HasValue && e.LeftButton == MouseButtonState.Pressed && _draggedItem != null && _sourceDataGrid != null)
            {
                Point currentPosition = e.GetPosition(null);
                Vector diff = _dragStartPoint.Value - currentPosition;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // Start the drag operation
                    // The 'sender' here is the DataGridRow. We pass it as the drag source.
                    DataGridRow row = sender as DataGridRow;
                    if (row != null)
                    {
                        DragDrop.DoDragDrop(row, _draggedItem, DragDropEffects.Move);
                    }

                    // Reset stored drag data after DragDrop completes
                    _dragStartPoint = null;
                    _draggedItem = null;
                    _sourceDataGrid = null;
                    // e.Handled can optionally be set to true here if needed,
                    // but DoDragDrop usually takes over input sufficiently.
                }
            }
        }

        private void DataGridRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // If the mouse button is released and a drag hasn't started, clear drag state.
            if (_dragStartPoint.HasValue)
            {
                _dragStartPoint = null;
                _draggedItem = null;
                _sourceDataGrid = null;
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

        private void AddQuickTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton && clickedButton.Tag is string tagString)
            {
                if (int.TryParse(tagString, out int quadrantIndex))
                {
                    // Ensure _llmService is available (it's initialized in MainWindow's constructor)
                    if (_llmService == null)
                    {
                        MessageBox.Show("LLM Service is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // This constructor signature will be updated in the next subtask.
                    // For now, this code assumes AddTaskWindow can take quadrantIndex.
                    AddTaskWindow addTaskWindow = new AddTaskWindow(_llmService, quadrantIndex);
                    bool? dialogResult = addTaskWindow.ShowDialog();

                    if (dialogResult == true && addTaskWindow.IsTaskAdded && addTaskWindow.NewTask != null)
                    {
                        ItemGrid newTask = addTaskWindow.NewTask;

                        int finalQuadrantIndex = AddTaskWindow.GetIndexFromPriority(newTask.Importance, newTask.Urgency);
                        DataGrid targetDataGrid = null;

                        switch (finalQuadrantIndex)
                        {
                            case 0: targetDataGrid = task1; break;
                            case 1: targetDataGrid = task2; break;
                            case 2: targetDataGrid = task3; break;
                            case 3: targetDataGrid = task4; break;
                            default:
                                MessageBox.Show("Invalid quadrant specified for the new task after dialog.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                        }

                        string csvFileNumber = GetQuadrantNumber(targetDataGrid.Name);

                        List<ItemGrid> items = targetDataGrid.ItemsSource as List<ItemGrid>;
                        if (items == null)
                        {
                            items = new List<ItemGrid>();
                            targetDataGrid.ItemsSource = items;
                        }

                        items.Add(newTask);
                        // Re-score all items in the list for consistent ordering
                        for (int i = 0; i < items.Count; i++)
                        {
                            items[i].Score = items.Count - i;
                        }

                        RefreshDataGrid(targetDataGrid);
                        if (!string.IsNullOrEmpty(csvFileNumber))
                        {
                            update_csv(targetDataGrid, csvFileNumber);
                        }
                        else
                        {
                             MessageBox.Show($"Could not determine CSV file for quadrant: {targetDataGrid.Name}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            LlmSettingsWindow settingsWindow = new LlmSettingsWindow();
            settingsWindow.Owner = this; // Set the owner for proper modal behavior and centering

            // ShowDialog returns a nullable bool (bool?). True if OK/Save, False if Cancel, Null if just closed.
            bool? dialogResult = settingsWindow.ShowDialog();

            if (dialogResult == true) // This implies settings were saved and DialogResult was set to true in LlmSettingsWindow
            {
                // Check if _llmService is null, though it should be initialized in the constructor
                if (_llmService == null)
                {
                    _llmService = LlmService.Create(); // Or handle error appropriately
                    MessageBox.Show("LLM Service was not initialized. It has been initialized now. Please try your operation again.", "Service Re-initialized", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Assuming LlmService will have a public method to reload its config.
                    // This method needs to be added in the next step.
                    _llmService.ReloadConfigAndReinitialize();
                    MessageBox.Show("LLM settings have been updated and the service has been re-initialized.", "Settings Updated", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Optionally, you might want to refresh or reload data that depends on LLM service,
                // for example, if LLM is used to process tasks on load.
                // loadDataGridView(); // Consider if this is needed and its implications.
                // For now, just re-initializing the service.

                // Re-initialize sync service with potentially new settings
                if (_syncTimer != null) _syncTimer.Stop(); // Stop existing timer before re-init
                InitializeSyncService();
            }
        }

        private async void LongTermGoalButton_Click(object sender, RoutedEventArgs e)
        {
            SetLongTermGoalWindow goalDialog = new SetLongTermGoalWindow
            {
                Owner = this
            };

            if (goalDialog.ShowDialog() == true)
            {
                string userGoal = goalDialog.GoalDescription;
                string userDuration = goalDialog.Duration;

                if (_llmService == null)
                {
                    MessageBox.Show("LLM Service is not available. Cannot decompose goal.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Show some kind of loading indicator here if possible (optional for now)
                List<ProposedDailyTask> proposedTasks = null;
                try
                {
                    // This is an async call, so the method should be async void
                    proposedTasks = await _llmService.DecomposeGoalIntoDailyTasksAsync(userGoal, userDuration);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred while trying to decompose the goal: {ex.Message}", "LLM Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return; // Stop further processing
                }
                // Hide loading indicator here

                if (proposedTasks == null || !proposedTasks.Any())
                {
                    MessageBox.Show("The LLM could not break down this goal into daily tasks, or no tasks were returned. Please try a different goal or phrasing.", "No Tasks Generated", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ConfirmGoalTasksWindow confirmDialog = new ConfirmGoalTasksWindow(proposedTasks)
                {
                    Owner = this
                };

                if (confirmDialog.ShowDialog() == true && confirmDialog.SelectedTasks.Any())
                {
                    int tasksAddedCount = 0;
                    foreach (var taskToAdd in confirmDialog.SelectedTasks)
                    {
                        var newItem = new ItemGrid
                        {
                            Task = taskToAdd.TaskDescription + (!string.IsNullOrWhiteSpace(taskToAdd.EstimatedTime) ? $" ({taskToAdd.EstimatedTime})" : ""),
                            // Importance and Urgency need to be mapped from taskToAdd.Quadrant
                            // Score will be set on refresh/add
                            IsActive = true,
                            Result = string.Empty,
                            CreatedDate = DateTime.Now, // Consider if 'Day' from ProposedDailyTask should influence this
                            LastModifiedDate = DateTime.Now
                        };

                        // Map Quadrant string to Importance and Urgency
                        // And determine target DataGrid
                        DataGrid targetGrid = null;
                        string targetCsvNumber = null;

                        switch (taskToAdd.Quadrant?.ToLowerInvariant())
                        {
                            case "important & urgent":
                                newItem.Importance = "High"; newItem.Urgency = "High";
                                targetGrid = task1; targetCsvNumber = "1";
                                break;
                            case "important & not urgent":
                                newItem.Importance = "High"; newItem.Urgency = "Low";
                                targetGrid = task2; targetCsvNumber = "2";
                                break;
                            case "not important & urgent":
                                newItem.Importance = "Low"; newItem.Urgency = "High";
                                targetGrid = task3; targetCsvNumber = "3";
                                break;
                            case "not important & not urgent":
                                newItem.Importance = "Low"; newItem.Urgency = "Low";
                                targetGrid = task4; targetCsvNumber = "4";
                                break;
                            default:
                                Console.WriteLine($"Unknown quadrant: {taskToAdd.Quadrant}. Defaulting to Important & Urgent.");
                                newItem.Importance = "High"; newItem.Urgency = "High"; // Default
                                targetGrid = task1; targetCsvNumber = "1";
                                break;
                        }

                        if (targetGrid != null)
                        {
                            var items = targetGrid.ItemsSource as List<ItemGrid>;
                            if (items == null)
                            {
                                items = new List<ItemGrid>();
                                targetGrid.ItemsSource = items;
                            }
                            items.Add(newItem);
                            // Re-score items in this grid
                            for (int i = 0; i < items.Count; i++)
                            {
                                items[i].Score = items.Count - i;
                            }
                            RefreshDataGrid(targetGrid); // Refresh the specific grid
                            if (targetCsvNumber != null)
                            {
                                update_csv(targetGrid, targetCsvNumber);
                            }
                            tasksAddedCount++;
                        }
                    }
                    if (tasksAddedCount > 0)
                    {
                         MessageBox.Show($"{tasksAddedCount} new daily task(s) have been added to your plan.", "Tasks Added", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }
    }
}

// Add this class within the TimeTask namespace, but outside the MainWindow class.
// The namespace TimeTask is already declared above, so we are effectively adding to it.
// No need to redeclare "namespace TimeTask" if the class is meant to be in the same one.
// However, the original file has two namespace blocks. Let's keep that structure.
namespace TimeTask
{
    public class TaskWithReminderIndicatorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return string.Empty;

            string taskDescription = values[0] as string ?? string.Empty;
            DateTime? reminderTime = null;

            if (values[1] != null && values[1] != DependencyProperty.UnsetValue)
            {
                reminderTime = values[1] as DateTime?;
            }

            if (reminderTime.HasValue)
            {
                return $"{taskDescription} ⏰";
            }
            return taskDescription;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
