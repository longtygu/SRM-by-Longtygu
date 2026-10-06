using System;
using System.Diagnostics;
using System.Threading.Tasks;
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
    /// Chỉ BÁO có bản mới và mở trang Release, không tự tải/thay file.
    /// </summary>
    public class AppUpdateViewModel : ViewModelBase
    {
        private const int MaxNotesLength = 500;

        private static readonly Brush NeutralBrush = CreateBrush(0xAA, 0xAA, 0xAA);
        private static readonly Brush OkBrush = CreateBrush(0x6C, 0xCB, 0x5F);
        private static readonly Brush UpdateBrush = CreateBrush(0xFB, 0xBF, 0x24);
        private static readonly Brush ErrorBrush = CreateBrush(0xF8, 0x71, 0x71);

        private readonly IAppSelfUpdateChecker _checker;
        private readonly ILogService _logService;
        private readonly IDialogService _dialogService;

        private string? _releaseUrl;

        // ================= THUỘC TÍNH HIỂN THỊ =================
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
        public bool HasUpdate { get => _hasUpdate; private set => SetProperty(ref _hasUpdate, value); }

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

        // ================= LỆNH =================
        public ICommand CheckForUpdateCommand { get; }
        public ICommand OpenReleasePageCommand { get; }

        public AppUpdateViewModel(IAppSelfUpdateChecker checker, ILogService logService, IDialogService dialogService)
        {
            _checker = checker;
            _logService = logService;
            _dialogService = dialogService;

            CurrentVersionText = "v" + FormatVersion(_checker.GetCurrentVersion());

            CheckForUpdateCommand = new RelayCommand(async _ => await CheckAsync(), _ => !IsChecking);
            OpenReleasePageCommand = new RelayCommand(_ => OpenReleasePage(), _ => HasUpdate);
        }

        // ================= KIỂM TRA =================
        private async Task CheckAsync()
        {
            if (IsChecking) return;

            IsChecking = true;
            HasUpdate = false;
            ReleaseNotesPreview = string.Empty;
            _releaseUrl = null;
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
                    ReleaseNotesPreview = BuildNotesPreview(result.ReleaseNotes);
                    HasUpdate = true;
                    break;

                case AppUpdateStatus.Cancelled:
                    SetStatus(result.ErrorMessage ?? "Đã hủy kiểm tra cập nhật.", NeutralBrush);
                    break;

                default: // NetworkError, RateLimited, InvalidResponse
                    SetStatus(result.ErrorMessage ?? "Không kiểm tra được cập nhật. Vui lòng thử lại sau.", ErrorBrush);
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

        // "1.0.0.0" -> "1.0.0", "1.2.3.4" -> "1.2.3.4"
        private static string FormatVersion(Version v)
        {
            return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
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
