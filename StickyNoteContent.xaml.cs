using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace TimeTask
{
    public partial class StickyNoteContent : UserControl, INotifyPropertyChanged
    {
        private string _newTaskDescription = string.Empty;
        private bool _isTaskListMode = true;
        private readonly ObservableCollection<TaskItem> _tasks = new ObservableCollection<TaskItem>();
        private bool _isBold;
        private bool _isItalic;
        private bool _isUnderline;
        private readonly ICommand _addTaskCommand;
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public string NewTaskDescription
        {
            get => _newTaskDescription;
            set
            {
                if (_newTaskDescription != value)
                {
                    _newTaskDescription = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool IsTaskListMode
        {
            get => _isTaskListMode;
            set
            {
                if (_isTaskListMode != value)
                {
                    _isTaskListMode = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public ObservableCollection<TaskItem> Tasks => _tasks;
        
        public ICommand AddTaskCommand => _addTaskCommand;
        
        public StickyNoteContent()
        {
            InitializeComponent();
            DataContext = this;
            
            // Initialize commands
            _addTaskCommand = new RelayCommand(
                param => AddTask(),
                param => !string.IsNullOrWhiteSpace(NewTaskDescription)
            );
            
            // Add a sample task for demonstration
            _tasks.Add(new TaskItem { Description = "Double-click to edit", IsCompleted = false });
            
            // Update command's CanExecute when NewTaskDescription changes
            PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(NewTaskDescription))
                {
                    (_addTaskCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            };
            
            // Set focus to the editor when switching to note mode
            this.IsVisibleChanged += (s, e) => 
            {
                if (IsVisible && !IsTaskListMode)
                {
                    Dispatcher.BeginInvoke(new Action(() => 
                    {
                        NoteEditor.Focus();
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }
            };
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        private void AddTask()
        {
            if (string.IsNullOrWhiteSpace(NewTaskDescription)) return;
            
            Tasks.Add(new TaskItem { Description = NewTaskDescription.Trim() });
            NewTaskDescription = string.Empty;
            
            // Focus the task input for the next task
            Dispatcher.BeginInvoke(new Action(() => 
            {
                TaskInput.Focus();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }
        
        private void TaskInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(NewTaskDescription))
            {
                AddTask();
                e.Handled = true;
            }
        }
        
        private void TaskText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is TextBlock textBlock && 
                textBlock.DataContext is TaskItem taskItem)
            {
                taskItem.BeginEdit();
                e.Handled = true;
            }
        }
        
        private void EditTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }
        
        private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is TaskItem taskItem)
            {
                if (string.IsNullOrWhiteSpace(taskItem.Description))
                {
                    // If the task is empty after edit, remove it
                    Tasks.Remove(taskItem);
                }
                else
                {
                    taskItem.EndEdit();
                }
            }
        }
        
        private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                if (sender is TextBox textBox)
                {
                    if (textBox.DataContext is TaskItem taskItem)
                    {
                        if (e.Key == Key.Enter)
                        {
                            taskItem.EndEdit();
                        }
                        else if (e.Key == Key.Escape)
                        {
                            taskItem.CancelEdit();
                        }
                        
                        // Move focus away from the TextBox to trigger LostFocus
                        var request = new TraversalRequest(FocusNavigationDirection.Next);
                        request.Wrapped = true;
                        textBox.MoveFocus(request);
                        e.Handled = true;
                    }
                }
            }
        }
        
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is TaskItem taskItem)
            {
                Tasks.Remove(taskItem);
            }
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        public bool IsBold
        {
            get => _isBold;
            set
            {
                if (_isBold != value)
                {
                    _isBold = value;
                    OnPropertyChanged();
                    ApplyFormatting();
                }
            }
        }
        
        public bool IsItalic
        {
            get => _isItalic;
            set
            {
                if (_isItalic != value)
                {
                    _isItalic = value;
                    OnPropertyChanged();
                    ApplyFormatting();
                }
            }
        }
        
        public bool IsUnderline
        {
            get => _isUnderline;
            set
            {
                if (_isUnderline != value)
                {
                    _isUnderline = value;
                    OnPropertyChanged();
                    ApplyFormatting();
                }
            }
        }
        

        
        private void ApplyFormatting()
        {
            if (NoteEditor.Selection == null) return;
            
            if (IsBold)
                NoteEditor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
            else
                NoteEditor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                
            if (IsItalic)
                NoteEditor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);
            else
                NoteEditor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
                
            if (IsUnderline)
                NoteEditor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Underline);
            else
            {
                NoteEditor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            }
        }

        public StickyNoteContent()
        {
            InitializeComponent();
            
            // Initialize with empty content
            NoteEditor.Document = new FlowDocument(new Paragraph(new Run("")));
            NoteEditor.Selection.Select(NoteEditor.Document.ContentStart, NoteEditor.Document.ContentStart);
            
            // Set DataContext after initialization
            DataContext = this;
            
            // Initialize color picker
            ColorPicker.ItemsSource = new Brush[]
            {
                new SolidColorBrush(Color.FromRgb(255, 255, 200)), // Yellow
                new SolidColorBrush(Color.FromRgb(200, 255, 200)), // Green
                new SolidColorBrush(Color.FromRgb(200, 200, 255)), // Blue
                new SolidColorBrush(Color.FromRgb(255, 200, 200)), // Red
                new SolidColorBrush(Color.FromRgb(255, 220, 200)), // Orange
                new SolidColorBrush(Color.FromRgb(255, 255, 255))  // White
            };
            ColorPicker.SelectedIndex = 0;
        }

        private void FormatButton_Click(object sender, RoutedEventArgs e)
        {
            // This is now handled by the view model properties and commands
        }

        private void ConvertRichTextToTasks()
        {
            var text = new TextRange(NoteEditor.Document.ContentStart, NoteEditor.Document.ContentEnd).Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                Tasks.Clear();
                foreach (var line in text.Split('\n'))
                {
                    var trimmedLine = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        Tasks.Add(new TaskItem { Description = trimmedLine });
                    }
                }
            }
        }
        
        private void ConvertTasksToRichText()
        {
            if (Tasks.Count > 0)
            {
                var document = new FlowDocument();
                var paragraph = new Paragraph();
                
                foreach (var task in Tasks)
                {
                    paragraph.Inlines.Add($"• {task.Description}\n");
                }
                
                document.Blocks.Add(paragraph);
                NoteEditor.Document = document;
            }
            else
            {
                NoteEditor.Document = new FlowDocument(new Paragraph(new Run("")));
            }
        }

        private void AddTask()
        {
            var newTask = new TaskItem { Description = "New Task" };
            Tasks.Add(newTask);
            
            // Scroll to the bottom using the ScrollViewer
            if (VisualTreeHelper.GetChild(TaskList, 0) is Decorator border && 
                border.Child is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToEnd();
            }
        }

        private void ColorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ColorPicker.SelectedItem is SolidColorBrush selectedBrush)
            {
                var noteWindow = Window.GetWindow(this) as StickyNote;
                if (noteWindow != null)
                {
                    // Update note color with some transparency
                    var color = selectedBrush.Color;
                    color.A = 230;
                    noteWindow.Background = new SolidColorBrush(color);
                    
                    // Update header color (slightly darker)
                    var headerColor = Color.FromArgb(
                        255,
                        (byte)(color.R * 0.8),
                        (byte)(color.G * 0.8),
                        (byte)(color.B * 0.8));
                    
                    var headerBrush = new SolidColorBrush(headerColor);
                    headerBrush.Freeze();
                    this.Resources["NoteHeaderBrush"] = headerBrush;
                }
            }
        }

        public string GetContentAsRtf()
        {
            if (!IsTaskListMode)
            {
                var textRange = new TextRange(
                    NoteEditor.Document.ContentStart,
                    NoteEditor.Document.ContentEnd);
                
                using (var stream = new MemoryStream())
                {
                    textRange.Save(stream, DataFormats.Rtf);
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            else
            {
                // Convert tasks to formatted text for saving
                var sb = new StringBuilder();
                foreach (var task in Tasks)
                {
                    sb.AppendLine($"• {task.Description}");
                }
                return sb.ToString();
            }
        }

        public void LoadContentFromRtf(string rtfContent)
        {
            if (!string.IsNullOrEmpty(rtfContent))
            {
                try
                {
                    var textRange = new TextRange(
                        NoteEditor.Document.ContentStart,
                        NoteEditor.Document.ContentEnd);
                    
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtfContent)))
                    {
                        textRange.Load(stream, DataFormats.Rtf);
                    }
                }
                catch
                {
                    // If RTF loading fails, just set the text
                    NoteEditor.Document.Blocks.Clear();
                    NoteEditor.Document.Blocks.Add(new Paragraph(new Run(rtfContent)));
                }
            }
        }
    }
}
