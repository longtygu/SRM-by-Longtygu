using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace SRM_by_Longtygu.ViewModels
{
    // Trạng thái xử lý của 1 phần mềm trong hàng đợi "Thêm nhiều phần mềm"
    public enum EntryStatus
    {
        Pending,
        Processing,
        Success,
        Duplicate,
        Error
    }

    // Đại diện cho 1 phần mềm đang chờ trong hàng đợi thêm hàng loạt.
    // Gom lại toàn bộ các field mà trước đây nằm trực tiếp trong AddSoftwareViewModel,
    // để mỗi file được chọn có thể có Name/Version/Category/... riêng của nó.
    public class SoftwareEntryItem : ViewModelBase
    {
        public Guid QueueId { get; } = Guid.NewGuid();

        private string _filePath;
        public string FilePath
        {
            get => _filePath;
            set
            {
                if (SetProperty(ref _filePath, value))
                {
                    FileSizeDisplay = ComputeFileSize(value);
                }
            }
        }

        private string _iconPath;
        public string IconPath { get => _iconPath; set => SetProperty(ref _iconPath, value); }

        private string _name;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _version;
        public string Version { get => _version; set => SetProperty(ref _version, value); }

        private string _publisher;
        public string Publisher { get => _publisher; set => SetProperty(ref _publisher, value); }

        private string _category;
        public string Category { get => _category; set => SetProperty(ref _category, value); }

        private string _silentCommand;
        public string SilentCommand { get => _silentCommand; set => SetProperty(ref _silentCommand, value); }

        private string _silentUninstallCommand;
        public string SilentUninstallCommand { get => _silentUninstallCommand; set => SetProperty(ref _silentUninstallCommand, value); }

        private bool _isPortable;
        public bool IsPortable { get => _isPortable; set => SetProperty(ref _isPortable, value); }

        private string _licenseKey;
        public string LicenseKey { get => _licenseKey; set => SetProperty(ref _licenseKey, value); }

        private string _description;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private string _readme;
        public string Readme { get => _readme; set => SetProperty(ref _readme, value); }

        private bool _isSafeToSilentInstall;
        public bool IsSafeToSilentInstall
        {
            get => _isSafeToSilentInstall;
            set
            {
                if (SetProperty(ref _isSafeToSilentInstall, value))
                {
                    // Tự động phân tích và hiển thị lệnh ngầm ngay khi tick Checkbox
                    if (value && !string.IsNullOrWhiteSpace(FilePath))
                    {
                        var commands = SRM_by_Longtygu.Helpers.InstallerAnalyzer.Analyze(FilePath);
                        SilentCommand = commands.InstallCmd;
                        SilentUninstallCommand = commands.UninstallCmd;
                    }
                    else if (!value)
                    {
                        SilentCommand = "";
                        SilentUninstallCommand = "";
                    }
                }
            }
        }

        // ================= TRẠNG THÁI XỬ LÝ (hiển thị trong danh sách hàng đợi) =================

        private EntryStatus _status = EntryStatus.Pending;
        public EntryStatus Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    StatusLabel = ComputeStatusLabel(value);
                    StatusBrush = ComputeStatusBrush(value);
                }
            }
        }

        private string _statusMessage = "";
        // Lý do trùng lặp / lỗi (nếu có) để hiển thị cho người dùng biết cách xử lý
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string _statusLabel = "Chờ xử lý";
        public string StatusLabel { get => _statusLabel; private set => SetProperty(ref _statusLabel, value); }

        private Brush _statusBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
        public Brush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }

        private string _fileSizeDisplay = "";
        public string FileSizeDisplay { get => _fileSizeDisplay; private set => SetProperty(ref _fileSizeDisplay, value); }

        // ================= HÀM TIỆN ÍCH =================

        // Tự động điền Tên / Phiên bản / Nhà phát triển từ metadata của file cài đặt
        // (Tương tự AutoFillData cũ trong AddSoftwareViewModel, nhưng chạy trên chính entry này)
        public void AutoFillFromFile()
        {
            if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath)) return;

            string fileNameOnly = Path.GetFileNameWithoutExtension(FilePath);
            try
            {
                var info = FileVersionInfo.GetVersionInfo(FilePath);
                Name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName : fileNameOnly;
                Publisher = info.CompanyName;

                string extractedVer = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
                if (string.IsNullOrWhiteSpace(extractedVer) || extractedVer.Trim() == "1.0" || extractedVer.Trim() == "0.0.0.0")
                {
                    var match = Regex.Match(fileNameOnly, @"\d+(\.\d+)+");
                    if (match.Success) extractedVer = match.Value;
                }
                Version = !string.IsNullOrWhiteSpace(extractedVer) ? extractedVer : "1.0";
            }
            catch
            {
                Name = fileNameOnly;
                Version = "1.0";
            }
        }

        private static string ComputeFileSize(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";
                double mb = new FileInfo(path).Length / (1024.0 * 1024.0);
                return mb >= 1024 ? $"{(mb / 1024.0):0.##} GB" : $"{mb:0.##} MB";
            }
            catch
            {
                return "";
            }
        }

        private static string ComputeStatusLabel(EntryStatus status)
        {
            switch (status)
            {
                case EntryStatus.Pending: return "Chờ xử lý";
                case EntryStatus.Processing: return "Đang xử lý...";
                case EntryStatus.Success: return "Thành công";
                case EntryStatus.Duplicate: return "Trùng lặp";
                case EntryStatus.Error: return "Lỗi";
                default: return "";
            }
        }

        private static Brush ComputeStatusBrush(EntryStatus status)
        {
            switch (status)
            {
                case EntryStatus.Pending: return new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
                case EntryStatus.Processing: return new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
                case EntryStatus.Success: return new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                case EntryStatus.Duplicate: return new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
                case EntryStatus.Error: return new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23));
                default: return Brushes.Gray;
            }
        }
    }
}
