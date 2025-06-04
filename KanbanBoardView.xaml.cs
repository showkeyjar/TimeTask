using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
// using TimeTask; // No longer needed as ItemGrid and HelperClass are in the same namespace
using System.Windows; // Required for DragDrop, DragEventArgs,DataObject, DragDropEffects
using System; // Required for DateTime
using System.Windows.Media; // Required for Brushes

namespace TimeTask // Corrected namespace
{
    /// <summary>
    /// Interaction logic for KanbanBoardView.xaml
    /// </summary>
    public partial class KanbanBoardView : UserControl
    {
        private ItemGrid _draggedItem;
        private Dictionary<string, int> _wipLimits = new Dictionary<string, int>
        {
            { "To Do", 5 },
            { "In Progress", 3 }
        };

        public ObservableCollection<ItemGrid> BacklogTasks { get; set; }
        public ObservableCollection<ItemGrid> ToDoTasks { get; set; }
        public ObservableCollection<ItemGrid> InProgressTasks { get; set; }
        public ObservableCollection<ItemGrid> DoneTasks { get; set; }

        public KanbanBoardView()
        {
            InitializeComponent();

            BacklogTasks = new ObservableCollection<ItemGrid>();
            ToDoTasks = new ObservableCollection<ItemGrid>();
            InProgressTasks = new ObservableCollection<ItemGrid>();
            DoneTasks = new ObservableCollection<ItemGrid>();

            RefreshTasks(); // Renamed for clarity and public access
        }

        public void RefreshTasks() // Made public and renamed
        {
            // Clear existing tasks to avoid duplication if called multiple times
            BacklogTasks.Clear();
            ToDoTasks.Clear();
            InProgressTasks.Clear();
            DoneTasks.Clear();

            var allTasks = new List<ItemGrid>();
            string executablePath = Assembly.GetExecutingAssembly().Location;
            string projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(executablePath)));
            string dataFolderPath = Path.Combine(projectRoot, "data");

            if (!Directory.Exists(dataFolderPath))
            {
                // Handle cases where the data directory might not be found as expected
                // For example, when running tests or from a different deployment structure.
                // Fallback to a path relative to the executable if "data" is not found in the project root.
                string assemblyDir = Path.GetDirectoryName(executablePath);
                dataFolderPath = Path.Combine(assemblyDir, "data");
                if (!Directory.Exists(dataFolderPath))
                {
                    // As a last resort, try the current working directory's data folder
                    dataFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "data");
                     if (!Directory.Exists(dataFolderPath))
                    {
                        // If still not found, output a debug message or handle error
                        System.Diagnostics.Debug.WriteLine($"Data folder not found at {dataFolderPath} or other attempted locations.");
                        return; // Or throw an exception
                    }
                }
            }

            string[] csvFiles = new string[] { "1.csv", "2.csv", "3.csv", "4.csv" }; // These are quadrant files, not stage files.

            foreach (var fileName in csvFiles)
            {
                string filePath = Path.Combine(dataFolderPath, fileName);
                if (File.Exists(filePath))
                {
                    allTasks.AddRange(HelperClass.ReadCsv(filePath));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"CSV file not found: {filePath}");
                }
            }

            foreach (var task in allTasks)
            {
                // Default to Backlog if KanbanStage is null or empty
                string stage = string.IsNullOrEmpty(task.KanbanStage) ? "Backlog" : task.KanbanStage;

                switch (stage)
                {
                    case "Backlog":
                        BacklogTasks.Add(task);
                        break;
                    case "To Do":
                        ToDoTasks.Add(task);
                        break;
                    case "In Progress":
                        InProgressTasks.Add(task);
                        break;
                    case "Done":
                        DoneTasks.Add(task);
                        break;
                    default: // If stage is unrecognized, add to backlog
                        BacklogTasks.Add(task);
                        break;
                }
            }
            UpdateWipLimitVisuals(); // Call after loading tasks
        }

        private void UpdateWipLimitVisuals()
        {
            // Update "To Do" column header
            if (ToDoColumnHeader != null && _wipLimits.TryGetValue("To Do", out int toDoWipLimit))
            {
                ToDoColumnHeader.Text = $"To Do ({ToDoTasks.Count}/{toDoWipLimit})";
                if (ToDoTasks.Count > toDoWipLimit)
                {
                    ToDoColumnHeader.Background = Brushes.PaleVioletRed;
                    ToDoColumnHeader.Text += " - LIMIT EXCEEDED";
                }
                else
                {
                    ToDoColumnHeader.Background = Brushes.LightGray; // Default background
                }
            }
            else if (ToDoColumnHeader != null) // Fallback if not in WIP limits
            {
                ToDoColumnHeader.Text = $"To Do ({ToDoTasks.Count})";
                ToDoColumnHeader.Background = Brushes.LightGray;
            }

            // Update "In Progress" column header
            if (InProgressColumnHeader != null && _wipLimits.TryGetValue("In Progress", out int inProgressWipLimit))
            {
                InProgressColumnHeader.Text = $"In Progress ({InProgressTasks.Count}/{inProgressWipLimit})";
                if (InProgressTasks.Count > inProgressWipLimit)
                {
                    InProgressColumnHeader.Background = Brushes.PaleVioletRed;
                    InProgressColumnHeader.Text += " - LIMIT EXCEEDED";
                }
                else
                {
                    InProgressColumnHeader.Background = Brushes.LightGray; // Default background
                }
            }
            else if (InProgressColumnHeader != null) // Fallback if not in WIP limits
            {
                InProgressColumnHeader.Text = $"In Progress ({InProgressTasks.Count})";
                InProgressColumnHeader.Background = Brushes.LightGray;
            }
        }

        // Drag and Drop Event Handlers
        private void TaskCard_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement frameworkElement && frameworkElement.DataContext is ItemGrid item)
            {
                _draggedItem = item;
                DragDrop.DoDragDrop(frameworkElement, _draggedItem, DragDropEffects.Move);
            }
        }

        private void Column_DragEnter(object sender, DragEventArgs e)
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

        private void Column_DragOver(object sender, DragEventArgs e)
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

        private void Column_Drop(object sender, DragEventArgs e)
        {
            if (_draggedItem == null) return;

            ItemGrid droppedItem = _draggedItem; // Use the stored _draggedItem
            if (droppedItem == null) return;

            FrameworkElement targetElement = sender as FrameworkElement;
            if (targetElement == null || targetElement.Tag == null) return;

            string targetStage = targetElement.Tag.ToString();
            ObservableCollection<ItemGrid> sourceCollection = null;

            if (BacklogTasks.Contains(droppedItem)) sourceCollection = BacklogTasks;
            else if (ToDoTasks.Contains(droppedItem)) sourceCollection = ToDoTasks;
            else if (InProgressTasks.Contains(droppedItem)) sourceCollection = InProgressTasks;
            else if (DoneTasks.Contains(droppedItem)) sourceCollection = DoneTasks;

            if (sourceCollection != null && droppedItem.KanbanStage != targetStage)
            {
                sourceCollection.Remove(droppedItem);
                droppedItem.KanbanStage = targetStage;
                droppedItem.LastModifiedDate = DateTime.Now; // Update LastModifiedDate

                switch (targetStage)
                {
                    case "Backlog": BacklogTasks.Add(droppedItem); break;
                    case "To Do": ToDoTasks.Add(droppedItem); break;
                    case "In Progress": InProgressTasks.Add(droppedItem); break;
                    case "Done": DoneTasks.Add(droppedItem); break;
                }

                // Persist changes
                string csvFileName = GetCsvFileNameForItem(droppedItem);
                string executablePath = Assembly.GetExecutingAssembly().Location;
                string projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(executablePath)));
                string dataFolderPath = Path.Combine(projectRoot, "data");

                if (!Directory.Exists(dataFolderPath))
                {
                    string assemblyDir = Path.GetDirectoryName(executablePath);
                    dataFolderPath = Path.Combine(assemblyDir, "data");
                     if (!Directory.Exists(dataFolderPath))
                    {
                         dataFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "data");
                    }
                }

                string filePath = Path.Combine(dataFolderPath, csvFileName);

                // Get all tasks that BELONG to this CSV file (quadrant)
                // Updated call to pass the instance collections
                List<ItemGrid> tasksForSpecificQuadrant = GetAllTasksForOriginalQuadrant(droppedItem.Importance, droppedItem.Urgency, this.BacklogTasks, this.ToDoTasks, this.InProgressTasks, this.DoneTasks);

                // Ensure the droppedItem in this list has the updated KanbanStage
                // This is implicitly handled if GetAllTasksForOriginalQuadrant correctly pulls from the updated ObservableCollections

                HelperClass.WriteCsv(tasksForSpecificQuadrant, filePath);
            }

            UpdateWipLimitVisuals(); // Call after a task is dropped and collections are updated

            _draggedItem = null;
            e.Handled = true;
        }

        internal static string GetCsvFileNameForItem(ItemGrid item) // Made internal static
        {
            if (item == null) return "1.csv"; // Fallback for null item
            if (item.Importance == "High" && item.Urgency == "High") return "1.csv";
            if (item.Importance == "High" && item.Urgency == "Low") return "2.csv";
            if (item.Importance == "Low" && item.Urgency == "High") return "3.csv";
            if (item.Importance == "Low" && item.Urgency == "Low") return "4.csv";

            System.Diagnostics.Debug.WriteLine($"Warning: Could not determine CSV file for task '{item.Task}' with Importance='{item.Importance}', Urgency='{item.Urgency}'. Defaulting to 1.csv for save attempt.");
            return "1.csv"; // Fallback
        }

        internal static List<ItemGrid> GetAllTasksForOriginalQuadrant(
            string importance,
            string urgency,
            IEnumerable<ItemGrid> backlogTasks,
            IEnumerable<ItemGrid> toDoTasks,
            IEnumerable<ItemGrid> inProgressTasks,
            IEnumerable<ItemGrid> doneTasks)
        {
            if (importance == null || urgency == null) return new List<ItemGrid>();

            var allLoadedTasks = Enumerable.Empty<ItemGrid>();
            if (backlogTasks != null) allLoadedTasks = allLoadedTasks.Concat(backlogTasks);
            if (toDoTasks != null) allLoadedTasks = allLoadedTasks.Concat(toDoTasks);
            if (inProgressTasks != null) allLoadedTasks = allLoadedTasks.Concat(inProgressTasks);
            if (doneTasks != null) allLoadedTasks = allLoadedTasks.Concat(doneTasks);

            return allLoadedTasks.Where(t => t != null && t.Importance == importance && t.Urgency == urgency)
                                 .Distinct()
                                 .ToList();
        }
    }
}
