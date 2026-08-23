using System.ComponentModel;
using ACViewer.Utilities;

namespace ACViewer.ViewModels
{
    public abstract class OverrideRowBase : INotifyPropertyChanged
    {
        private uint _newId;
        private bool _isLocked;

        public int PartIndex { get; set; }
        public uint OldId { get; set; }

        public uint NewId
        {
            get => _newId;
            set
            {
                if (_newId == value)
                    return;

                _newId = value;
                OnPropertyChanged(nameof(NewId));
                OnPropertyChanged(nameof(NewHex));
            }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value)
                    return;

                _isLocked = value;
                OnPropertyChanged(nameof(IsLocked));
            }
        }

        public bool IsUserAdded { get; set; }
        public string OldHex => HexId.Format(OldId);
        public string NewHex => HexId.Format(NewId);

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
