#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TimeTask.Models;
using TimeTask.Services;
using MessageBox = System.Windows.MessageBox;
// Using WPF's built-in screen handling instead of Windows Forms
using Application = System.Windows.Application;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace TimeTask
{

    public static class HelperClass
    {
        // ... (no changes)
    }

    public class ItemGrid : INotifyPropertyChanged
    {
        private string _task = string.Empty;
        private string _importance = string.Empty;
        private string _urgency = string.Empty;
        private int _score;
        private bool _isActive;
        private DateTime _createdDate = DateTime.Now;
        private DateTime _lastModifiedDate = DateTime.Now;
        private string _result = string.Empty;
        private PropertyChangedEventHandler? _propertyChanged;

        public string Task
        {
            get => _task;
            set { _task = value; OnPropertyChanged(); }
        }

        public string Importance
        {
            get => _importance;
            set { _importance = value; OnPropertyChanged(); }
        }

        public string Urgency
        {
            get => _urgency;
            set { _urgency = value; OnPropertyChanged(); }
        }

        public int Score
        {
            get => _score;
            set { _score = value; OnPropertyChanged(); }
        }

        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(); }
        }

        public DateTime LastModifiedDate
        {
            get => _lastModifiedDate;
            set { _lastModifiedDate = value; OnPropertyChanged(); }
        }

        public string Result
        {
            get => _result;
            set { _result = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }
    }

    public partial class MainWindow : Window, IDisposable
    {
        private readonly ILogger<MainWindow>? _logger;
        private readonly Services.ILLMService _llmService;
        private readonly IServiceProvider _serviceProvider;
        private bool _disposed = false;
        private readonly List<DispatcherTimer> _timers = new List<DispatcherTimer>();

        // Track the currently selected item and its container
        private ItemGrid? _selectedTask;
        private DataGrid? _selectedTaskGrid;

        // Private fields for UI elements
        private TextBlock StatusBarTextBlock = new TextBlock();
        private TextBox NewTaskTextBox = new TextBox();
        private ComboBox CategoryComboBox = new ComboBox();

        public MainWindow(Services.ILLMService llmService, ILogger<MainWindow>? logger)
        {
            try
            {
                InitializeComponent();
                
                _logger = logger;
                _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
                _serviceProvider = App.ServiceProvider ?? throw new InvalidOperationException("ServiceProvider is not initialized");
                
                // Initialize UI references
                InitializeUIElements();
                
                // Set up window event handlers
                Loaded += MainWindow_Loaded;
                Closing += MainWindow_Closing;
                Closed += MainWindow_Closed;
                
                _logger.LogInformation("MainWindow initialized successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize MainWindow");
                throw;
            }
        }

        private void InitializeUIElements()
        {
            try
            {
                // Initialize UI element references with null checks
                if (FindName("statusBarTextBlock") is TextBlock statusBar)
                    StatusBarTextBlock = statusBar;
                    
                if (FindName("newTaskTextBox") is TextBox newTaskBox)
                    NewTaskTextBox = newTaskBox;

                // AnalysisPanel and AnalysisTextBlock are auto-generated from XAML
                // No need to initialize them here as they're already available
                    
                if (FindName("categoryComboBox") is ComboBox categoryBox)
                {
                    CategoryComboBox = categoryBox;
                    
                    // Initialize ComboBox items
                    CategoryComboBox.ItemsSource = new List<string> 
                    { 
                        "重要且紧急", 
                        "重要不紧急", 
                        "不重要但紧急", 
                        "不重要不紧急" 
                    };
                    CategoryComboBox.SelectedIndex = 0;
                }
                
                _logger?.LogDebug("UI elements initialized");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing UI elements");
                throw;
            }
        }
        


        private void InitializeDataGrids()
        {
            try
            {
                var dataGrids = new[] 
                {
                    this.FindName("task1") as DataGrid,
                    this.FindName("task2") as DataGrid,
                    this.FindName("task3") as DataGrid,
                    this.FindName("task4") as DataGrid
                };

                // Set up each DataGrid with common properties
                foreach (var dataGrid in dataGrids.OfType<DataGrid>())
                {
                    if (dataGrid != null)
                    {
                        dataGrid.ItemsSource = new ObservableCollection<ItemGrid>();
                        dataGrid.AutoGenerateColumns = false;
                        dataGrid.CanUserAddRows = false;
                        dataGrid.CanUserDeleteRows = false;
                        dataGrid.IsReadOnly = true;
                        dataGrid.SelectionMode = DataGridSelectionMode.Single;
                        dataGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
                        dataGrid.RowHeaderWidth = 0;
                        dataGrid.BorderThickness = new Thickness(1);
                        dataGrid.BorderBrush = Brushes.Gray;
                        dataGrid.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                        dataGrid.RowBackground = new SolidColorBrush(Color.FromRgb(37, 37, 38));
                        dataGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                        dataGrid.RowHeight = 30;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing DataGrids");
                throw;
            }
        }

        // Helper method to find a parent of a specific type in the visual tree
        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            // Get the parent of the child
            DependencyObject? parentObject = VisualTreeHelper.GetParent(child);
            
            // If we've reached the root or found the parent we're looking for
            if (parentObject == null)
                return null;
                
            if (parentObject is T parent)
                return parent;
                
            // Continue up the visual tree
            return FindVisualParent<T>(parentObject);
        }

        private void LoadData()
        {
            try
            {
                // Initialize DataGrids first
                InitializeDataGrids();
                
                // Get DataGrid references from XAML and initialize with empty collections
                var dataGrids = new[] 
                {
                    this.FindName("task1") as DataGrid,
                    this.FindName("task2") as DataGrid,
                    this.FindName("task3") as DataGrid,
                    this.FindName("task4") as DataGrid
                };
                
                foreach (var dataGrid in dataGrids.OfType<DataGrid>())
                {
                    dataGrid.ItemsSource = new ObservableCollection<ItemGrid>();
                }
                
                // TODO: Load tasks from data source
                // This will be implemented to load tasks from persistent storage
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading data");
                ShowStatusMessage("加载数据时出错: " + ex.Message, Colors.Red);
            }
        }
        
        private async Task SaveDataAsync()
        {
            try
            {
                // Save tasks to data source
                // This will be implemented later
                
                // Get UI-related values on the UI thread
                double left = 0, top = 0, width = 0, height = 0;
                var windowState = WindowState.Normal;
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    left = Left;
                    top = Top;
                    width = Width;
                    height = Height;
                    windowState = WindowState;
                });
                
                // Save settings on a background thread to avoid UI freezes
                await Task.Run(() =>
                {
                    var settings = TimeTask.Properties.Settings.Default;
                    if (windowState == WindowState.Normal)
                    {
                        settings.WindowLeft = left;
                        settings.WindowTop = top;
                        settings.WindowWidth = width;
                        settings.WindowHeight = height;
                    }
                    settings.WindowState = windowState;
                    settings.Save();
                });
                
                _logger?.LogInformation("Data saved successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving data");
                ShowStatusMessage($"保存数据时出错: {ex.Message}", Colors.Red);
            }
        }

        private void ShowStatusMessage(string message, Color color)
        {
            try
            {
                // Ensure we're on the UI thread
                if (StatusBarTextBlock == null) return;
                
                if (!StatusBarTextBlock.Dispatcher.CheckAccess())
                {
                    // If we're not on the UI thread, invoke on the UI thread
                    StatusBarTextBlock.Dispatcher.InvokeAsync(() => ShowStatusMessage(message, color));
                    return;
                }

                // Clear any existing message
                StatusBarTextBlock.Text = string.Empty;
                
                // Set the new message
                StatusBarTextBlock.Text = message;
                StatusBarTextBlock.Foreground = new SolidColorBrush(color);
                
                // Auto-hide the message after 5 seconds
                var timer = new DispatcherTimer(DispatcherPriority.Normal, StatusBarTextBlock.Dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                
                timer.Tick += (s, e) =>
                {
                    try
                    {
                        if (StatusBarTextBlock != null)
                        {
                            StatusBarTextBlock.Text = string.Empty;
                        }
                    }
                    finally
                    {
                        timer.Stop();
                        _timers.Remove(timer);
                    }
                };
                
                _timers.Add(timer);
                timer.Start();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error showing status message");
            }
        }

        private void SetupTimers()
        {
            try
            {
                // Setup auto-save timer
                var saveTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(30)
                };
                
                // Use a local async method to handle the timer tick
                async void TimerTickHandler(object s, EventArgs e)
                {
                    try 
                    { 
                        await SaveDataAsync(); 
                    }
                    catch (Exception ex) 
                    { 
                        _logger?.LogError(ex, "Auto-save failed"); 
                    }
                }
                
                saveTimer.Tick += TimerTickHandler;
                saveTimer.Start();
                _timers.Add(saveTimer);
                
                _logger.LogDebug("Timers initialized");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting up timers");
                throw;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize data grids and load data
                InitializeDataGrids();
                LoadData();
                
                // Set up timers
                SetupTimers();
                
                // Load window position and size from settings
                LoadWindowSettings();
                
                // Show status message
                ShowStatusMessage("应用程序已启动", Colors.Green);
                _logger.LogInformation("MainWindow loaded successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in MainWindow_Loaded");
                ShowStatusMessage($"加载错误: {ex.Message}", Colors.Red);
            }
        }
        
        private void LoadWindowSettings()
        {
            try
            {
                var settings = Properties.Settings.Default;
                if (settings.WindowLeft >= 0 && settings.WindowLeft + 100 <= SystemParameters.VirtualScreenWidth)
                    Left = settings.WindowLeft;
                    
                if (settings.WindowTop >= 0 && settings.WindowTop + 100 <= SystemParameters.VirtualScreenHeight)
                    Top = settings.WindowTop;
                    
                if (settings.WindowWidth > 0 && settings.WindowWidth <= SystemParameters.PrimaryScreenWidth)
                    Width = settings.WindowWidth;
                    
                if (settings.WindowHeight > 0 && settings.WindowHeight <= SystemParameters.PrimaryScreenHeight)
                    Height = settings.WindowHeight;

                // Ensure window is visible on screen
                EnsureWindowIsVisible();
                
                if (settings.WindowState != WindowState.Minimized)
                {
                    WindowState = settings.WindowState;
                }
                
                _logger.LogDebug("Window settings loaded");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading window settings");
                // Reset to default position/size on error
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        
        private void EnsureWindowIsVisible()
        {
            try
            {
                var screenWidth = SystemParameters.VirtualScreenWidth;
                var screenHeight = SystemParameters.VirtualScreenHeight;
                var screenLeft = SystemParameters.VirtualScreenLeft;
                var screenTop = SystemParameters.VirtualScreenTop;
                
                // Adjust window position if it's outside the screen bounds
                if (Left < screenLeft) Left = screenLeft;
                if (Top < screenTop) Top = screenTop;
                if (Left + Width > screenLeft + screenWidth) Left = screenLeft + screenWidth - Width;
                if (Top + Height > screenTop + screenHeight) Top = screenTop + screenHeight - Height;
                
                // If window is still not visible, center it
                if (Left < 0 || Top < 0 || 
                    Left + Width > SystemParameters.VirtualScreenWidth || 
                    Top + Height > SystemParameters.VirtualScreenHeight)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error ensuring window visibility");
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        
        private void location_Save(object sender, EventArgs e)
        {
            try
            {
                if (WindowState == WindowState.Normal)
                {
                    var settings = Properties.Settings.Default;
                    settings.WindowLeft = Left;
                    settings.WindowTop = Top;
                    settings.WindowWidth = Width;
                    settings.WindowHeight = Height;
                    settings.WindowState = WindowState;
                    settings.Save();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving window position");
            }
        }

        private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _logger?.LogInformation("MainWindow closing - saving settings");
                
                // Save window position and settings when the window is closing
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var settings = TimeTask.Properties.Settings.Default;
                    if (WindowState == WindowState.Normal)
                    {
                        settings.WindowLeft = Left;
                        settings.WindowTop = Top;
                        settings.WindowWidth = Width;
                        settings.WindowHeight = Height;
                    }
                    settings.WindowState = WindowState;
                    settings.Save();
                });
                
                // Save application data
                await SaveDataAsync();
                
                _logger?.LogInformation("Window settings and data saved");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving window settings during close");
                // Don't prevent closing if there's an error
            }
        }

        private void task1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ItemGrid selectedItem)
            {
                _selectedTask = selectedItem;
                _selectedTaskGrid = grid;
            }
        }

        private void task2_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ItemGrid selectedItem)
            {
                _selectedTask = selectedItem;
                _selectedTaskGrid = grid;
            }
        }

        private void task3_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ItemGrid selectedItem)
            {
                _selectedTask = selectedItem;
                _selectedTaskGrid = grid;
            }
        }

        private void task4_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is ItemGrid selectedItem)
            {
                _selectedTask = selectedItem;
                _selectedTaskGrid = grid;
            }
        }

        private async void AddNewTaskButton_Click(object sender, RoutedEventArgs e)
        {
            // Store the button reference
            if (sender is not Button button)
            {
                _logger?.LogWarning("AddNewTaskButton_Click called from non-Button sender");
                return;
            }
            
            object? originalContent = button.Content;
            
            try
            {
                // Disable the button to prevent multiple clicks
                button.IsEnabled = false;
                button.Content = "处理中...";
                
                // Ensure we're on the UI thread
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    // Validate service is available
                    if (_llmService == null)
                    {
                        throw new InvalidOperationException("Language model service is not available");
                    }
                    
                    // Create and show the add task window using DI
                    var addTaskWindow = _serviceProvider.GetRequiredService<AddTaskWindow>();
                    
                    // Set owner to center the dialog on the main window
                    addTaskWindow.Owner = this;
                    addTaskWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    
                    // Show the dialog on the UI thread
                    bool? result = await Application.Current.Dispatcher.InvokeAsync(() => addTaskWindow.ShowDialog());

                    if (result == true && addTaskWindow.NewTask != null)
                    {
                        var task = addTaskWindow.NewTask;
                        if (task == null)
                        {
                            _logger?.LogWarning("New task is null");
                            return;
                        }
                        
                        // Get the selected category from the ComboBox in the add task window
                        string selectedCategory = "";
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            selectedCategory = addTaskWindow.ListSelectorComboBox?.SelectedItem?.ToString() ?? "";
                            
                            // Map the full category name to the standard format if needed
                            if (!string.IsNullOrEmpty(selectedCategory))
                            {
                                selectedCategory = selectedCategory.Split(' ')[0]; // Get the first part (e.g., "重要且紧急" from "重要且紧急 (重要 & 紧急)")
                            }
                        });
                        
                        // Get the target grid based on the selected category
                        DataGrid? targetGrid = GetTargetGrid(selectedCategory);
                        
                        if (targetGrid?.ItemsSource is ObservableCollection<ItemGrid> tasks)
                        {
                            // Add the task to the collection
                            tasks.Add(task);
                            ShowStatusMessage($"任务已添加到: {selectedCategory}", Colors.LightGreen);
                            
                            // Auto-save the updated task list
                            await SaveDataAsync();
                            
                            // Refresh the DataGrid
                            targetGrid.Items.Refresh();
                        }
                        else
                        {
                            _logger?.LogError("Target grid or its ItemsSource is null");
                            ShowStatusMessage("无法添加任务: 目标列表不可用", Colors.Orange);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error adding new task");
                ShowStatusMessage($"添加任务时出错: {ex.Message}", Colors.Red);
                
                // Show detailed error in debug mode
                #if DEBUG
                MessageBox.Show(ex.ToString(), "Error Details", MessageBoxButton.OK, MessageBoxImage.Error);
                #endif
            }
            finally
            {
                // Restore button state on UI thread
                button.Dispatcher.Invoke(() =>
                {
                    button.IsEnabled = true;
                    button.Content = originalContent ?? "添加任务";
                });
            }
        }
        
        private bool LogException(Exception ex, string message)
        {
            _logger?.LogError(ex, message);
            return false; // Always return false to allow the exception to propagate
        }

        /// <summary>
        /// Gets the target DataGrid based on the selected category
        /// </summary>
        /// <param name="category">The selected category (e.g., "重要且紧急")</param>
        /// <returns>Target DataGrid or null if not found</returns>
        private DataGrid? GetTargetGrid(string category)
        {
            try
            {
                // Map category to DataGrid name
                string gridName = category switch
                {
                    "重要且紧急" => "task1",
                    "重要不紧急" => "task2",
                    "不重要但紧急" => "task3",
                    "不重要不紧急" => "task4",
                    _ => throw new ArgumentException($"Unknown category: {category}")
                };

                // Find and return the DataGrid
                return this.FindName(gridName) as DataGrid;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting target grid for category: {Category}", category);
                return null;
            }
        }

        /// <summary>
        /// Handles the click event for the close analysis panel button
        /// </summary>
        private void CloseAnalysisPanel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Find the analysis panel in the visual tree
                var analysisPanel = this.FindName("AnalysisPanel") as FrameworkElement;
                if (analysisPanel != null)
                {
                    // Collapse the panel
                    analysisPanel.Visibility = Visibility.Collapsed;
                    
                    // Optionally, clear any analysis results
                    var analysisTextBlock = this.FindName("AnalysisTextBlock") as TextBlock;
                    if (analysisTextBlock != null)
                    {
                        analysisTextBlock.Text = string.Empty;
                    }
                    
                    // Set focus back to the main content
                    var mainContent = this.FindName("MainContent") as FrameworkElement;
                    mainContent?.Focus();
                    
                    _logger?.LogDebug("Analysis panel closed");
                }
                else
                {
                    _logger?.LogWarning("Analysis panel not found in visual tree");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error closing analysis panel");
                ShowStatusMessage($"关闭分析面板时出错: {ex.Message}", Colors.Red);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects)
                    (_llmService as IDisposable)?.Dispose();
                    (_serviceProvider as IDisposable)?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private async void MainWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                _logger?.LogInformation("MainWindow closed - cleaning up resources");
                
                try
                {
                    // Save any unsaved data
                    await SaveDataAsync();
                }
                catch (Exception saveEx)
                {
                    _logger?.LogError(saveEx, "Error saving data during close");
                }
                
                // Dispose of resources on the UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        Dispose();
                        _logger?.LogInformation("Cleanup completed");
                    }
                    catch (Exception disposeEx)
                    {
                        _logger?.LogError(disposeEx, "Error during cleanup");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during window closed event");
                // At this point, we're closing anyway, so just log the error
            }
        }

        /// <summary>
        /// Configures the dependency injection services
        /// </summary>
        private void ConfigureServices(IServiceCollection services)
        {
            try
            {
                if (services == null)
                {
                    throw new ArgumentNullException(nameof(services));
                }

                // Get API key from configuration
                var apiKey = ConfigurationManager.AppSettings["OpenAIApiKey"]?.Trim();
                
                // Validate API key
                if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_API_KEY_HERE")
                {
                    var errorMsg = "OpenAI API key is not configured or is using the default value. " +
                                  "Please update the app.config file with a valid API key.";
                    _logger?.LogWarning(errorMsg);
                    
                    // Show a message to the user in release mode
                    #if !DEBUG
                    Dispatcher.BeginInvoke(new Action(() => 
                    {
                        ShowStatusMessage("警告: OpenAI API 密钥未配置", Colors.Orange);
                        MessageBox.Show(
                            "OpenAI API 密钥未配置或使用默认值。\n" +
                            "请在 app.config 文件中配置有效的 API 密钥以使用 AI 功能。",
                            "配置警告",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }));
                    #endif
                }
                
                // Register the LLM service with proper error handling
                services.AddScoped<ILLMService>(provider => 
                    new Services.OpenAIService(
                        provider.GetRequiredService<ILogger<Services.OpenAIService>>()));
                        
                _logger?.LogInformation("Services configured successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error configuring services");
                throw;
            }
        }

        // Moved to the top of the file to avoid duplicates

        /// <summary>
        /// Handles the click event for the analyze task button
        /// </summary>
        /// <summary>
        /// Handles the click event for the delete selected task button
        /// </summary>
        private void DeleteSelectedTaskButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedTask == null || _selectedTaskGrid == null)
                {
                    ShowStatusMessage("请先在表格中选择要删除的任务", Colors.Orange);
                    return;
                }
                
                var selectedItem = _selectedTask;
                
                // Confirm deletion
                var result = MessageBox.Show(
                    $"确定要删除任务 '{selectedItem.Task}' 吗？",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                    
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
                
                // Remove the item from the DataGrid's ItemsSource
                if (_selectedTaskGrid.ItemsSource is ObservableCollection<ItemGrid> items)
                {
                    items.Remove(selectedItem);
                    ShowStatusMessage("任务已删除", Colors.LightGreen);
                    _logger?.LogInformation($"Task deleted: {selectedItem.Task}");
                    
                    // Clear the selection
                    _selectedTask = null;
                    _selectedTaskGrid = null;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting selected task");
                ShowStatusMessage($"删除任务时出错: {ex.Message}", Colors.Red);
                
                // Show detailed error in debug mode
                #if DEBUG
                MessageBox.Show(ex.ToString(), "Error Details", MessageBoxButton.OK, MessageBoxImage.Error);
                #endif
            }
        }
        
        /// <summary>
        /// Handles the click event for the analyze task button
        /// </summary>
        private async void AnalyzeTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NewTaskTextBox.Text))
            {
                ShowStatusMessage("请输入要分析的任务描述", Colors.Orange);
                return;
            }

            try
            {
                // Validate service is available
                if (_llmService == null)
                {
                    throw new InvalidOperationException("Language model service is not available");
                }

                // Show loading state
                AnalyzeTaskButton.IsEnabled = false;
                AnalyzeTaskButton.Content = "分析中...";
                Mouse.OverrideCursor = Cursors.Wait;
                
                // Clear previous results
                AnalysisTextBlock.Text = string.Empty;
                AnalysisPanel.Visibility = Visibility.Visible;
                
                // Call LLM service for analysis
                var taskDescription = NewTaskTextBox.Text.Trim();
                _logger?.LogInformation($"Analyzing task: {taskDescription}");
                
                // Run analysis and classification in parallel
                var analysisTask = _llmService.AnalyzeTaskAsync(taskDescription);
                var categoryTask = _llmService.ClassifyTaskAsync(taskDescription);
                
                // Wait for both tasks to complete
                await Task.WhenAll(analysisTask, categoryTask);
                
                // Update UI with results
                AnalysisTextBlock.Text = analysisTask.Result;
                
                // Set category if valid
                if (categoryTask.Result >= 0 && CategoryComboBox != null && 
                    categoryTask.Result < CategoryComboBox.Items.Count)
                {
                    CategoryComboBox.SelectedIndex = categoryTask.Result;
                }

                ShowStatusMessage("任务分析完成", Colors.LightGreen);
                _logger?.LogInformation("Task analysis completed successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error analyzing task");
                ShowStatusMessage($"分析任务时出错: {ex.Message}", Colors.Red);
                
                // Show detailed error in debug mode
                #if DEBUG
                MessageBox.Show(ex.ToString(), "Error Details", MessageBoxButton.OK, MessageBoxImage.Error);
                #endif
            }
            finally
            {
                AnalyzeTaskButton.IsEnabled = true;
                AnalyzeTaskButton.Content = "分析任务";
                Mouse.OverrideCursor = null;
            }
        }
    }
} // Closing brace for namespace TimeTask
