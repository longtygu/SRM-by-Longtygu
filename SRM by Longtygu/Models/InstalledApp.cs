using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SRM_by_Longtygu.Models
{
    // ================= ỨNG DỤNG ĐÃ CÀI ĐẶT TRÊN WINDOWS (UNINSTALLER) =================
    public class InstalledApp : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string InstallDate { get; set; }
        public string EstimatedSize { get; set; }
        public string UninstallString { get; set; }
        public object AppIcon { get; set; }

        // Cờ phân loại dùng cho 3 công tắc lọc (User, Component, System)
        public string AppType { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _status;
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}