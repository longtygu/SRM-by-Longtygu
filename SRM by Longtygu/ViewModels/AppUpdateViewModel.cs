using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Services;
using SRM_by_Longtygu.Services.AppUpdate;

namespace SRM_by_Longtygu.ViewModels
{
    /// <summary>
    /// ViewModel của card "Cập nhật ứng dụng" trong trang Cài đặt (đăng ký Singleton để giữ kết quả khi chuyển trang).
    /// Kiểm tra bản mới, mở trang Release, hoặc tự tải + xác minh + cài đặt rồi khởi động lại (có hoàn tác khi lỗi).
    /// </summary>
    public class AppUpdateViewModel : ViewModelBase
    {
        private const int MaxNotesLength = 500;
        private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

        private static readonly Brush NeutralBrush = CreateBrush(0xAA, 0xAA, 0xAA);
        private static readonly Brush OkBrush = CreateBrush(0x6C, 0xCB, 0x5F);
        private static readonly Brush UpdateBrush = CreateBrush(0xFB, 0xBF, 0x24);
        private static readonly Brush ErrorBrush = CreateBrush(0xF8, 0x71, 0x71);

        private readonly IAppSelfUpdateChecker _checker;
        private readonly IAppUpdateDownloader _downloader;
        private readonly IAppUpdateInstaller _installer;
        private readonly ILogService _logService;
        private readonly IDialogService _dialogService;
        private readonly IBusyService _busy;

        // Bản tin tiến độ có thể đến muộn hơn thông báo kết quả cuối cùng; cờ này chặn việc chúng ghi đè thông báo cuối
        private volatile bool _acceptProgress;

        private string? _releaseUrl;
        private AppUpdateResult? _lastResult;

        // ================= THUỘC TÍNH HIỂN THỊ: KIỂM TRA =================
        public string CurrentVersionText { get; }

        private string _lastCheckedText = "Chưa kiểm tra lần nào";
        public string LastCheckedText { get => _lastCheckedText; private set => SetProperty(ref _lastCheckedText, value); }

        private string _statusText = "Nhấn \"Kiểm tra cập nhật\" để xem có bản mới hay không.";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        private Brush _statusBrush = NeutralBrush;
        public Brush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }

        private bool _isChecking;
        public bool IsChecking
        {
            get => _isChecking;
            private set
            {
                if (SetProperty(ref _isChecking, value))
                    OnPropertyChanged(nameof(CheckButtonText));
            }
        }

        public string CheckButtonText => IsChecking ? "Đang kiểm tra..." : "Kiểm tra cập nhật";

        private bool _hasUpdate;
        public bool HasUpdate
        {
            get => _hasUpdate;
            private set
            {
                if (SetProperty(ref _hasUpdate, value))
                    OnPropertyChanged(nameof(CanAutoDownload));
            }
        }

        private string _releaseNotesPreview = string.Empty;
        public string ReleaseNotesPreview
        {
            get => _releaseNotesPreview;
            private set
            {
                if (SetProperty(ref _releaseNotesPreview, value))
                    OnPropertyChanged(nameof(HasReleaseNotes));
            }
        }

        public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(_releaseNotesPreview);

        // ================= THUỘC TÍNH HIỂN THỊ: TẢI VỀ =================

        /// <summary>True khi có bản mới VÀ bản đó có file gói + mã SHA256 từ GitHub (đủ điều kiện tự tải).</summary>
        public bool CanAutoDownload => HasUpdate && _lastResult != null && _lastResult.CanDownloadAutomatically;

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            private set
            {
                if (SetProperty(ref _isDownloading, value))
                    OnPropertyChanged(nameof(DownloadButtonText));
            }
        }

        public string DownloadButtonText => IsDownloading ? "Đang cập nhật..." : "Tải về và cài đặt";

        private bool _showDownloadPanel;
        public bool ShowDownloadPanel { get => _showDownloadPanel; private set => SetProperty(ref _showDownloadPanel, value); }

        private double _downloadProgressValue;
        public double DownloadProgressValue { get => _downloadProgressValue; private set => SetProperty(ref _downloadProgressValue, value); }

        private string _downloadStatusText = string.Empty;
        public string DownloadStatusText { get => _downloadStatusText; private set => SetProperty(ref _downloadStatusText, value); }

        private Brush _downloadStatusBrush = NeutralBrush;
        public Brush DownloadStatusBrush { get => _downloadStatusBrush; private set => SetProperty(ref _downloadStatusBrush, value); }

        private string _downloadLocationText = string.Empty;
        public string DownloadLocationText { get => _downloadLocationText; private set => SetProperty(ref _downloadLocationText, value); }

        // ================= LỆNH =================
        public ICommand CheckForUpdateCommand { get; }
        public ICommand OpenReleasePageCommand { get; }
        public ICommand DownloadUpdateCommand { get; }

        public AppUpdateViewModel(
            IAppSelfUpdateChecker checker,
            IAppUpdateDownloader downloader,
            IAppUpdateInstaller installer,
            ILogService logService,
            IDialogService dialogService,
            IBusyService busyService)
        {
            _checker = checker;
            _downloader = downloader;
            _installer = installer;
            _logService = logService;
            _dialogService = dialogService;
            _busy = busyService;

            CurrentVersionText = "v" + FormatVersion(_checker.GetCurrentVersion());

            CheckForUpdateCommand = new RelayCommand(async _ => await CheckAsync(), _ => !IsChecking && !IsDownloading);
            OpenReleasePageCommand = new RelayCommand(_ => OpenReleasePage(), _ => HasUpdate && !IsDownloading);
            DownloadUpdateCommand = new RelayCommand(async _ => await DownloadAsync(),
                _ => CanAutoDownload && !IsDownloading && !IsChecking && !_busy.IsBusy);
        }

        // ================= KIỂM TRA =================
        private async Task CheckAsync()
        {
            if (IsChecking || IsDownloading) return;

            IsChecking = true;
            HasUpdate = false;
            _lastResult = null;
            ReleaseNotesPreview = string.Empty;
            _releaseUrl = null;
            ShowDownloadPanel = false;
            DownloadStatusText = string.Empty;
            SetStatus("Đang kiểm tra cập nhật...", NeutralBrush);

            try
            {
                var result = await _checker.CheckAsync();
                ApplyResult(result);
            }
            catch (Exception ex) // service không ném lỗi, đây chỉ là lưới an toàn
            {
                _logService.LogError("[Cập nhật SRM] Lỗi không mong muốn trong AppUpdateViewModel.", ex);
                SetStatus("Đã xảy ra lỗi không mong muốn khi kiểm tra cập nhật.", ErrorBrush);
            }
            finally
            {
                IsChecking = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ApplyResult(AppUpdateResult result)
        {
            if (result.Status != AppUpdateStatus.Cancelled)
            {
                LastCheckedText = $"Lần kiểm tra gần nhất: {result.CheckedAt:HH:mm dd/MM/yyyy}";
            }

            switch (result.Status)
            {
                case AppUpdateStatus.UpToDate:
                    if (result.LatestVersion != null && result.LatestVersion.CompareTo(result.CurrentVersion) < 0)
                    {
                        SetStatus($"Bản đang chạy (v{FormatVersion(result.CurrentVersion)}) mới hơn bản phát hành mới nhất trên GitHub (v{FormatVersion(result.LatestVersion)}). Có thể đây là bản đang phát triển.", OkBrush);
                    }
                    else
                    {
                        SetStatus($"Bạn đang dùng bản mới nhất (v{FormatVersion(result.CurrentVersion)}).", OkBrush);
                    }
                    break;

                case AppUpdateStatus.UpdateAvailable:
                    string latest = result.LatestVersion != null ? FormatVersion(result.LatestVersion) : (result.LatestTag ?? "?");
                    string preTag = result.IsPreRelease ? " (pre-release)" : string.Empty;
                    SetStatus($"Có bản mới: v{latest}{preTag}. Bạn đang dùng v{FormatVersion(result.CurrentVersion)}.", UpdateBrush);
                    _releaseUrl = result.ReleaseUrl;
                    _lastResult = result;
                    ReleaseNotesPreview = BuildNotesPreview(result.ReleaseNotes);
                    HasUpdate = true;
                    OnPropertyChanged(nameof(CanAutoDownload));
                    break;

                case AppUpdateStatus.Cancelled:
                    SetStatus(result.ErrorMessage ?? "Đã hủy kiểm tra cập nhật.", NeutralBrush);
                    break;

                default: // NetworkError, RateLimited, InvalidResponse
                    SetStatus(result.ErrorMessage ?? "Không kiểm tra được cập nhật. Vui lòng thử lại sau.", ErrorBrush);
                    break;
            }
        }

        // ================= TẢI VỀ + CÀI ĐẶT + KHỞI ĐỘNG LẠI =================
        private async Task DownloadAsync()
        {
            var update = _lastResult;
            if (update == null || !update.CanDownloadAutomatically || IsDownloading) return;

            // Kiểm tra trước: thư mục cài đặt có ghi được không (tránh tải xong mới báo lỗi)
            string? writableError = _installer.CheckInstallFolderWritable();
            if (writableError != null)
            {
                MessageBox.Show(writableError, "Không thể tự cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                return;
            }

            string latest = update.LatestVersion != null ? FormatVersion(update.LatestVersion) : (update.LatestTag ?? "?");

            var confirm = MessageBox.Show(
                $"Cập nhật ứng dụng lên v{latest}?\n\n" +
                $"1. Tải gói cập nhật ({FormatMb(update.AssetSize)}) về thư mục tạm và kiểm tra mã SHA256.\n" +
                "2. Sao lưu file cơ sở dữ liệu (library.db) vào thư mục Backups.\n" +
                "3. Thay các file chương trình. Dữ liệu của bạn (Database, Data, Library, Backups, Logs, cài đặt) KHÔNG bị đụng tới.\n" +
                "4. Xóa file đã tải và tự khởi động lại ứng dụng.\n\n" +
                "Nếu có lỗi, ứng dụng tự hoàn tác về bản cũ. Trong lúc cập nhật, vui lòng KHÔNG thao tác và KHÔNG tắt ứng dụng. Tiếp tục?",
                "Cập nhật ứng dụng",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes) return;

            IsDownloading = true;
            ShowDownloadPanel = true;
            DownloadProgressValue = 0;
            SetDownloadStatus("Đang chuẩn bị tải...", NeutralBrush);
            DownloadLocationText = "Tải về thư mục tạm: " + _downloader.StagingFolderPath;

            var lease = _busy.Begin("Cập nhật ứng dụng");
            bool restart = false;
            try
            {
                _acceptProgress = true;
                var progress = new Progress<AppUpdateProgress>(p => { if (_acceptProgress) OnDownloadProgress(p); });

                // 1) Tải + xác minh SHA256 + giải nén ra thư mục tạm
                var staged = await _downloader.DownloadAndStageAsync(update, progress);
                if (!staged.Success || string.IsNullOrEmpty(staged.StagedFolder))
                {
                    _acceptProgress = false;
                    SetDownloadStatus(staged.ErrorMessage ?? "Không tải được bản cập nhật.", ErrorBrush);
                    return;
                }

                // 2) Cài đặt: sao lưu DB, thay file (file chính thay sau cùng), tự hoàn tác khi lỗi
                var install = await _installer.InstallAsync(staged.StagedFolder, progress);
                if (!install.Success)
                {
                    _acceptProgress = false;
                    SetDownloadStatus(install.ErrorMessage ?? "Cài đặt bản cập nhật thất bại.", ErrorBrush);
                    return;
                }

                _acceptProgress = false;
                DownloadProgressValue = 100;
                SetDownloadStatus(
                    $"Cập nhật thành công lên v{latest}! (thay {install.FilesReplaced} file, thêm {install.FilesAdded}, giữ nguyên {install.FilesUnchanged}; dữ liệu của bạn không bị đụng tới). " +
                    "Ứng dụng sẽ tự khởi động lại sau vài giây, vui lòng chờ...", OkBrush);
                restart = true;
            }
            catch (Exception ex) // các dịch vụ không ném lỗi, đây chỉ là lưới an toàn
            {
                _acceptProgress = false;
                _logService.LogError("[Cập nhật SRM] Lỗi không mong muốn khi cập nhật ứng dụng.", ex);
                SetDownloadStatus("Đã xảy ra lỗi không mong muốn khi cập nhật ứng dụng.", ErrorBrush);
            }
            finally
            {
                _acceptProgress = false;

                // Luôn xóa file đã tải và thư mục giải nén (dù thành công hay thất bại) để không để lại rác
                _downloader.CleanupStaging();

                string staging = _downloader.StagingFolderPath;
                DownloadLocationText = Directory.Exists(staging)
                    ? "Lưu ý: chưa xóa hết được thư mục tạm, bạn có thể tự xóa: " + staging
                    : "Đã xóa file tải về và thư mục tạm: " + staging;

                if (!restart)
                {
                    IsDownloading = false;
                    lease.Dispose();
                    CommandManager.InvalidateRequerySuggested();
                }
            }

            if (restart)
            {
                // Giữ nút bị khóa trong lúc chờ để không ai bấm lần nữa; cho người dùng kịp đọc thông báo
                await Task.Delay(2500);
                lease.Dispose(); // nhả khóa để cửa sổ chính không hỏi "tiến trình đang chạy" khi thoát
                RestartApplication();
            }
        }

        private void RestartApplication()
        {
            try
            {
                string exe = _installer.MainExePath;
                if (!File.Exists(exe))
                {
                    SetDownloadStatus("Cập nhật xong nhưng không tìm thấy file chương trình để mở lại. Hãy đóng và tự mở lại ứng dụng.", UpdateBrush);
                    IsDownloading = false;
                    return;
                }

                // Mở bản mới rồi thoát bản cũ (file cũ đã được đổi tên .srm-old và sẽ được dọn khi bản mới chạy)
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = _installer.InstallFolderPath });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logService.LogError("[Cập nhật SRM] Không tự mở lại được ứng dụng sau khi cập nhật.", ex);
                SetDownloadStatus("Cập nhật xong nhưng không tự mở lại được. Hãy đóng và mở lại ứng dụng để dùng bản mới.", UpdateBrush);
                IsDownloading = false;
            }
        }

        private void OnDownloadProgress(AppUpdateProgress p)
        {
            string percent = p.Percent.ToString("0", Vi);

            switch (p.Phase)
            {
                case AppUpdatePhase.Downloading:
                    DownloadProgressValue = p.Percent;
                    if (p.StalledSeconds > 0)
                    {
                        // Không nhận được dữ liệu: báo rõ và đếm giây cho tới khi tự hủy
                        DownloadStatusBrush = UpdateBrush;
                        DownloadStatusText = $"Đã {percent}% - không nhận được dữ liệu từ máy chủ trong {p.StalledSeconds} giây. Hãy kiểm tra kết nối mạng. Tự hủy sau {p.StallLimitSeconds} giây nếu vẫn không có dữ liệu...";
                    }
                    else
                    {
                        DownloadStatusBrush = NeutralBrush;
                        DownloadStatusText = $"Đang tải: {percent}%  ({FormatMb(p.BytesDone)} / {FormatMb(p.BytesTotal)})  -  {FormatSpeed(p.BytesPerSecond)}";
                    }
                    break;

                case AppUpdatePhase.Verifying:
                    DownloadProgressValue = 100;
                    DownloadStatusText = "Đang kiểm tra mã SHA256 của file đã tải...";
                    break;

                case AppUpdatePhase.Extracting:
                    DownloadProgressValue = p.Percent;
                    DownloadStatusText = $"Đang giải nén và kiểm tra gói: {percent}%";
                    break;

                case AppUpdatePhase.BackingUp:
                    DownloadProgressValue = 0;
                    DownloadStatusText = "Đang sao lưu file cơ sở dữ liệu (library.db) vào thư mục Backups...";
                    break;

                case AppUpdatePhase.Installing:
                    DownloadProgressValue = p.Percent;
                    DownloadStatusText = $"Đang cài đặt bản mới: {percent}% ({p.BytesDone}/{p.BytesTotal} file). Vui lòng KHÔNG tắt ứng dụng...";
                    break;
            }
        }

        // ================= MỞ TRANG RELEASE =================
        private void OpenReleasePage()
        {
            string? url = _releaseUrl;

            // Chỉ mở link thuộc repo SRM trên github.com
            if (!AppSelfUpdateChecker.IsTrustedReleaseUrl(url))
            {
                _dialogService.ShowMessage("Không có đường dẫn hợp lệ để mở trang tải về.", "Thông báo");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logService.LogError("[Cập nhật SRM] Không mở được trang Release.", ex);
                _dialogService.ShowMessage($"Không mở được trình duyệt. Bạn có thể tự mở đường dẫn sau:\n{url}", "Thông báo");
            }
        }

        // ================= HÀM HỖ TRỢ =================
        private void SetStatus(string text, Brush brush)
        {
            StatusText = text;
            StatusBrush = brush;
        }

        private void SetDownloadStatus(string text, Brush brush)
        {
            DownloadStatusText = text;
            DownloadStatusBrush = brush;
        }

        // "1.0.0.0" -> "1.0.0", "1.2.3.4" -> "1.2.3.4"
        private static string FormatVersion(Version v)
        {
            return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
        }

        private static string FormatMb(long bytes)
        {
            return (bytes / 1048576.0).ToString("0.0", Vi) + " MB";
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1048576.0)
                return (bytesPerSecond / 1048576.0).ToString("0.0", Vi) + " MB/s";

            return (bytesPerSecond / 1024.0).ToString("0", Vi) + " KB/s";
        }

        // Ghi chú phát hành: thuần văn bản, cắt khoảng 500 ký tự, không render Markdown
        private static string BuildNotesPreview(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) return string.Empty;

            string text = notes.Replace("\r\n", "\n").Trim();
            if (text.Length <= MaxNotesLength) return text;

            int length = MaxNotesLength;
            if (char.IsHighSurrogate(text[length - 1])) length--; // không cắt đôi một ký tự emoji
            return text.Substring(0, length).TrimEnd() + "…";
        }

        private static Brush CreateBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
