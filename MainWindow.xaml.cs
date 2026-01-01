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
using System.Collections.ObjectModel; // Required for ObservableCollection
using System.Text.Json; // For JSON serialization
using System.Text; // For StringBuilder and encoding

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
                    IsActive = !isCompleted, // IsActive is the opposite of is_completed
                    Importance = temparry.Length > 4 && !string.IsNullOrWhiteSpace(temparry[4]) ? temparry[4] : "Unknown",
                    Urgency = temparry.Length > 5 && !string.IsNullOrWhiteSpace(temparry[5]) ? temparry[5] : "Unknown",
                    CreatedDate = temparry.Length > 6 && DateTime.TryParse(temparry[6], out DateTime cd) ? cd : DateTime.Now,
                    LastModifiedDate = temparry.Length > 7 && DateTime.TryParse(temparry[7], out DateTime lmd) ? lmd : DateTime.Now,
                    ReminderTime = temparry.Length > 8 && DateTime.TryParse(temparry[8], out DateTime rt) ? rt : (DateTime?)null,
                    LongTermGoalId = temparry.Length > 9 && !string.IsNullOrWhiteSpace(temparry[9]) ? temparry[9] : null,
                    OriginalScheduledDay = temparry.Length > 10 && int.TryParse(temparry[10], out int osd) ? osd : 0,
                    IsActiveInQuadrant = temparry.Length > 11 && bool.TryParse(temparry[11], out bool iaiq) ? iaiq : true, // Default to true for backward compatibility
                    InactiveWarningCount = temparry.Length > 12 && int.TryParse(temparry[12], out int iwc) ? iwc : 0 // New field
                };
            var result_list = new List<ItemGrid>();
            try
            {
                result_list = result.ToList();
            }
            catch (Exception ex) { // Catch specific exceptions if possible, or log general ones
                Console.WriteLine($"Error parsing CSV lines: {ex.Message}");
                // Add a default item or handle error as appropriate
                result_list.Add(new ItemGrid { Task = "csv文件错误", Score = 0, Result= "", IsActive = true, Importance = "Unknown", Urgency = "Unknown", CreatedDate = DateTime.Now, LastModifiedDate = DateTime.Now, IsActiveInQuadrant = true, InactiveWarningCount = 0 });
            }
            return result_list;
        }

        public static void WriteCsv(IEnumerable<ItemGrid> items, string filepath)
        {
            var temparray = items.Select(item =>
                $"{item.Task},{item.Score},{item.Result},{(item.IsActive ? "False" : "True")},{item.Importance ?? "Unknown"},{item.Urgency ?? "Unknown"},{item.CreatedDate:o},{item.LastModifiedDate:o},{item.ReminderTime?.ToString("o") ?? ""},{item.LongTermGoalId ?? ""},{item.OriginalScheduledDay},{item.IsActiveInQuadrant},{item.InactiveWarningCount}" // Added InactiveWarningCount
            ).ToArray();
            var contents = new string[temparray.Length + 2];
            Array.Copy(temparray, 0, contents, 1, temparray.Length);
            // Updated header
            contents[0] = "task,score,result,is_completed,importance,urgency,createdDate,lastModifiedDate,reminderTime,longTermGoalId,originalScheduledDay,isActiveInQuadrant,inactiveWarningCount"; // Added inactiveWarningCount header
            File.WriteAllLines(filepath, contents);
        }

        public static List<LongTermGoal> ReadLongTermGoalsCsv(string filepath)
        {
            if (!File.Exists(filepath))
            {
                return new List<LongTermGoal>();
            }

            var allLines = File.ReadAllLines(filepath).Where(arg => !string.IsNullOrWhiteSpace(arg)).ToList();
            if (allLines.Count <= 1)
            {
                return new List<LongTermGoal>();
            }

            var goals = new List<LongTermGoal>();
            foreach (var line in allLines.Skip(1))
            {
                var fields = line.Split(',');
                if (fields.Length >= 5)
                {
                    try
                    {
                        var goal = new LongTermGoal
                        {
                            Id = fields[0],
                            Description = fields[1].Replace(";;;", ","),
                            TotalDuration = fields[2],
                            CreationDate = DateTime.TryParse(fields[3], out DateTime cd) ? cd : DateTime.MinValue,
                            IsActive = bool.TryParse(fields[4], out bool ia) && ia,
                            IsLearningPlan = fields.Length > 5 && bool.TryParse(fields[5], out bool ilp) && ilp,
                            Subject = fields.Length > 6 ? fields[6].Replace(";;;", ",") : null,
                            StartDate = fields.Length > 7 && DateTime.TryParse(fields[7], out DateTime sd) ? sd : (DateTime?)null,
                            TotalStages = fields.Length > 8 && int.TryParse(fields[8], out int ts) ? ts : 0,
                            CompletedStages = fields.Length > 9 && int.TryParse(fields[9], out int cs) ? cs : 0
                        };
                        goals.Add(goal);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing LongTermGoal line: {line}. Error: {ex.Message}");
                    }
                }
            }
            return goals;
        }

        public static void WriteLongTermGoalsCsv(List<LongTermGoal> goals, string filepath)
        {
            var directory = Path.GetDirectoryName(filepath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var lines = new List<string> { "Id,Description,TotalDuration,CreationDate,IsActive,IsLearningPlan,Subject,StartDate,TotalStages,CompletedStages" };
            foreach (var goal in goals)
            {
                string safeDescription = goal.Description?.Replace(",", ";;;") ?? "";
                string safeSubject = goal.Subject?.Replace(",", ";;;") ?? "";
                lines.Add($"{goal.Id},{safeDescription},{goal.TotalDuration},{goal.CreationDate:o},{goal.IsActive},{goal.IsLearningPlan},{safeSubject},{goal.StartDate?.ToString("o") ?? ""},{goal.TotalStages},{goal.CompletedStages}");
            }
            File.WriteAllLines(filepath, lines);
        }

        public static List<LearningPlan> ReadLearningPlansCsv(string filepath)
        {
            if (!File.Exists(filepath))
            {
                return new List<LearningPlan>();
            }

            var allLines = File.ReadAllLines(filepath).Where(arg => !string.IsNullOrWhiteSpace(arg)).ToList();
            if (allLines.Count <= 1)
            {
                return new List<LearningPlan>();
            }

            var plans = new List<LearningPlan>();
            foreach (var line in allLines.Skip(1))
            {
                var fields = line.Split(',');
                if (fields.Length >= 10)
                {
                    try
                    {
                        var plan = new LearningPlan
                        {
                            Id = fields[0],
                            Subject = fields[1].Replace(";;;", ","),
                            Goal = fields[2].Replace(";;;", ","),
                            Duration = fields[3],
                            CreationDate = DateTime.TryParse(fields[4], out DateTime cd) ? cd : DateTime.MinValue,
                            StartDate = DateTime.TryParse(fields[5], out DateTime sd) ? sd : (DateTime?)null,
                            EndDate = DateTime.TryParse(fields[6], out DateTime ed) ? ed : (DateTime?)null,
                            IsActive = bool.TryParse(fields[7], out bool ia) && ia,
                            TotalStages = int.TryParse(fields[8], out int ts) ? ts : 0,
                            CompletedStages = int.TryParse(fields[9], out int cs) ? cs : 0
                        };
                        plans.Add(plan);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing LearningPlan line: {line}. Error: {ex.Message}");
                    }
                }
            }
            return plans;
        }

        public static void WriteLearningPlansCsv(List<LearningPlan> plans, string filepath)
        {
            var directory = Path.GetDirectoryName(filepath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var lines = new List<string> { "Id,Subject,Goal,Duration,CreationDate,StartDate,EndDate,IsActive,TotalStages,CompletedStages" };
            foreach (var plan in plans)
            {
                string safeSubject = plan.Subject?.Replace(",", ";;;") ?? "";
                string safeGoal = plan.Goal?.Replace(",", ";;;") ?? "";
                lines.Add($"{plan.Id},{safeSubject},{safeGoal},{plan.Duration},{plan.CreationDate:o},{plan.StartDate?.ToString("o") ?? ""},{plan.EndDate?.ToString("o") ?? ""},{plan.IsActive},{plan.TotalStages},{plan.CompletedStages}");
            }
            File.WriteAllLines(filepath, lines);
        }

        public static List<LearningMilestone> ReadLearningMilestonesCsv(string filepath)
        {
            if (!File.Exists(filepath))
            {
                return new List<LearningMilestone>();
            }

            var allLines = File.ReadAllLines(filepath).Where(arg => !string.IsNullOrWhiteSpace(arg)).ToList();
            if (allLines.Count <= 1)
            {
                return new List<LearningMilestone>();
            }

            var milestones = new List<LearningMilestone>();
            foreach (var line in allLines.Skip(1))
            {
                var fields = line.Split(',');
                if (fields.Length >= 9)
                {
                    try
                    {
                        var milestone = new LearningMilestone
                        {
                            Id = fields[0],
                            LearningPlanId = fields[1],
                            StageName = fields[2].Replace(";;;", ","),
                            Description = fields[3].Replace(";;;", ","),
                            StageNumber = int.TryParse(fields[4], out int sn) ? sn : 0,
                            TargetDate = DateTime.TryParse(fields[5], out DateTime td) ? td : (DateTime?)null,
                            IsCompleted = bool.TryParse(fields[6], out bool ic) && ic,
                            CompletedDate = DateTime.TryParse(fields[7], out DateTime ccd) ? ccd : (DateTime?)null,
                            AssociatedTaskId = fields.Length > 8 ? fields[8] : null
                        };
                        milestones.Add(milestone);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing LearningMilestone line: {line}. Error: {ex.Message}");
                    }
                }
            }
            return milestones;
        }

        public static void WriteLearningMilestonesCsv(List<LearningMilestone> milestones, string filepath)
        {
            var directory = Path.GetDirectoryName(filepath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var lines = new List<string> { "Id,LearningPlanId,StageName,Description,StageNumber,TargetDate,IsCompleted,CompletedDate,AssociatedTaskId" };
            foreach (var milestone in milestones)
            {
                string safeStageName = milestone.StageName?.Replace(",", ";;;") ?? "";
                string safeDescription = milestone.Description?.Replace(",", ";;;") ?? "";
                lines.Add($"{milestone.Id},{milestone.LearningPlanId},{safeStageName},{safeDescription},{milestone.StageNumber},{milestone.TargetDate?.ToString("o") ?? ""},{milestone.IsCompleted},{milestone.CompletedDate?.ToString("o") ?? ""},{milestone.AssociatedTaskId ?? ""}");
            }
            File.WriteAllLines(filepath, lines);
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

        // New properties for Long-Term Goal association
        public string LongTermGoalId { get; set; } = null; // ID of the parent LongTermGoal
        public int OriginalScheduledDay { get; set; } = 0; // Day index for tasks from a long-term plan
        public bool IsActiveInQuadrant { get; set; } = true; // True if the task should be displayed in the main quadrants

        // New property for inactivity warning
        public int InactiveWarningCount { get; set; } = 0;

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
        // Configurable timeout settings with defaults
        private static TimeSpan StaleTaskThreshold => TimeSpan.FromDays(Properties.Settings.Default.StaleTaskThresholdDays);
        private static int MaxInactiveWarnings => Properties.Settings.Default.MaxInactiveWarnings;
        private static TimeSpan FirstWarningAfter => TimeSpan.FromDays(Properties.Settings.Default.FirstWarningAfterDays);
        private static TimeSpan SecondWarningAfter => TimeSpan.FromDays(Properties.Settings.Default.SecondWarningAfterDays);
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

        private LongTermGoal _activeLongTermGoal;

        private void LoadActiveLongTermGoalAndRefreshDisplay()
        {
            string longTermGoalsCsvPath = Path.Combine(currentPath, "data", "long_term_goals.csv");
            if (File.Exists(longTermGoalsCsvPath))
            {
                List<LongTermGoal> allLongTermGoals = HelperClass.ReadLongTermGoalsCsv(longTermGoalsCsvPath);
                _activeLongTermGoal = allLongTermGoals.FirstOrDefault(g => g.IsActive);
            }
            else
            {
                _activeLongTermGoal = null;
            }
            UpdateLongTermGoalBadge();
        }

        public void UpdateLongTermGoalBadge()
        {
            if (_activeLongTermGoal != null)
            {
                ActiveLongTermGoalDisplay.Visibility = Visibility.Visible;
                
                if (_activeLongTermGoal.IsLearningPlan)
                {
                    ActiveLongTermGoalName.Text = $"{TruncateText(_activeLongTermGoal.Subject, 15)}计划";
                    double progress = _activeLongTermGoal.ProgressPercentage;
                    LongTermGoalBadge.Text = $"{progress:F0}%";
                }
                else
                {
                    ActiveLongTermGoalName.Text = TruncateText(_activeLongTermGoal.Description, 20);

                    int pendingSubTasks = 0;
                    string[] csvFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
                    for (int i = 0; i < csvFiles.Length; i++)
                    {
                        string filePath = Path.Combine(currentPath, "data", csvFiles[i]);
                        if (File.Exists(filePath))
                        {
                            List<ItemGrid> items = HelperClass.ReadCsv(filePath);
                            if (items != null)
                            {
                                pendingSubTasks += items.Count(item => item.LongTermGoalId == _activeLongTermGoal.Id && item.IsActive);
                            }
                        }
                    }
                    LongTermGoalBadge.Text = pendingSubTasks.ToString();
                }
            }
            else
            {
                ActiveLongTermGoalDisplay.Visibility = Visibility.Collapsed;
            }
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        public void loadDataGridView()
        {
            LoadActiveLongTermGoalAndRefreshDisplay(); // Load active goal and update its display
            _llmConfigErrorDetectedInLoad = false;

            string[] csvFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            DataGrid[] dataGrids = { task1, task2, task3, task4 };

            for (int i = 0; i < csvFiles.Length; i++)
            {
                string filePath = Path.Combine(currentPath, "data", csvFiles[i]);
                List<ItemGrid> allItemsInCsv = HelperClass.ReadCsv(filePath);
                List<ItemGrid> itemsToDisplayInQuadrant;

                if (allItemsInCsv == null)
                {
                    Console.WriteLine($"Error reading CSV file: {filePath}. Or file is empty/new.");
                    allItemsInCsv = new List<ItemGrid>();
                }
                else
                {
                    foreach (var item in allItemsInCsv)
                    {
                        if (!string.IsNullOrWhiteSpace(item.SourceTaskID) && !_syncedTaskSourceIDs.Contains(item.SourceTaskID))
                        {
                            _syncedTaskSourceIDs.Add(item.SourceTaskID);
                        }
                    }
                }

                // Filter tasks for display: only those marked IsActiveInQuadrant
                itemsToDisplayInQuadrant = allItemsInCsv.Where(item => item.IsActiveInQuadrant).ToList();

                dataGrids[i].ItemsSource = null;
                dataGrids[i].ItemsSource = itemsToDisplayInQuadrant;
                if (!dataGrids[i].Items.SortDescriptions.Contains(new SortDescription("Score", ListSortDirection.Descending)))
                {
                    dataGrids[i].Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                }

                // After all files are processed, show a single notification if LLM config error was detected
                if (_llmConfigErrorDetectedInLoad)
                {
                    MessageBox.Show(this, "During task loading, some AI assistant features may have been limited due to a configuration issue (e.g., missing or placeholder API key). Please check the application's setup if you expect full AI functionality.",
                                    "LLM Configuration Issue", MessageBoxButton.OK, MessageBoxImage.Warning);
                    // As per requirement, not resetting _llmConfigErrorDetectedInLoad here.
                }
            }
        }

        public void DataGrid_MouseDoubleClick_AddTask(object sender, MouseButtonEventArgs e)
        {
            DataGrid dataGrid = sender as DataGrid;
            if (dataGrid == null) return;

            // Check if the double-click was on an empty area
            var hitTestResult = VisualTreeHelper.HitTest(dataGrid, e.GetPosition(dataGrid));
            if (hitTestResult != null)
            {
                var visualHit = hitTestResult.VisualHit;
                while (visualHit != null && visualHit != dataGrid)
                {
                    if (visualHit is DataGridRow || visualHit is System.Windows.Controls.Primitives.DataGridColumnHeader || visualHit is DataGridCell)
                    {
                        return; // Click was on a row, header, or cell, not empty space
                    }
                    visualHit = VisualTreeHelper.GetParent(visualHit);
                }
            }
            else
            {
                // Should not happen if click is within the DataGrid bounds
                return;
            }

            int quadrantIndex = -1;
            switch (dataGrid.Name)
            {
                case "task1": quadrantIndex = 0; break;
                case "task2": quadrantIndex = 1; break;
                case "task3": quadrantIndex = 2; break;
                case "task4": quadrantIndex = 3; break;
            }

            if (quadrantIndex == -1)
            {
                Console.WriteLine($"Error: Could not determine quadrant index for DataGrid named {dataGrid.Name}");
                return;
            }

            if (_llmService == null)
            {
                MessageBox.Show("LLM Service is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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



        public MainWindow()
        {
            InitializeComponent();
            
            // 初始化用户体验改进功能
            UXImprovements.Initialize(this);
            
            // 配置验证已移除
            
            _llmService = LlmService.Create();

            
            this.Top = (double)Properties.Settings.Default.Top;
            this.Left = (double)Properties.Settings.Default.Left;
            
            loadDataGridView();

            // Attach CellEditEnding event handler to all DataGrids
            task1.CellEditEnding += DataGrid_CellEditEnding;
            task2.CellEditEnding += DataGrid_CellEditEnding;
            task3.CellEditEnding += DataGrid_CellEditEnding;
            task4.CellEditEnding += DataGrid_CellEditEnding;

            // Initialize and start the reminder timer with configurable interval
            _reminderTimer = new System.Windows.Threading.DispatcherTimer();
            _reminderTimer.Interval = TimeSpan.FromSeconds(Properties.Settings.Default.ReminderCheckIntervalSeconds);
            _reminderTimer.Tick += ReminderTimer_Tick;
            _reminderTimer.Start();
            
            // Start periodic task reminder checks
            StartPeriodicTaskReminderChecks();

            InitializeSyncService();

            // 应用快速改进功能
            ApplyQuickImprovements();
            
            // 启动自动备份
            StartAutoBackup();
        }

        private void ApplyQuickImprovements()
        {
            try
            {
                // UX改进功能已集成到UXImprovements类中
                
                // 设置窗口为可聚焦，以便接收键盘事件
                this.Focusable = true;
                this.KeyDown += MainWindow_KeyDown;
                
                Console.WriteLine("快速改进功能已启用");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启用快速改进功能时出错: {ex.Message}");
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // 处理全局快捷键
            if (e.Key == Key.F1)
            {
                ShowHelpDialog();
                e.Handled = true;
            }
        }

        private void ShowHelpDialog()
        {
            var helpMessage = @"TimeTask 快捷键帮助

常用快捷键:
• Ctrl+N: 快速添加新任务
• Ctrl+F: 搜索任务
• Ctrl+S: 保存所有任务
• Del: 删除选中任务
• F2: 编辑选中任务
• Tab/Shift+Tab: 在象限间切换
• Escape: 清除选择
• F1: 显示此帮助

鼠标操作:
• 双击空白区域: 添加新任务
• 右键菜单: 批量操作
• 拖拽: 移动任务到其他象限

提示: 可以使用Ctrl+多选进行批量操作";

            MessageBox.Show(helpMessage, "快捷键帮助", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ActiveLongTermGoalDisplay_Click(object sender, RoutedEventArgs e)
        {
            if (_activeLongTermGoal != null)
            {
                if (_activeLongTermGoal.IsLearningPlan)
                {
                    string dataFolderPath = Path.Combine(currentPath, "data");
                    LearningPlanManagerWindow managerWindow = new LearningPlanManagerWindow(_activeLongTermGoal, dataFolderPath)
                    {
                        Owner = this
                    };
                    managerWindow.ShowDialog();
                    LoadActiveLongTermGoalAndRefreshDisplay();
                }
                else
                {
                    string dataFolderPath = Path.Combine(currentPath, "data");
                    TimeTask.LongTermGoalManagerWindow managerWindow = new TimeTask.LongTermGoalManagerWindow(_activeLongTermGoal, dataFolderPath)
                    {
                        Owner = this
                    };
                    bool? result = managerWindow.ShowDialog();

                    if (result == true)
                    {
                        LoadActiveLongTermGoalAndRefreshDisplay();
                        loadDataGridView();
                    }
                }
            }
            else
            {
                MessageBox.Show("No active long-term goal to manage.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void ShowFriendlyReminder(ItemGrid task, string message)
        {
            try
            {
                // Generate AI-powered reminder and suggestions
                TimeSpan taskAge = DateTime.Now - task.CreatedDate;
                var (reminder, suggestions) = await _llmService.GenerateTaskReminderAsync(task.Task, taskAge);
                
                // Show modern reminder window
                var reminderWindow = new TaskReminderWindow(task, reminder ?? message, suggestions)
                {
                    Owner = this
                };
                
                var result = reminderWindow.ShowDialog();
                if (result == true)
                {
                    await HandleTaskReminderResult(task, reminderWindow.Result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing friendly reminder: {ex.Message}");
                // Fallback to simple notification
                ShowSimpleNotification(task, message);
            }
        }
        
        private void ShowSimpleNotification(ItemGrid task, string message)
        {
            var notification = new System.Windows.Forms.NotifyIcon
            {
                Visible = true,
                Icon = System.Drawing.SystemIcons.Information,
                BalloonTipTitle = "Task Reminder",
                BalloonTipText = $"{task.Task}\n{message}",
                BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info
            };
            
            notification.ShowBalloonTip(5000);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                System.Threading.Thread.Sleep(5000);
                notification.Dispose();
            });
        }
        
        private async Task HandleTaskReminderResult(ItemGrid task, TaskReminderResult result)
        {
            switch (result)
            {
                case TaskReminderResult.Completed:
                    task.IsActive = false;
                    task.CompletionTime = DateTime.Now;
                    task.CompletionStatus = "Completed";
                    task.LastModifiedDate = DateTime.Now;
                    break;
                    
                case TaskReminderResult.Updated:
                    task.LastModifiedDate = DateTime.Now;
                    task.InactiveWarningCount = 0; // Reset warning count
                    break;
                    
                case TaskReminderResult.Decompose:
                    await HandleTaskDecomposition(task);
                    break;
                    
                case TaskReminderResult.Snoozed:
                    // Snooze for 1 day
                    task.LastModifiedDate = DateTime.Now.AddDays(-Properties.Settings.Default.FirstWarningAfterDays + 1);
                    break;
                    
                case TaskReminderResult.Dismissed:
                default:
                    // Do nothing, just update last interaction time
                    task.LastModifiedDate = DateTime.Now;
                    break;
            }
            
            // Save changes to CSV
            UpdateTaskInAllGrids(task);
        }
        
        private async Task HandleTaskDecomposition(ItemGrid task)
        {
            try
            {
                var (decompositionStatus, subTaskStrings) = await _llmService.DecomposeTaskAsync(task.Task);
                
                if (decompositionStatus == DecompositionStatus.NeedsDecomposition && subTaskStrings != null && subTaskStrings.Any())
                {
                    // Use smart quadrant selector
                    var quadrantSelector = new SmartQuadrantSelectorWindow(subTaskStrings, _llmService)
                    {
                        Owner = this
                    };
                    
                    if (quadrantSelector.ShowDialog() == true)
                    {
                        await AddSubTasksToQuadrants(quadrantSelector.TaskQuadrantAssignments, task);
                        
                        // Mark original task as decomposed
                        task.IsActive = false;
                        task.CompletionStatus = "Decomposed";
                        task.LastModifiedDate = DateTime.Now;
                        
                        MessageBox.Show(this, $"任务已成功分解为 {subTaskStrings.Count} 个子任务并分配到相应象限。", 
                                      "任务分解完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show(this, $"无法自动分解任务 '{task.Task}'。状态: {decompositionStatus}。", 
                                  "分解结果", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling task decomposition: {ex.Message}");
                MessageBox.Show(this, "任务分解过程中发生错误，请稍后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private Task AddSubTasksToQuadrants(Dictionary<string, int> taskQuadrantAssignments, ItemGrid originalTask)
        {
            DataGrid[] dataGrids = { task1, task2, task3, task4 };
            string[] csvNumbers = { "1", "2", "3", "4" };
            
            foreach (var assignment in taskQuadrantAssignments)
            {
                string taskDescription = assignment.Key;
                int quadrantIndex = assignment.Value;
                
                if (quadrantIndex >= 0 && quadrantIndex < dataGrids.Length)
                {
                    var targetGrid = dataGrids[quadrantIndex];
                    var currentGridItems = targetGrid.ItemsSource as List<ItemGrid> ?? new List<ItemGrid>();
                    
                    var newTask = new ItemGrid
                    {
                        Task = taskDescription,
                        Importance = GetImportanceFromQuadrant(quadrantIndex),
                        Urgency = GetUrgencyFromQuadrant(quadrantIndex),
                        IsActive = true,
                        CreatedDate = DateTime.Now,
                        LastModifiedDate = DateTime.Now,
                        LongTermGoalId = originalTask.LongTermGoalId, // Inherit from parent
                        IsActiveInQuadrant = true,
                        InactiveWarningCount = 0
                    };
                    
                    currentGridItems.Add(newTask);
                    targetGrid.ItemsSource = null;
                    targetGrid.ItemsSource = currentGridItems;
                    RefreshDataGrid(targetGrid);
                    update_csv(targetGrid, csvNumbers[quadrantIndex]);
                }
            }
            return Task.CompletedTask;
        }
        
        private string GetImportanceFromQuadrant(int quadrantIndex)
        {
            return quadrantIndex switch
            {
                0 => "High", // 重要且紧急
                1 => "High", // 重要不紧急
                2 => "Low",  // 不重要但紧急
                3 => "Low",  // 不重要不紧急
                _ => "Medium"
            };
        }
        
        private string GetUrgencyFromQuadrant(int quadrantIndex)
        {
            return quadrantIndex switch
            {
                0 => "High", // 重要且紧急
                1 => "Low",  // 重要不紧急
                2 => "High", // 不重要但紧急
                3 => "Low",  // 不重要不紧急
                _ => "Medium"
            };
        }
        
        private void UpdateTaskInAllGrids(ItemGrid task)
        {
            DataGrid[] dataGrids = { task1, task2, task3, task4 };
            string[] csvNumbers = { "1", "2", "3", "4" };
            
            for (int i = 0; i < dataGrids.Length; i++)
            {
                if (dataGrids[i].ItemsSource is List<ItemGrid> items && items.Contains(task))
                {
                    RefreshDataGrid(dataGrids[i]);
                    update_csv(dataGrids[i], csvNumbers[i]);
                    break;
                }
            }
        }
        
        private System.Windows.Threading.DispatcherTimer _taskReminderTimer;
        
        private void StartPeriodicTaskReminderChecks()
        {
            _taskReminderTimer = new System.Windows.Threading.DispatcherTimer();
            _taskReminderTimer.Interval = TimeSpan.FromMinutes(5); // Check every 5 minutes
            _taskReminderTimer.Tick += TaskReminderTimer_Tick;
            _taskReminderTimer.Start();
        }
        
        private async void TaskReminderTimer_Tick(object sender, EventArgs e)
        {
            await CheckForStaleTasksAndRemind();
        }
        
        private Task CheckForStaleTasksAndRemind()
        {
            DataGrid[] dataGrids = { task1, task2, task3, task4 };
            
            foreach (var dataGrid in dataGrids)
            {
                if (dataGrid.ItemsSource is List<ItemGrid> tasks)
                {
                    foreach (var task in tasks.Where(t => t.IsActive && t.IsActiveInQuadrant))
                    {
                        TimeSpan inactiveDuration = DateTime.Now - task.LastModifiedDate;
                        
                        // Check if task needs reminder
                        if (ShouldShowReminder(task, inactiveDuration))
                        {
                            ShowFriendlyReminder(task, GetReminderMessage(task, inactiveDuration));
                            
                            // Update warning count and last modified date
                            task.InactiveWarningCount++;
                            task.LastModifiedDate = DateTime.Now;
                            
                            // Only show one reminder per check cycle
                            break;
                        }
                    }
                }
            }
            return Task.CompletedTask;
        }
        
        private bool ShouldShowReminder(ItemGrid task, TimeSpan inactiveDuration)
        {
            // First warning after configured days
            if (inactiveDuration > FirstWarningAfter && task.InactiveWarningCount == 0)
                return true;
                
            // Second warning after configured days
            if (inactiveDuration > SecondWarningAfter && task.InactiveWarningCount == 1)
                return true;
                
            // Subsequent warnings for very stale tasks
            if (inactiveDuration > StaleTaskThreshold && task.InactiveWarningCount >= 2)
            {
                // Show reminder every few days for very stale tasks
                var daysSinceLastWarning = (DateTime.Now - task.LastModifiedDate).TotalDays;
                return daysSinceLastWarning >= 3; // Remind every 3 days for very stale tasks
            }
            
            return false;
        }
        
        private string GetReminderMessage(ItemGrid task, TimeSpan inactiveDuration)
        {
            if (inactiveDuration <= FirstWarningAfter)
                return "这个任务有一段时间没有更新了，需要关注一下吗？";
            else if (inactiveDuration <= SecondWarningAfter)
                return "这个任务变得有些陈旧了，请考虑尽快更新或完成它。";
            else
                return "这个任务已经很久没有进展了，建议重新评估其优先级或进行分解。";
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
                // Using HeaderText assumes the XAML <DataGridTemplateColumn Header="Task" ... />
                // For DataGridTemplateColumn, e.Column.Header is correct.
                if (e.Column.Header != null && e.Column.Header.ToString() == "Task")
                {
                    var item = e.Row.Item as ItemGrid;
                    if (item != null && e.EditingElement is TextBox textBox)
                    {
                        string newDescription = textBox.Text;
                        // The newDescription is what the user entered.
                        // item.Task will be updated by the binding mechanism after this event if not handled.
                        // We will update it manually here.

                        item.Task = newDescription; // Update the task description
                        item.LastModifiedDate = DateTime.Now; // Update the last modified date

                        Console.WriteLine($"Task edited in grid. Task ID (if available): [{item.SourceTaskID}], New Description: [{newDescription}]");

                        DataGrid sourceGrid = sender as DataGrid;
                        if (sourceGrid != null)
                        {
                            string quadrantNumber = GetQuadrantNumber(sourceGrid.Name);
                            if (!string.IsNullOrEmpty(quadrantNumber))
                            {
                                update_csv(sourceGrid, quadrantNumber);
                                // RefreshDataGrid(sourceGrid); // Optional: Usually binding handles UI update.
                                                            // If not, or if sorting/filtering needs re-evaluation based on new text, uncomment.
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

        // 定义常量用于SetWindowLong
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            IntPtr tWnd = new WindowInteropHelper(this).Handle;
            
            // 设置窗口为最底层（桌面之上，其他窗口之下）
            // 不再将窗口设置为桌面子窗口，避免桌面刷新时消失
            SetWindowPos(tWnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
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
                if (taskToDelete.LongTermGoalId == _activeLongTermGoal?.Id) // Check if deleted task was part of the active goal
                {
                    UpdateLongTermGoalBadge();
                }
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
            
            try
            {
                // 取消任何正在进行的编辑操作
                dataGrid.CancelEdit();
                dataGrid.CommitEdit();
                
                var itemsSource = dataGrid.ItemsSource as List<ItemGrid>;
                if (itemsSource == null) return;
                
                // 安全地刷新数据源
                dataGrid.ItemsSource = null;
                dataGrid.ItemsSource = itemsSource;
                
                // 重新应用排序
                if (!dataGrid.Items.SortDescriptions.Contains(new SortDescription("Score", ListSortDirection.Descending)))
                {
                    dataGrid.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
                }
            }
            catch (Exception ex)
            {
                // 如果刷新失败，尝试简单的Items.Refresh()
                try
                {
                    dataGrid.Items.Refresh();
                }
                catch
                {
                    // 记录错误但不中断程序
                    System.Diagnostics.Debug.WriteLine($"RefreshDataGrid failed: {ex.Message}");
                }
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
            ShowSettingsMenu();
        }
        
        private void ShowSettingsMenu()
        {
            var contextMenu = new ContextMenu();
            
            // LLM设置
            var llmSettingsItem = new MenuItem
            {
                Header = "🤖 AI助手设置",
                ToolTip = "配置大语言模型API设置"
            };
            llmSettingsItem.Click += (s, e) => OpenLlmSettings();
            contextMenu.Items.Add(llmSettingsItem);
            
            // 提醒设置
            var reminderSettingsItem = new MenuItem
            {
                Header = "⏰ 任务提醒设置",
                ToolTip = "配置任务提醒频率和行为"
            };
            reminderSettingsItem.Click += (s, e) => OpenReminderSettings();
            contextMenu.Items.Add(reminderSettingsItem);
            
            contextMenu.Items.Add(new Separator());
            
            // 备份管理
            var backupItem = new MenuItem
            {
                Header = "💾 备份管理",
                ToolTip = "管理数据备份和恢复"
            };
            backupItem.Click += (s, e) => ShowBackupManager();
            contextMenu.Items.Add(backupItem);
            
            // 数据导出
            var exportItem = new MenuItem
            {
                Header = "📤 导出数据",
                ToolTip = "导出任务数据为JSON格式"
            };
            exportItem.Click += async (s, e) => await ExportAllData();
            contextMenu.Items.Add(exportItem);
            
            contextMenu.Items.Add(new Separator());
            
            // 关于
            var aboutItem = new MenuItem
            {
                Header = "ℹ️ 关于",
                ToolTip = "查看应用程序信息"
            };
            aboutItem.Click += (s, e) => ShowAbout();
            contextMenu.Items.Add(aboutItem);
            
            // 显示菜单
            contextMenu.PlacementTarget = SettingsButton;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            contextMenu.IsOpen = true;
        }

        private void ShowBackupManager()
        {
            try
            {
                MessageBox.Show("备份管理功能暂时不可用", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExportAllData()
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON文件|*.json|所有文件|*.*",
                    DefaultExt = "json",
                    FileName = $"TimeTask_Export_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    MessageBox.Show("导出功能暂时不可用", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void OpenLlmSettings()
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
                bool isLearningPlan = goalDialog.IsLearningPlan;

                if (isLearningPlan)
                {
                    await HandleLearningPlanCreation(goalDialog);
                }
                else
                {
                    await HandleNormalGoalCreation(goalDialog);
                }
            }
        }

        private async Task HandleNormalGoalCreation(SetLongTermGoalWindow goalDialog)
        {
            string userGoalDescription = goalDialog.GoalDescription;
            string userDuration = goalDialog.Duration;

            if (_llmService == null)
            {
                MessageBox.Show("LLM Service is not available. Cannot decompose goal.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            List<ProposedDailyTask> proposedTasks = null;
            try
            {
                proposedTasks = await _llmService.DecomposeGoalIntoDailyTasksAsync(userGoalDescription, userDuration);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while trying to decompose the goal: {ex.Message}", "LLM Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (proposedTasks == null || !proposedTasks.Any())
            {
                MessageBox.Show(this, "The LLM could not break down this goal into daily tasks. Please try a different goal or phrasing.", "Goal Decomposition Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ConfirmGoalTasksWindow confirmDialog = new ConfirmGoalTasksWindow(proposedTasks)
            {
                Owner = this
            };

            if (confirmDialog.ShowDialog() == true && confirmDialog.SelectedTasks.Any())
            {
                var newLongTermGoal = new LongTermGoal
                {
                    Description = userGoalDescription,
                    TotalDuration = userDuration,
                    IsActive = true,
                    IsLearningPlan = false
                };

                string longTermGoalsCsvPath = Path.Combine(currentPath, "data", "long_term_goals.csv");
                List<LongTermGoal> allLongTermGoals = HelperClass.ReadLongTermGoalsCsv(longTermGoalsCsvPath);

                foreach (var goal in allLongTermGoals)
                {
                    goal.IsActive = false;
                }

                var existingGoal = allLongTermGoals.FirstOrDefault(g => g.Id == newLongTermGoal.Id);
                if (existingGoal != null)
                {
                    allLongTermGoals.Remove(existingGoal);
                }
                allLongTermGoals.Add(newLongTermGoal);
                HelperClass.WriteLongTermGoalsCsv(allLongTermGoals, longTermGoalsCsvPath);

                int tasksProcessedCount = 0;
                var tasksByQuadrant = confirmDialog.SelectedTasks.GroupBy(taskToAdd =>
                {
                    string quadrant = taskToAdd.Quadrant?.ToLowerInvariant();
                    if (quadrant == "important & urgent") return "1";
                    if (quadrant == "important & not urgent") return "2";
                    if (quadrant == "not important & urgent") return "3";
                    if (quadrant == "not important & not urgent") return "4";
                    return "1";
                });

                foreach (var group in tasksByQuadrant)
                {
                    string targetCsvNumber = group.Key;
                    string quadrantCsvPath = Path.Combine(currentPath, "data", $"{targetCsvNumber}.csv");
                    List<ItemGrid> quadrantTasks = HelperClass.ReadCsv(quadrantCsvPath);
                    if (quadrantTasks == null) quadrantTasks = new List<ItemGrid>();

                    foreach (var taskToAdd in group)
                    {
                        string displayTaskDescription = string.IsNullOrWhiteSpace(taskToAdd.TaskDescription) ? "(Task description not provided)" : taskToAdd.TaskDescription;
                        string displayEstimatedTime = !string.IsNullOrWhiteSpace(taskToAdd.EstimatedTime) ? $" ({taskToAdd.EstimatedTime})" : "";

                        int dayNumber = taskToAdd.Day > 0 ? taskToAdd.Day : 0;

                        var newItem = new ItemGrid
                        {
                            Task = displayTaskDescription + displayEstimatedTime,
                            IsActive = true,
                            Result = string.Empty,
                            CreatedDate = DateTime.Now,
                            LastModifiedDate = DateTime.Now,
                            LongTermGoalId = newLongTermGoal.Id,
                            OriginalScheduledDay = dayNumber,
                            IsActiveInQuadrant = false
                        };

                        switch (taskToAdd.Quadrant?.ToLowerInvariant())
                        {
                            case "important & urgent":
                                newItem.Importance = "High"; newItem.Urgency = "High";
                                break;
                            case "important & not urgent":
                                newItem.Importance = "High"; newItem.Urgency = "Low";
                                break;
                            case "not important & urgent":
                                newItem.Importance = "Low"; newItem.Urgency = "High";
                                break;
                            case "not important & not urgent":
                                newItem.Importance = "Low"; newItem.Urgency = "Low";
                                break;
                            default:
                                newItem.Importance = "High"; newItem.Urgency = "High";
                                break;
                        }
                        quadrantTasks.Add(newItem);
                        tasksProcessedCount++;
                    }
                    HelperClass.WriteCsv(quadrantTasks, quadrantCsvPath);
                }

                if (tasksProcessedCount > 0)
                {
                    MessageBox.Show($"{tasksProcessedCount} sub-task(s) for your long-term goal '{newLongTermGoal.Description}' have been planned. You can manage and activate them from the main screen.", "Long-Term Goal Set", MessageBoxButton.OK, MessageBoxImage.Information);
                    loadDataGridView();
                }
            }
        }

        private async Task HandleLearningPlanCreation(SetLongTermGoalWindow goalDialog)
        {
            string goal = goalDialog.GoalDescription;
            string duration = goalDialog.Duration;
            string subject = goal.Split(new[] { '、', '，', ',', ' ', '的' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? goal;
            DateTime? startDate = DateTime.Now;

            if (_llmService == null)
            {
                MessageBox.Show("LLM Service is not available. Cannot decompose learning plan.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var loadingWindow = new Window
            {
                Title = "正在生成学习计划",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.SingleBorderWindow,
                Content = new StackPanel
                {
                    Margin = new System.Windows.Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "📚 正在为您生成学习计划...",
                            FontSize = 16,
                            FontWeight = FontWeights.Bold,
                            Margin = new System.Windows.Thickness(0, 0, 0, 10)
                        },
                        new TextBlock
                        {
                            Text = $"目标：{goal}\n时长：{duration}",
                            FontSize = 12,
                            Foreground = System.Windows.Media.Brushes.Gray
                        }
                    }
                }
            };

            loadingWindow.Show();

            List<LlmLearningMilestone> proposedMilestones = null;
            try
            {
                proposedMilestones = await _llmService.DecomposeLearningPlanIntoMilestonesAsync(subject, goal, duration);
            }
            catch (Exception ex)
            {
                loadingWindow.Close();
                MessageBox.Show($"An error occurred while trying to decompose the learning plan: {ex.Message}", "LLM Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            loadingWindow.Close();

            if (proposedMilestones == null || !proposedMilestones.Any())
            {
                MessageBox.Show(this, "The LLM could not break down this learning plan into milestones. Please try a different plan or phrasing.", "Learning Plan Decomposition Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newLearningPlan = new LongTermGoal
            {
                Id = Guid.NewGuid().ToString(),
                Description = goal,
                TotalDuration = duration,
                CreationDate = DateTime.Now,
                IsActive = true,
                IsLearningPlan = true,
                Subject = subject,
                StartDate = startDate,
                TotalStages = proposedMilestones.Count,
                CompletedStages = 0
            };

            string longTermGoalsCsvPath = Path.Combine(currentPath, "data", "long_term_goals.csv");
            List<LongTermGoal> allLongTermGoals = HelperClass.ReadLongTermGoalsCsv(longTermGoalsCsvPath);

            foreach (var plan in allLongTermGoals)
            {
                plan.IsActive = false;
            }
            allLongTermGoals.Add(newLearningPlan);
            HelperClass.WriteLongTermGoalsCsv(allLongTermGoals, longTermGoalsCsvPath);

            var convertedMilestones = new List<LearningMilestone>();
            foreach (var llmMilestone in proposedMilestones)
            {
                convertedMilestones.Add(new LearningMilestone
                {
                    Id = Guid.NewGuid().ToString(),
                    LearningPlanId = newLearningPlan.Id,
                    StageName = llmMilestone.Title,
                    Description = llmMilestone.Description,
                    StageNumber = llmMilestone.Stage,
                    TargetDate = null,
                    IsCompleted = false,
                    CompletedDate = null,
                    AssociatedTaskId = null
                });
            }

            string milestonesCsvPath = Path.Combine(currentPath, "data", $"learning_milestones_{newLearningPlan.Id}.csv");
            HelperClass.WriteLearningMilestonesCsv(convertedMilestones, milestonesCsvPath);

            LoadActiveLongTermGoalAndRefreshDisplay();

            var learningPlanManager = new LearningPlanManagerWindow(newLearningPlan, Path.Combine(currentPath, "data"));
            learningPlanManager.Owner = this;
            learningPlanManager.ShowDialog();
        }
    }
}

namespace TimeTask // For InverseBooleanConverter
{
    [System.Windows.Data.ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBooleanConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (targetType != typeof(bool) && targetType != typeof(bool?))
                throw new InvalidOperationException("The target must be a boolean");

            return !(bool?)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (targetType != typeof(bool) && targetType != typeof(bool?))
                throw new InvalidOperationException("The target must be a boolean");

            return !(bool?)value;
        }
    }
}

namespace TimeTask // Ensure it's within the same namespace or accessible
{
    public class LongTermGoal
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public string TotalDuration { get; set; }
        public DateTime CreationDate { get; set; }
        public bool IsActive { get; set; }
        public bool IsLearningPlan { get; set; }
        public string Subject { get; set; }
        public DateTime? StartDate { get; set; }
        public int TotalStages { get; set; }
        public int CompletedStages { get; set; }

        public LongTermGoal()
        {
            Id = Guid.NewGuid().ToString();
            CreationDate = DateTime.Now;
            IsActive = false;
            IsLearningPlan = false;
            TotalStages = 0;
            CompletedStages = 0;
        }

        public double ProgressPercentage
        {
            get
            {
                if (!IsLearningPlan || TotalStages == 0) return 0;
                return (double)CompletedStages / TotalStages * 100;
            }
        }
    }

    public class LearningPlan
    {
        public string Id { get; set; }
        public string Subject { get; set; }
        public string Goal { get; set; }
        public string Duration { get; set; }
        public DateTime CreationDate { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; }
        public int TotalStages { get; set; }
        public int CompletedStages { get; set; }

        public LearningPlan()
        {
            Id = Guid.NewGuid().ToString();
            CreationDate = DateTime.Now;
            IsActive = false;
            TotalStages = 0;
            CompletedStages = 0;
        }

        public double ProgressPercentage
        {
            get
            {
                if (TotalStages == 0) return 0;
                return (double)CompletedStages / TotalStages * 100;
            }
        }
    }

    public class LearningMilestone
    {
        public string Id { get; set; }
        public string LearningPlanId { get; set; }
        public string StageName { get; set; }
        public string Description { get; set; }
        public int StageNumber { get; set; }
        public DateTime? TargetDate { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedDate { get; set; }
        public string AssociatedTaskId { get; set; }

        public LearningMilestone()
        {
            Id = Guid.NewGuid().ToString();
            IsCompleted = false;
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

    public class IsGreaterThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue && parameter is string paramString && int.TryParse(paramString, out int compareToValue))
            {
                return intValue > compareToValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}