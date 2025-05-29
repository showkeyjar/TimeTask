using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TimeTask.Models
{
    public class ItemGrid : INotifyPropertyChanged
    {
        // Primary fields
        private string _description = string.Empty;
        private string _category = string.Empty;
        private DateTime _createdTime = DateTime.Now;
        private bool _isCompleted = false;
        
        // Backward compatibility fields
        private string _task = string.Empty;
        private int _importance = 5;
        private int _urgency = 5;
        private double _score = 5.0;
        private bool _isActive = true;
        private DateTime _createdDate = DateTime.Now;
        private DateTime _lastModifiedDate = DateTime.Now;
        private string _result = string.Empty;
        
        // Property changed event
        private PropertyChangedEventHandler? _propertyChanged;
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }

        // Primary properties
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }

        public DateTime CreatedTime
        {
            get => _createdTime;
            set { _createdTime = value; OnPropertyChanged(); }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(); }
        }

        // Backward compatibility properties
        public string Task
        {
            get => _task;
            set { _task = value; OnPropertyChanged(); }
        }

        public int Importance
        {
            get => _importance;
            set { _importance = value; OnPropertyChanged(); UpdateScore(); }
        }

        public int Urgency
        {
            get => _urgency;
            set { _urgency = value; OnPropertyChanged(); UpdateScore(); }
        }

        public double Score
        {
            get => _score;
            private set { _score = value; OnPropertyChanged(); }
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

        public ItemGrid()
        {
            _description = string.Empty;
            _category = string.Empty;
            _task = string.Empty;
            _result = string.Empty;
            _createdTime = DateTime.Now;
            _createdDate = DateTime.Now;
            _lastModifiedDate = DateTime.Now;
            
            // Initialize score based on default importance and urgency
            UpdateScore();
        }

        private void UpdateScore()
        {
            // Simple scoring: average of importance and urgency
            Score = (Importance + Urgency) / 2.0;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (propertyName != nameof(LastModifiedDate) && propertyName != nameof(Score))
            {
                LastModifiedDate = DateTime.Now;
            }
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
        }
    }
}
