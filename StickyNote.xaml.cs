using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Automation.Peers;

namespace TimeTask
{
    public partial class StickyNote : Window
    {
        private bool _isMinimized = false;
        private double _savedHeight;
        private WindowState _savedWindowState;
        private bool _isDragging = false;
        private Point _startPoint;
        private ContentPresenter? _contentHost;
        private bool _isClosing = false;
        
        public new object NoteContent
        {
            get => base.Content;
            set
            {
                base.Content = value;
                if (_contentHost != null)
                {
                    _contentHost.Content = value;
                }
            }
        }
        #region Win32 API
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        #endregion

        public StickyNote()
        {
            InitializeComponent();
            
            // Set default content if none provided
            if (this.Content == null)
            {
                this.Content = new StickyNoteContent();
            }
            
            this.SourceInitialized += OnSourceInitialized;
            this.Loaded += OnLoaded;
            
            // Set initial background color
            this.Background = new SolidColorBrush(Colors.White);
            
            // Enable window dragging
            this.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    ToggleMinimize();
                }
                else
                {
                    _isDragging = true;
                    _startPoint = e.GetPosition(this);
                    this.CaptureMouse();
                }
                e.Handled = true;
            };
            
            this.MouseMove += (s, e) =>
            {
                if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
                {
                    Point currentPosition = e.GetPosition(Application.Current.MainWindow);
                    this.Left = currentPosition.X - _startPoint.X;
                    this.Top = currentPosition.Y - _startPoint.Y;
                    e.Handled = true;
                }
            };
            
            this.MouseLeftButtonUp += (s, e) =>
            {
                _isDragging = false;
                this.ReleaseMouseCapture();
                e.Handled = true;
            };
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                // Make window click-through when not focused
                var hwnd = new WindowInteropHelper(this).Handle;
                var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW);
                
                // Set window to be always on top
                this.Topmost = true;
                
                // Set initial position if not already set
                if (this.Left == 0 && this.Top == 0)
                {
                    this.Left = SystemParameters.WorkArea.Width - this.Width - 20;
                    this.Top = 20;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnSourceInitialized: {ex.Message}");
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Apply fade-in animation
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.3)
            };
            this.BeginAnimation(OpacityProperty, fadeIn);
            
            // Set initial background color if not set
            if (this.Background == null)
            {
                this.Background = new SolidColorBrush(Color.FromArgb(255, 255, 255, 200)); // Light yellow
            }
            
            // Set owner to main window to stay on top
            if (Application.Current.MainWindow != null && Application.Current.MainWindow != this)
            {
                this.Owner = Application.Current.MainWindow;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMinimize();
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    this.DragMove();
                }
                catch (InvalidOperationException)
                {
                    // Ignore drag move errors
                }
            }
            e.Handled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isClosing) return;
            _isClosing = true;
            
            try
            {
                var fadeOut = new DoubleAnimation
                {
                    From = this.Opacity,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(0.2)
                };
                
                fadeOut.Completed += (s, _) => 
                {
                    try { this.Close(); }
                    catch { /* Ignore */ }
                };
                
                this.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in CloseButton_Click: {ex.Message}");
                this.Close();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMinimize();
        }

        private void ToggleMinimize()
        {
            if (_isMinimized)
            {
                // Restore
                var heightAnimation = new DoubleAnimation
                {
                    To = _savedHeight,
                    Duration = TimeSpan.FromSeconds(0.2),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                this.BeginAnimation(HeightProperty, heightAnimation);
                this.WindowState = _savedWindowState;
            }
            else
            {
                // Minimize
                _savedHeight = this.Height;
                _savedWindowState = this.WindowState;
                
                var heightAnimation = new DoubleAnimation
                {
                    To = 32, // Just show the header
                    Duration = TimeSpan.FromSeconds(0.2),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };

                heightAnimation.Completed += (s, _) => 
                {
                    this.WindowState = WindowState.Normal;
                    this.Height = 32;
                };

                this.BeginAnimation(HeightProperty, heightAnimation);
            }

            _isMinimized = !_isMinimized;
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            // Make window semi-transparent when not focused
            this.Opacity = 0.7;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Restore full opacity when focused
            this.Opacity = 1.0;
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);
            if (_contentHost != null)
            {
                _contentHost.Content = newContent;
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _contentHost = this.Template?.FindName("ContentHost", this) as ContentPresenter;
            if (_contentHost != null && this.Content != null)
            {
                _contentHost.Content = this.Content;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Clean up resources
                this.SourceInitialized -= OnSourceInitialized;
                this.Loaded -= OnLoaded;
                
                // Clear content to help with garbage collection
                if (_contentHost != null)
                {
                    _contentHost.Content = null;
                    _contentHost = null;
                }
                
                // Clear any running animations
                this.BeginAnimation(OpacityProperty, null);
                this.BeginAnimation(HeightProperty, null);
                
                // Release mouse capture if still active
                if (this.IsMouseCaptured)
                {
                    this.ReleaseMouseCapture();
                }
                
                base.OnClosed(e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnClosed: {ex.Message}");
                base.OnClosed(e);
            }
        }
        
        // Helper method to find a child of a certain type and name in the visual tree
        public static T? FindChild<T>(DependencyObject? parent, string? childName = null) where T : DependencyObject
        {
            if (parent == null) return default;

            T? foundChild = null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T childOfType)
                {
                    foundChild = childOfType;
                    break;
                }
                
                foundChild = FindChild<T>(child, childName);
                if (foundChild != null) break;
                else if (!string.IsNullOrEmpty(childName))
                {
                    var frameworkElement = child as FrameworkElement;
                    if (frameworkElement != null && frameworkElement.Name == childName)
                    {
                        foundChild = (T)child;
                        break;
                    }
                }
                else
                {
                    foundChild = (T)child;
                    break;
                }
            }
            return foundChild;
        }
        
        public new object? Content
        {
            get => ContentHost?.Content;
            set
            {
                if (ContentHost != null && value != null)
                {
                    ContentHost.Content = value;
                }
                else
                {
                    base.Content = value;
                }
            }
        }
    }
}
