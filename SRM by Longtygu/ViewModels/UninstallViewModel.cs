using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Services;

namespace SRM_by_Longtygu.ViewModels
{
    public class UninstallViewModel : ViewModelBase
    {
        private readonly IUninstallService _uninstallService;

        public ObservableCollection<InstalledApp> AppList { get; } = new ObservableCollection<InstalledApp>();
        public ICollectionView AppListView { get; }

        // Danh sách các phần mềm đã tích chọn để hiển thị sang bên phải
        public ObservableCollection<InstalledApp> SelectedApps { get; } = new ObservableCollection<InstalledApp>();
        // Mặc định bật hiển thị App người dùng và App thành phần phụ, Ẩn App hệ thống
        private bool _showUserApps = true;
        public bool ShowUserApps { get => _showUserApps; set { SetProperty(ref _showUserApps, value); AppListView.Refresh(); } }

        private bool _showComponentApps = true;
        public bool ShowComponentApps { get => _showComponentApps; set { SetProperty(ref _showComponentApps, value); AppListView.Refresh(); } }

        private bool _showSystemApps = false;
        public bool ShowSystemApps { get => _showSystemApps; set { SetProperty(ref _showSystemApps, value); AppListView.Refresh(); } }
        public int SelectedCount => SelectedApps.Count;

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    AppListView.Refresh(); // Lọc danh sách ngay khi gõ chữ
                }
            }
        }

        private bool _isUninstalling;
        public bool IsUninstalling { get => _isUninstalling; set => SetProperty(ref _isUninstalling, value); }

        private string _globalStatus = "Đang tải dữ liệu hệ thống...";
        public string GlobalStatus { get => _globalStatus; set => SetProperty(ref _globalStatus, value); }

        public ICommand StartUninstallCommand { get; }

        public UninstallViewModel(IUninstallService uninstallService)
        {
            _uninstallService = uninstallService;

            AppListView = CollectionViewSource.GetDefaultView(AppList);
            // Bộ lọc tìm kiếm
            AppListView.Filter = item =>
            {
                if (item is InstalledApp app)
                {
                    // 1. Kiểm tra qua công tắc phân loại trước
                    bool typeMatch = false;
                    if (app.AppType == "User" && ShowUserApps) typeMatch = true;
                    if (app.AppType == "Component" && ShowComponentApps) typeMatch = true;
                    if (app.AppType == "System" && ShowSystemApps) typeMatch = true;

                    if (!typeMatch) return false;

                    // 2. Nếu qua được công tắc, mới xét tiếp ô Tìm kiếm
                    if (!string.IsNullOrWhiteSpace(SearchText))
                    {
                        return app.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    return true;
                }
                return false;
            };

            StartUninstallCommand = new RelayCommand(_ => _ = ExecuteUninstallAsync(), _ => CanUninstall());

            _ = LoadSystemAppsAsync();
        }

        private async Task LoadSystemAppsAsync()
        {
            try
            {
                var apps = await _uninstallService.GetAllInstalledAppsAsync();
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    AppList.Clear();
                    SelectedApps.Clear();

                    // Sắp xếp theo tên A-Z
                    foreach (var app in apps.OrderBy(a => a.Name))
                    {
                        // Lắng nghe sự kiện người dùng Click vào Checkbox
                        app.PropertyChanged += (s, e) => {
                            if (e.PropertyName == nameof(InstalledApp.IsSelected))
                            {
                                if (app.IsSelected && !SelectedApps.Contains(app))
                                    SelectedApps.Add(app);
                                else if (!app.IsSelected && SelectedApps.Contains(app))
                                    SelectedApps.Remove(app);

                                OnPropertyChanged(nameof(SelectedCount));
                                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                            }
                        };
                        AppList.Add(app);
                    }
                    GlobalStatus = $"Sẵn sàng. Đã quét thấy {AppList.Count} ứng dụng trên hệ thống.";
                });
            }
            catch { GlobalStatus = "Lỗi khi đọc Registry."; }
        }

        private bool CanUninstall() => !IsUninstalling && SelectedCount > 0;

        private async Task ExecuteUninstallAsync()
        {
            // Lấy danh sách từ giỏ hàng thay vì duyệt DataGrid
            var appsToUninstall = SelectedApps.ToList();

            var result = MessageBox.Show($"Bạn đang yêu cầu gỡ cài đặt {appsToUninstall.Count} phần mềm khỏi hệ thống.\n\nChú ý: Một số bộ gỡ cài đặt có thể yêu cầu bạn xác nhận xóa Cache/Data trên màn hình.\nBạn có muốn tiếp tục?",
                                         "CẢNH BÁO", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No) return;

            IsUninstalling = true;
            int total = appsToUninstall.Count;
            int current = 0;

            foreach (var app in appsToUninstall)
            {
                GlobalStatus = $"Đang gọi bộ gỡ cài đặt: {app.Name} ({current + 1}/{total})...";
                app.Status = "Đang gỡ...";

                bool success = await _uninstallService.RunUninstallAsync(app.UninstallString);

                app.Status = success ? "Đã gỡ" : "Lỗi / Hủy";
                if (success)
                {
                    app.IsSelected = false; // Tự động bỏ check và xóa khỏi giỏ hàng
                }

                current++;
            }

            GlobalStatus = $"Hoàn tất! Đã xử lý xong {total} phần mềm. Đang làm mới dữ liệu...";
            IsUninstalling = false;

            // Tải lại danh sách sau khi gỡ
            await LoadSystemAppsAsync();
        }
    }
}