using System;
using System.ComponentModel;

namespace TimeTask
{
    public class TaskItem : INotifyPropertyChanged, IEditableObject
    {
        private string _description = string.Empty;
        private bool _isCompleted;
        private string _backupDescription;
        private bool _isEditing;

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        public void BeginEdit()
        {
            if (!_isEditing)
            {
                _backupDescription = _description;
                IsEditing = true;
            }
        }
        
        public void CancelEdit()
        {
            if (_isEditing)
            {
                Description = _backupDescription;
                IsEditing = false;
            }
        }
        
        public void EndEdit()
        {
            if (_isEditing)
            {
                _backupDescription = null;
                IsEditing = false;
            }
        }
    }
}
