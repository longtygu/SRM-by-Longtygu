using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace SRM_by_Longtygu.ViewModels
{
    // 3 trạng thái của tab bên cột trái AboutView
    public enum AboutTabType
    {
        AppInfo,
        OperatingPrinciple,
        ActivityLog
    }

    public class AboutViewModel : ViewModelBase
    {
        private readonly ILogService _logService;

        private AboutTabType _currentTab = AboutTabType.AppInfo;
        public AboutTabType CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetProperty(ref _currentTab, value))
                {
                    OnPropertyChanged(nameof(IsShowingAppInfo));
                    OnPropertyChanged(nameof(IsShowingOperatingPrinciple));
                    OnPropertyChanged(nameof(IsShowingActivityLog));
                }
            }
        }

        // Giữ nguyên tên 2 property cũ để không phải sửa các chỗ khác đang bind tới chúng
        public bool IsShowingAppInfo => CurrentTab == AboutTabType.AppInfo;
        public bool IsShowingOperatingPrinciple => CurrentTab == AboutTabType.OperatingPrinciple;
        public bool IsShowingActivityLog => CurrentTab == AboutTabType.ActivityLog;

        // ====== Dữ liệu cho tab Nhật ký hoạt động ======
        public ObservableCollection<LogEntryModel> LogEntries { get; } = new ObservableCollection<LogEntryModel>();
        public ObservableCollection<DateTime> AvailableLogDates { get; } = new ObservableCollection<DateTime>();

        private DateTime? _selectedLogDate;
        public DateTime? SelectedLogDate
        {
            get => _selectedLogDate;
            set
            {
                if (SetProperty(ref _selectedLogDate, value))
                {
                    LoadLogEntries();
                }
            }
        }

        private string _logStatusText = "Chưa tải dữ liệu.";
        public string LogStatusText
        {
            get => _logStatusText;
            set => SetProperty(ref _logStatusText, value);
        }

        public ICommand ShowAppInfoCommand { get; }
        public ICommand ShowOperatingPrincipleCommand { get; }
        public ICommand ShowActivityLogCommand { get; }
        public ICommand RefreshLogCommand { get; }
        public ICommand OpenLogFolderCommand { get; }

        // Constructor nhận ILogService qua DI (AboutViewModel đã được đăng ký Transient trong App.xaml.cs,
        // và ILogService đã đăng ký Singleton -> DI container sẽ tự inject, không cần sửa gì thêm ở App.xaml.cs)
        public AboutViewModel(ILogService logService)
        {
            _logService = logService;

            ShowAppInfoCommand = new RelayCommand(_ => CurrentTab = AboutTabType.AppInfo);
            ShowOperatingPrincipleCommand = new RelayCommand(_ => CurrentTab = AboutTabType.OperatingPrinciple);

            ShowActivityLogCommand = new RelayCommand(_ =>
            {
                CurrentTab = AboutTabType.ActivityLog;
                if (AvailableLogDates.Count == 0)
                {
                    LoadAvailableDates();
                }
            });

            RefreshLogCommand = new RelayCommand(_ =>
            {
                LoadAvailableDates();
                LoadLogEntries();
            });

            OpenLogFolderCommand = new RelayCommand(_ =>
            {
                try { _logService.OpenLogFolder(); }
                catch { /* Bỏ qua nếu không mở được thư mục (VD: môi trường không có Explorer) */ }
            });
        }

        private void LoadAvailableDates()
        {
            AvailableLogDates.Clear();
            foreach (var date in _logService.GetAvailableLogDates())
            {
                AvailableLogDates.Add(date);
            }

            if (AvailableLogDates.Any())
            {
                // Chọn ngày mới nhất mặc định -> tự động trigger LoadLogEntries() qua setter SelectedLogDate
                SelectedLogDate = AvailableLogDates.First();
            }
            else
            {
                LogEntries.Clear();
                LogStatusText = "Chưa có dữ liệu log nào được ghi nhận.";
            }
        }

        private void LoadLogEntries()
        {
            LogEntries.Clear();
            if (SelectedLogDate == null) return;

            var entries = _logService.ReadLogs(SelectedLogDate.Value);
            foreach (var entry in entries)
            {
                LogEntries.Add(entry);
            }

            LogStatusText = entries.Count == 0
                ? "Không có hoạt động nào được ghi nhận trong ngày này."
                : $"{entries.Count} mục hoạt động — cập nhật lúc {DateTime.Now:HH:mm:ss}";
        }
    }
}
