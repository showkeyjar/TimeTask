using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data; // For CollectionViewSource if needed for sorting/grouping

namespace TimeTask
{
    public partial class LongTermGoalManagerWindow : Window
    {
        private LongTermGoal _currentGoal;
        public ObservableCollection<ItemGrid> SubTasks { get; set; }
        private string _appDataPath; // Path to the "data" folder
        private bool _dataChanged = false; // Flag to indicate if any data was modified

        public LongTermGoalManagerWindow(LongTermGoal goal, string appDataPath)
        {
            InitializeComponent();
            _currentGoal = goal;
            _appDataPath = appDataPath; // e.g., Path.Combine(mainWindow.currentPath, "data")

            GoalDescriptionTextBlock.Text = _currentGoal.Description;
            SubTasks = new ObservableCollection<ItemGrid>();
            SubTasksDataGrid.ItemsSource = SubTasks;

            LoadSubTasks();
        }

        private void LoadSubTasks()
        {
            SubTasks.Clear();
            if (_currentGoal == null) return;

            string[] quadrantCsvFiles = { "1.csv", "2.csv", "3.csv", "4.csv" };
            List<ItemGrid> allGoalRelatedTasks = new List<ItemGrid>();

            foreach (var csvFile in quadrantCsvFiles)
            {
                string filePath = Path.Combine(_appDataPath, csvFile);
                if (File.Exists(filePath))
                {
                    List<ItemGrid> tasksInQuadrant = HelperClass.ReadCsv(filePath);
                    if (tasksInQuadrant != null)
                    {
                        allGoalRelatedTasks.AddRange(tasksInQuadrant.Where(t => t.LongTermGoalId == _currentGoal.Id));
                    }
                }
            }

            // Sort tasks by OriginalScheduledDay, then by Task description
            var sortedTasks = allGoalRelatedTasks.OrderBy(t => t.OriginalScheduledDay).ThenBy(t => t.Task);
            foreach (var task in sortedTasks)
            {
                SubTasks.Add(task);
            }
             _dataChanged = false; // Reset flag after loading
        }

        private void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            if (SubTasksDataGrid.SelectedItems.Count == 0) return;

            var itemsToModify = SubTasksDataGrid.SelectedItems.Cast<ItemGrid>().ToList();
            bool changedAny = false;

            foreach (var item in itemsToModify)
            {
                if (!item.IsActiveInQuadrant)
                {
                    item.IsActiveInQuadrant = true;
                    item.LastModifiedDate = DateTime.Now; // Optional: update modified date on activation
                    changedAny = true;
                }
            }

            if (changedAny)
            {
                SaveChangesToCsvFiles(itemsToModify);
                _dataChanged = true;
                // DataGrid should update due to TwoWay binding on IsActiveInQuadrant
                // If not, explicitly refresh: SubTasksDataGrid.Items.Refresh();
            }
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (SubTasksDataGrid.SelectedItems.Count == 0) return;

            var itemsToModify = SubTasksDataGrid.SelectedItems.Cast<ItemGrid>().ToList();

            foreach (var item in itemsToModify)
            {
                item.IsActive = !item.IsActive; // Toggle completion status
                item.LastModifiedDate = DateTime.Now;
            }

            SaveChangesToCsvFiles(itemsToModify);
            _dataChanged = true;
            // DataGrid should update due to TwoWay binding on IsCompleted (via IsActive and converter)
            // If not, explicitly refresh: SubTasksDataGrid.Items.Refresh();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (SubTasksDataGrid.SelectedItems.Count == 0) return;

            var itemsToDelete = SubTasksDataGrid.SelectedItems.Cast<ItemGrid>().ToList();
            if (MessageBox.Show($"Are you sure you want to delete {itemsToDelete.Count} selected task(s)? This action cannot be undone.",
                                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
            {
                return;
            }

            // Group by CSV file to optimize read/write
            var groupedByCsv = itemsToDelete.GroupBy(item => GetQuadrantCsvFileForItem(item));

            foreach (var group in groupedByCsv)
            {
                string csvFileName = group.Key;
                string filePath = Path.Combine(_appDataPath, csvFileName);
                List<ItemGrid> tasksInQuadrant = HelperClass.ReadCsv(filePath);
                if (tasksInQuadrant == null) tasksInQuadrant = new List<ItemGrid>();

                bool anyRemovedFromThisCsv = false;
                foreach (var itemToRemove in group)
                {
                    var itemInCsv = tasksInQuadrant.FirstOrDefault(t => t.Task == itemToRemove.Task && t.CreatedDate == itemToRemove.CreatedDate && t.LongTermGoalId == itemToRemove.LongTermGoalId); // More robust ID needed
                    if (itemInCsv != null)
                    {
                        tasksInQuadrant.Remove(itemInCsv);
                        anyRemovedFromThisCsv = true;
                    }
                }
                if (anyRemovedFromThisCsv)
                {
                    HelperClass.WriteCsv(tasksInQuadrant, filePath);
                }
            }

            // Remove from the UI collection
            foreach (var item in itemsToDelete)
            {
                SubTasks.Remove(item);
            }
            _dataChanged = true;
        }

        private void SaveChangesToCsvFiles(List<ItemGrid> modifiedItems)
        {
            if (modifiedItems == null || !modifiedItems.Any()) return;

            // Group by CSV file to optimize read/write
            var groupedByCsv = modifiedItems.GroupBy(item => GetQuadrantCsvFileForItem(item));

            foreach (var group in groupedByCsv)
            {
                string csvFileName = group.Key;
                string filePath = Path.Combine(_appDataPath, csvFileName);

                // Read all tasks from the specific CSV
                List<ItemGrid> tasksInQuadrant = HelperClass.ReadCsv(filePath);
                if (tasksInQuadrant == null) tasksInQuadrant = new List<ItemGrid>();

                bool fileNeedsUpdate = false;
                foreach (var modifiedItem in group)
                {
                    // Find the corresponding item in the list read from CSV and update it.
                    // This assumes Task + CreatedDate + LTG_ID is unique enough for now. A real unique ID per ItemGrid would be better.
                    var itemInCsv = tasksInQuadrant.FirstOrDefault(t => t.Task == modifiedItem.Task && t.CreatedDate == modifiedItem.CreatedDate && t.LongTermGoalId == modifiedItem.LongTermGoalId);
                    if (itemInCsv != null)
                    {
                        // Update properties from the modifiedItem (which is from the DataGrid)
                        itemInCsv.IsActiveInQuadrant = modifiedItem.IsActiveInQuadrant;
                        itemInCsv.IsActive = modifiedItem.IsActive;
                        itemInCsv.LastModifiedDate = modifiedItem.LastModifiedDate;
                        // any other editable properties
                        fileNeedsUpdate = true;
                    }
                    else
                    {
                        // This case (item modified in UI but not found in CSV) should ideally not happen if data is loaded correctly.
                        // Could log an error or even add it if that's desired behavior.
                        Console.WriteLine($"Warning: Modified item '{modifiedItem.Task}' not found in CSV '{csvFileName}' for update.");
                    }
                }

                if (fileNeedsUpdate)
                {
                    HelperClass.WriteCsv(tasksInQuadrant, filePath);
                }
            }
        }


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // If _dataChanged is true, MainWindow might need to refresh its badge/view
            // This can be handled by MainWindow when ShowDialog() returns.
            // Or, pass a flag/event back. For now, just close.
            this.DialogResult = _dataChanged; // Set DialogResult to true if changes were made
            this.Close();
        }

        // Helper method to find which CSV a task belongs to (based on its quadrant properties)
        // This will be needed when saving changes.
        private string GetQuadrantCsvFileForItem(ItemGrid item)
        {
            if (item.Importance == "High" && item.Urgency == "High") return "1.csv";
            if (item.Importance == "High" && item.Urgency == "Low") return "2.csv";
            if (item.Importance == "Low" && item.Urgency == "High") return "3.csv";
            if (item.Importance == "Low" && item.Urgency == "Low") return "4.csv";

            // Fallback or error if quadrant not determined
            Console.WriteLine($"Could not determine quadrant for task: {item.Task}. Defaulting to 1.csv for safety, but this should be reviewed.");
            return "1.csv";
        }
    }
}
