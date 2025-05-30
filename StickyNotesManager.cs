using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TimeTask
{
    public class StickyNotesManager
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TimeTask");
        
        private static readonly string NotesPath = Path.Combine(AppDataPath, "Notes");
        private static readonly string SettingsPath = Path.Combine(AppDataPath, "settings.json");
        
        private readonly NotifyIcon _notifyIcon;
        private readonly List<StickyNote> _notes = new List<StickyNote>();
        private bool _isExiting = false;

        public StickyNotesManager()
        {
            // Ensure directories exist
            Directory.CreateDirectory(NotesPath);
            
            // Initialize system tray icon
            _notifyIcon = new NotifyIcon
            {
                Icon = new System.Drawing.Icon(Application.GetResourceStream(
                    new Uri("pack://application:,,,/Assets/icon.ico")).Stream),
                Visible = true,
                Text = "TimeTask Sticky Notes"
            };
            
            // Create context menu
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("New Note", null, (s, e) => CreateNewNote());
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
            
            // Handle double click
            _notifyIcon.DoubleClick += (s, e) => CreateNewNote();
            
            // Handle application exit
            Application.Current.Exit += (s, e) => Cleanup();
            
            // Load saved notes
            LoadNotes();
        }
        
        private void CreateNewNote(Point? position = null, Size? size = null, string? content = null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var note = new StickyNote();
                var contentControl = new StickyNoteContent();
                note.ContentHost.Content = contentControl;
                
                note.Closed += (s, e) => 
                {
                    _notes.Remove(note);
                    SaveNote(note);
                    note = null;
                };
                
                // Set initial position and size
                if (position.HasValue)
                {
                    note.Left = position.Value.X;
                    note.Top = position.Value.Y;
                }
                else
                {
                    // Default position: cascaded from top-right
                    note.Left = SystemParameters.WorkArea.Right - 300;
                    note.Top = 50 + (_notes.Count * 30) % 300;
                }
                
                if (size.HasValue)
                {
                    note.Width = size.Value.Width;
                    note.Height = size.Value.Height;
                }
                else
                {
                    note.Width = 300;
                    note.Height = 300;
                }
                
                // Set content if provided
                if (content != null && note.Content is StickyNoteContent stickyContent)
                {
                    stickyContent.LoadContentFromRtf(content);
                }
                
                note.Show();
                _notes.Add(note);
                
                // Bring to front
                note.Activate();
            });
        }
        
        private void LoadNotes()
        {
            try
            {
                if (!Directory.Exists(NotesPath)) return;
                
                foreach (var file in Directory.GetFiles(NotesPath, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var noteData = JsonSerializer.Deserialize<NoteData>(json);
                        
                        if (noteData != null)
                        {
                            CreateNewNote(
                                new Point(noteData.Left, noteData.Top),
                                new Size(noteData.Width, noteData.Height),
                                noteData.Content);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading note {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading notes: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SaveNote(StickyNote note)
        {
            try
            {
                var contentControl = note.Content as StickyNoteContent;
                var content = contentControl?.GetContentAsRtf() ?? string.Empty;
                
                var noteData = new NoteData
                {
                    Left = (int)note.Left,
                    Top = (int)note.Top,
                    Width = (int)note.Width,
                    Height = (int)note.Height,
                    Content = content
                };
                
                var json = JsonSerializer.Serialize(noteData, new JsonSerializerOptions { WriteIndented = true });
                var noteId = note.GetHashCode().ToString("X8");
                var filePath = Path.Combine(NotesPath, $"note_{noteId}.json");
                
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving note: {ex.Message}");
            }
        }
        
        private void Cleanup()
        {
            if (_isExiting) return;
            _isExiting = true;
            
            try
            {
                // Save all notes
                foreach (var note in _notes.ToArray())
                {
                    SaveNote(note);
                    note.Close();
                }
                
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }
        
        private void ExitApplication()
        {
            var result = MessageBox.Show("Close all sticky notes and exit?", "Exit", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                Cleanup();
                Application.Current.Shutdown();
            }
        }
        
        private class NoteData
        {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string Content { get; set; } = string.Empty;
            
            public NoteData()
            {
                // Initialize with default values
                Width = 300;
                Height = 300;
                Left = 100;
                Top = 100;
            }
        }
    }
}
