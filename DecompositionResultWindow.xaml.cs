using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.ComponentModel; // Required for INotifyPropertyChanged

namespace TimeTask
{
    public class SelectableSubTask : INotifyPropertyChanged
    {
        private string _description;
        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class DecompositionResultWindow : Window
    {
        public List<string> SelectedSubTasks { get; private set; }
        public int ParentQuadrantIndex { get; private set; } // 0-3 for task1-task4 respectively
        public string ParentImportance { get; private set; }
        public string ParentUrgency { get; private set; }


        // Constructor accepting quadrant index
        public DecompositionResultWindow(List<string> subTaskDescriptions, int parentQuadrantIndex)
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow; // Set owner for proper dialog behavior

            this.ParentQuadrantIndex = parentQuadrantIndex;
            // Map index to Importance/Urgency - mirrors logic that will be needed in MainWindow
            (this.ParentImportance, this.ParentUrgency) = GetPriorityFromIndex(parentQuadrantIndex);

            var selectableTasks = subTaskDescriptions.Select(desc => new SelectableSubTask { Description = desc, IsSelected = true }).ToList(); // Default to selected
            SubTasksListBox.ItemsSource = selectableTasks;

            SelectedSubTasks = new List<string>();
        }

        // Overloaded constructor accepting importance/urgency strings
        public DecompositionResultWindow(List<string> subTaskDescriptions, string importance, string urgency)
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow;

            this.ParentImportance = importance;
            this.ParentUrgency = urgency;
            // Attempt to derive ParentQuadrantIndex if needed, or acknowledge it might be -1 (if direct mapping is complex here)
            this.ParentQuadrantIndex = GetIndexFromPriority(importance, urgency);


            var selectableTasks = subTaskDescriptions.Select(desc => new SelectableSubTask { Description = desc, IsSelected = true }).ToList();
            SubTasksListBox.ItemsSource = selectableTasks;
            SelectedSubTasks = new List<string>();
        }


        private void AddSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (SubTasksListBox.ItemsSource is List<SelectableSubTask> items)
            {
                SelectedSubTasks = items.Where(task => task.IsSelected).Select(task => task.Description).ToList();
            }
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // Helper to map quadrant index to Importance/Urgency strings (consistent with MainWindow)
        internal static (string Importance, string Urgency) GetPriorityFromIndex(int index)
        {
            switch (index)
            {
                case 0: return ("High", "High");   // task1: Important & Urgent
                case 1: return ("High", "Low");    // task2: Important & Not Urgent
                case 2: return ("Low", "High");    // task3: Not Important & Urgent
                case 3: return ("Low", "Low");     // task4: Not Important & Not Urgent
                default: return ("High", "High"); // Default to High/High
            }
        }

        // Helper to map Importance/Urgency strings to quadrant index (consistent with AddTaskWindow)
        internal static int GetIndexFromPriority(string importance, string urgency)
        {
            importance = importance?.ToLowerInvariant() ?? "unknown";
            urgency = urgency?.ToLowerInvariant() ?? "unknown";

            if (importance == "high" && urgency == "high") return 0;
            if (importance == "high" && urgency == "low") return 1;
            if (importance == "low" && urgency == "high") return 2;
            if (importance == "low" && urgency == "low") return 3;

            return 0; // Default to quadrant 0 if unknown
        }
    }
}
