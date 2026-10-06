using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using SRM_by_Longtygu.Helpers;

namespace SRM_by_Longtygu.Models
{
    // ================= PHẦN MỀM MẸ =================
    // ĐÃ SỬA: implement INotifyPropertyChanged - chỉ dùng cho 2 property tải cập nhật (transient,
    // không lưu DB) để progress bar tải cập nhật lên đúng theo thời gian thực. Các property cũ
    // vẫn là auto-property bình thường, Dapper vẫn map vào được không ảnh hưởng gì.
    public class Software : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string LicenseKey { get; set; }
        public string Readme { get; set; }
        public string IconPath { get; set; }
        public string DisplaySize { get; set; }

        public string SilentInstallCommand { get; set; }
        public string SilentUninstallCommand { get; set; }
        public string Size { get; set; } // Hiển thị dung lượng
        public string FilePath { get; set; } // Chứa đường dẫn file cài
        // ĐÃ THÊM: Biến lưu trữ phiên bản cụ thể được chọn trong Preset
        public SoftwareVersion SelectedVersion { get; set; }
        public string CreatedDate { get; set; }
        public ObservableCollection<SoftwareVersion> Versions { get; set; } = new ObservableCollection<SoftwareVersion>();

        // MỚI: Danh sách tài nguyên (ISO, DLL, driver, script...) đã được đính kèm cho phần mềm này
        public ObservableCollection<ResourceFile> AttachedResources { get; set; } = new ObservableCollection<ResourceFile>();

        // ===== MỚI: CẤU HÌNH NGUỒN KIỂM TRA CẬP NHẬT =====
        // "None" | "GitHubReleases" | "RegexPage"
        public string UpdateSourceType { get; set; } = "None";

        // GitHubReleases: dạng "owner/repo" (VD: "videolan/vlc")
        // RegexPage: URL trang web dùng để quét (VD: trang download chính thức)
        public string UpdateSourceUrl { get; set; }

        // Chỉ dùng cho RegexPage: regex có đúng 1 capture group lấy ra số phiên bản
        public string UpdateVersionPattern { get; set; }

        // Chỉ dùng cho RegexPage: regex có đúng 1 capture group lấy ra link tải (tuỳ chọn)
        public string UpdateDownloadPattern { get; set; }

        // Kết quả lần quét gần nhất
        public string LatestKnownVersion { get; set; }
        public string LastCheckedDate { get; set; }

        // Cờ tính toán runtime (KHÔNG map cột DB) — so sánh LatestKnownVersion với version cao nhất đang lưu
        public bool HasUpdateAvailable
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LatestKnownVersion)) return false;

                string currentBest = Versions != null && Versions.Count > 0
                    ? Versions.Select(v => v.Version)
                              .OrderByDescending(v => v, VersionComparer.Instance)
                              .FirstOrDefault()
                    : null;

                if (string.IsNullOrWhiteSpace(currentBest)) return true;

                return VersionComparer.Instance.Compare(LatestKnownVersion, currentBest) > 0;
            }
        }

        public bool HasUpdateSourceConfigured =>
            !string.IsNullOrWhiteSpace(UpdateSourceType) && UpdateSourceType != "None";

        // Chuỗi hiển thị sẵn cho UI, tránh phải viết converter riêng trong XAML
        public string LastCheckedDisplay => string.IsNullOrWhiteSpace(LastCheckedDate)
            ? "Chưa quét lần nào"
            : $"Quét lúc: {LastCheckedDate}";

        // Dùng để NHÓM trong danh sách (tránh nhóm "trống tên" khi phần mềm chưa gán danh mục)
        public string CategoryDisplay => string.IsNullOrWhiteSpace(Category) ? "Chưa phân loại" : Category;

        // ===== MỚI: THEO DÕI TIẾN TRÌNH TẢI CẬP NHẬT (transient, không lưu DB) =====
        private bool _isDownloadingUpdate;
        public bool IsDownloadingUpdate
        {
            get => _isDownloadingUpdate;
            set { _isDownloadingUpdate = value; OnPropertyChanged(); }
        }

        // -1 = không xác định được % (hiện icon xoay thay vì thanh %, VD: cài qua Winget).
        // 0-100 = % thật dựa trên byte đã tải / tổng dung lượng file.
        private int _downloadProgressPercent;
        public int DownloadProgressPercent
        {
            get => _downloadProgressPercent;
            set { _downloadProgressPercent = value; OnPropertyChanged(); }
        }

        // MỚI: Số byte đã tải / tổng số byte (0 nếu server không trả Content-Length) + tốc độ tải hiện tại
        private long _downloadedBytes;
        public long DownloadedBytes
        {
            get => _downloadedBytes;
            set { _downloadedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadProgressText)); }
        }

        private long _downloadTotalBytes;
        public long DownloadTotalBytes
        {
            get => _downloadTotalBytes;
            set { _downloadTotalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadProgressText)); }
        }

        private double _downloadSpeedBytesPerSecond;
        public double DownloadSpeedBytesPerSecond
        {
            get => _downloadSpeedBytesPerSecond;
            set { _downloadSpeedBytesPerSecond = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadSpeedText)); }
        }

        // Chuỗi hiển thị sẵn kiểu "45.2 MB / 120.5 MB" (hoặc chỉ "45.2 MB" nếu không rõ tổng dung lượng)
        public string DownloadProgressText
        {
            get
            {
                double downloadedMb = DownloadedBytes / 1048576.0;
                if (DownloadTotalBytes > 0)
                {
                    double totalMb = DownloadTotalBytes / 1048576.0;
                    return $"{downloadedMb:0.#} MB / {totalMb:0.#} MB";
                }
                return $"{downloadedMb:0.#} MB";
            }
        }

        // Chuỗi hiển thị sẵn kiểu "3.2 MB/s" hoặc "850 KB/s" tuỳ độ lớn
        public string DownloadSpeedText
        {
            get
            {
                if (DownloadSpeedBytesPerSecond <= 0) return string.Empty;
                return DownloadSpeedBytesPerSecond >= 1048576
                    ? $"{DownloadSpeedBytesPerSecond / 1048576.0:0.#} MB/s"
                    : $"{DownloadSpeedBytesPerSecond / 1024.0:0.#} KB/s";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ================= PHIÊN BẢN CON =================
    public class SoftwareVersion
    {
        public int Id { get; set; }
        public int SoftwareId { get; set; }
        public string Version { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }

        // Tính toán dung lượng tự động để hiển thị lên giao diện
        public string DisplaySize
        {
            get
            {
                if (FileSize >= 1073741824) return (FileSize / 1073741824.0).ToString("0.##") + " GB";
                if (FileSize >= 1048576) return (FileSize / 1048576.0).ToString("0.##") + " MB";
                if (FileSize >= 1024) return (FileSize / 1024.0).ToString("0.##") + " KB";
                return FileSize + " Bytes";
            }
        }

        public string SHA256 { get; set; }
        public bool IsPortable { get; set; }
        public string CreatedDate { get; set; }

        // MỚI: So sánh theo Id thay vì tham chiếu object mặc định. Cần thiết để các control như
        // ComboBox.SelectedItem nhận diện đúng "phiên bản đã chọn" ngay cả khi đó là 1 object
        // KHÁC instance nhưng cùng Id (VD: 1 object dựng từ Repository, 1 object trong danh sách Versions).
        public override bool Equals(object obj)
        {
            if (obj is SoftwareVersion other) return Id != 0 && Id == other.Id;
            return false;
        }

        public override int GetHashCode() => Id.GetHashCode();
    }

    // 1. Model cho Gói cài đặt (Preset)
    public class Preset
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string IconPath { get; set; }

        // Dung lượng tổng (Tính toán động dựa trên các phần mềm bên trong)
        public string TotalSize { get; set; }

        // Danh sách phần mềm thuộc Preset này
        public System.Collections.Generic.List<Software> Softwares { get; set; } = new System.Collections.Generic.List<Software>();
    }

    // 2. Model Mapping (Dùng để giao tiếp n-n với SQLite)
    public class PresetSoftwareMapping
    {
        public int PresetId { get; set; }
        public int SoftwareId { get; set; }
        // MỚI: Phiên bản cụ thể đã chọn cho phần mềm này trong Preset (NULL = luôn lấy bản mới nhất)
        public int? VersionId { get; set; }
    }

    // MỚI: 3. Model Mapping n-n giữa Software và ResourceFile
    public class SoftwareResourceMapping
    {
        public int SoftwareId { get; set; }
        public int ResourceId { get; set; }
    }
}