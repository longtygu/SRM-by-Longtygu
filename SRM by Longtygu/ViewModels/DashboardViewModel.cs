using LiveCharts;
using LiveCharts.Wpf;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Management;
using System.Collections.Generic;

namespace SRM_by_Longtygu.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly ISoftwareRepository _softwareRepo;
        private readonly ISoftwareVersionRepository _versionRepo;
        private readonly IDashboardService _dashboardService;

        public ObservableCollection<Software> RecentSoftwares { get; } = new ObservableCollection<Software>();
        public ObservableCollection<CategoryStat> CategoryStats { get; } = new ObservableCollection<CategoryStat>();

        // BỔ SUNG: Collection chứa thống kê dung lượng danh mục
        public ObservableCollection<CategoryStorageModel> CategoryStorageStats { get; } = new ObservableCollection<CategoryStorageModel>();

        private double _usedPercentage;
        public double UsedPercentage { get => _usedPercentage; set => SetProperty(ref _usedPercentage, value); }

        private SeriesCollection _categoryPieSeries;
        public SeriesCollection CategoryPieSeries { get => _categoryPieSeries; set => SetProperty(ref _categoryPieSeries, value); }

        private static readonly Brush[] PieColors = new Brush[]
        {
            new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)), // xanh dương
            new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), // xanh lá
            new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), // cam
            new SolidColorBrush(Color.FromRgb(0xE9, 0x1E, 0x63)), // hồng
            new SolidColorBrush(Color.FromRgb(0x9C, 0x27, 0xB0)), // tím
            new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)), // xanh ngọc
            new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)), // vàng
        };

        private void BuildCategoryPieChart()
        {
            var series = new SeriesCollection();
            int i = 0;
            foreach (var cat in CategoryStats)
            {
                series.Add(new PieSeries
                {
                    Title = cat.CategoryName,
                    Values = new ChartValues<int> { cat.Count },
                    Fill = PieColors[i % PieColors.Length],
                    DataLabels = true,
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 0
                });
                i++;
            }
            CategoryPieSeries = series;
        }

        // Các biến đếm
        private int _totalSoftwares; public int TotalSoftwares { get => _totalSoftwares; set => SetProperty(ref _totalSoftwares, value); }
        private int _totalVersions; public int TotalVersions { get => _totalVersions; set => SetProperty(ref _totalVersions, value); }
        private int _totalCategories; public int TotalCategories { get => _totalCategories; set => SetProperty(ref _totalCategories, value); }

        // Các biến Text phụ trợ
        private string _categorySubtitle; public string CategorySubtitle { get => _categorySubtitle; set => SetProperty(ref _categorySubtitle, value); }
        private string _usedStorageText; public string UsedStorageText { get => _usedStorageText; set => SetProperty(ref _usedStorageText, value); }
        private string _totalStorageText; public string TotalStorageText { get => _totalStorageText; set => SetProperty(ref _totalStorageText, value); }
        private string _freeStorageText; public string FreeStorageText { get => _freeStorageText; set => SetProperty(ref _freeStorageText, value); }

        // BỔ SUNG: Tổng dung lượng tài nguyên
        private string _totalResourceSize = "0 MB";
        public string TotalResourceSize { get => _totalResourceSize; set => SetProperty(ref _totalResourceSize, value); }

        public class CategoryStat
        {
            public string CategoryName { get; set; }
            public int Count { get; set; }
        }

        // BỔ SUNG: Model cho ListBox thống kê dung lượng
        public class CategoryStorageModel
        {
            public string CategoryName { get; set; }
            public string CategorySize { get; set; }
            public long SizeBytes { get; set; } // Dùng để sắp xếp logic ngầm
        }

        public DashboardViewModel(IDashboardService dashboardService, ISoftwareRepository softwareRepo, ISoftwareVersionRepository versionRepo)
        {
            _softwareRepo = softwareRepo;
            _versionRepo = versionRepo;
            _dashboardService = dashboardService;

            _ = LoadDashboardDataAsync();
            _ = LoadHardwareInfoAsync();
        }

        private async Task LoadDashboardDataAsync()
        {
            try
            {
                var softwares = await _softwareRepo.GetAllAsync();
                var versions = await _versionRepo.GetAllAsync();

                // Chạy tính toán nặng trên luồng nền để tránh đơ UI
                await Task.Run(() =>
                {
                    long totalResourceBytes = 0;
                    var categoryStorageTemp = new List<CategoryStorageModel>();

                    if (softwares != null && softwares.Any())
                    {
                        var grouped = softwares.GroupBy(s => string.IsNullOrWhiteSpace(s.Category) ? "Khác" : s.Category);

                        foreach (var group in grouped)
                        {
                            long categoryBytes = 0;

                            // Tính dung lượng từng phần mềm trong danh mục
                            foreach (var sw in group)
                            {
                                // LƯU Ý: Giả sử models Version của bạn có thuộc tính SoftwareId và FilePath
                                // Nếu bạn lưu dung lượng sẵn trong Database (vd: ver.FileSize) thì cộng thẳng ver.FileSize vào categoryBytes
                                var swVersions = versions?.Where(v => v.SoftwareId == sw.Id);
                                if (swVersions != null)
                                {
                                    foreach (var ver in swVersions)
                                    {
                                        if (!string.IsNullOrEmpty(ver.FilePath) && File.Exists(ver.FilePath))
                                        {
                                            try
                                            {
                                                categoryBytes += new FileInfo(ver.FilePath).Length;
                                            }
                                            catch { /* Bỏ qua nếu lỗi truy cập file */ }
                                        }
                                    }
                                }
                            }

                            totalResourceBytes += categoryBytes;

                            // Chỉ thêm vào List nếu danh mục đó có dung lượng
                            if (categoryBytes > 0)
                            {
                                categoryStorageTemp.Add(new CategoryStorageModel
                                {
                                    CategoryName = group.Key,
                                    SizeBytes = categoryBytes,
                                    CategorySize = FormatSize(categoryBytes)
                                });
                            }
                        }
                    }

                    // Sắp xếp danh mục từ dung lượng lớn nhất đến nhỏ nhất
                    categoryStorageTemp = categoryStorageTemp.OrderByDescending(x => x.SizeBytes).ToList();

                    // Cập nhật lại UI thông qua Dispatcher
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TotalSoftwares = softwares?.Count() ?? 0;
                        TotalVersions = versions?.Count() ?? 0;
                        TotalCategories = softwares?.Where(s => !string.IsNullOrWhiteSpace(s.Category)).Select(s => s.Category).Distinct().Count() ?? 0;

                        CategorySubtitle = $"{TotalSoftwares} phần mềm / {TotalCategories} danh mục";
                        TotalResourceSize = FormatSize(totalResourceBytes);

                        RecentSoftwares.Clear();
                        if (softwares != null)
                        {
                            foreach (var item in softwares.OrderByDescending(s => s.Id).Take(10))
                                RecentSoftwares.Add(item);
                        }

                        CategoryStats.Clear();
                        if (softwares != null && softwares.Any())
                        {
                            var grouped = softwares.GroupBy(s => string.IsNullOrWhiteSpace(s.Category) ? "Khác" : s.Category);
                            foreach (var group in grouped)
                                CategoryStats.Add(new CategoryStat { CategoryName = group.Key, Count = group.Count() });
                        }
                        else CategoryStats.Add(new CategoryStat { CategoryName = "Thư viện đang trống", Count = 0 });

                        CategoryStorageStats.Clear();
                        foreach (var item in categoryStorageTemp)
                        {
                            CategoryStorageStats.Add(item);
                        }

                        BuildCategoryPieChart();

                        // 4. Tính toán Storage phân vùng cài đặt phần mềm
                        var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name == Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory));
                        if (drive != null)
                        {
                            double totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                            double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                            double usedGb = totalGb - freeGb;

                            UsedStorageText = $"{Math.Round(usedGb, 1)} GB";
                            TotalStorageText = $"/ {Math.Round(totalGb, 1)} GB";
                            FreeStorageText = $"(Còn trống: {Math.Round(freeGb, 1)} GB)";
                            UsedPercentage = (usedGb / totalGb) * 100;
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Lỗi tải Dashboard: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        // ================= HÀM TIỆN ÍCH =================
        // Chuyển đổi Bytes sang định dạng dễ đọc (KB, MB, GB)
        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        // ================= BIẾN GIAO DIỆN PHẦN CỨNG =================
        private string _driveModel = "Đang quét phần cứng...";
        public string DriveModel { get => _driveModel; set => SetProperty(ref _driveModel, value); }

        private string _driveType = "N/A";
        public string DriveType { get => _driveType; set => SetProperty(ref _driveType, value); }

        private string _driveFirmware = "N/A";
        public string DriveFirmware { get => _driveFirmware; set => SetProperty(ref _driveFirmware, value); }

        private string _driveHealth = "N/A";
        public string DriveHealth { get => _driveHealth; set => SetProperty(ref _driveHealth, value); }

        private async Task LoadHardwareInfoAsync()
        {
            try
            {
                string tModel = "Không tìm thấy ổ đĩa", tType = "N/A", tFw = "N/A", tHealth = "N/A";

                await Task.Run(() =>
                {
                    // Xác định ổ đĩa logic (vd: "D:") nơi app đang được cài / chạy
                    string driveLetter = Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory)?.TrimEnd('\\');

                    if (string.IsNullOrEmpty(driveLetter))
                        return;

                    try
                    {
                        // Bước 1: Logical Disk -> Partition chứa nó
                        using (var partSearcher = new ManagementObjectSearcher(
                            $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                        {
                            foreach (ManagementObject partition in partSearcher.Get())
                            {
                                // Bước 2: Partition -> Disk Drive vật lý tương ứng
                                using (var diskSearcher = new ManagementObjectSearcher(
                                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                                {
                                    foreach (ManagementObject disk in diskSearcher.Get())
                                    {
                                        tModel = disk["Model"]?.ToString() ?? "Unknown Model";

                                        string interfaceType = disk["InterfaceType"]?.ToString() ?? "";
                                        string mediaType = disk["MediaType"]?.ToString() ?? "";

                                        bool isUsb = interfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase);

                                        if (isUsb)
                                            tType = "USB (Removable)";
                                        else if (mediaType.IndexOf("Fixed", StringComparison.OrdinalIgnoreCase) >= 0)
                                            tType = "SSD / NVMe";
                                        else
                                            tType = string.IsNullOrEmpty(mediaType) ? interfaceType : mediaType;

                                        tFw = disk["FirmwareRevision"]?.ToString() ?? "N/A";

                                        string status = disk["Status"]?.ToString() ?? "OK";
                                        tHealth = (status == "OK") ? "Tốt (S.M.A.R.T OK)" : "Cảnh báo (Dữ liệu lỗi)";

                                        break; // chỉ lấy ổ vật lý đầu tiên khớp với partition này
                                    }
                                }
                                break; // logical disk chỉ thuộc 1 partition, không cần lặp tiếp
                            }
                        }
                    }
                    catch
                    {
                        // Nếu truy vấn ASSOCIATORS lỗi (thiếu quyền, WMI lỗi...), giữ giá trị mặc định
                    }
                });

                DriveModel = tModel;
                DriveType = tType;
                DriveFirmware = tFw;
                DriveHealth = tHealth;
            }
            catch
            {
                DriveModel = "Lỗi quyền truy cập phần cứng";
                DriveType = "Cần Run as Admin";
            }
        }
    }
}