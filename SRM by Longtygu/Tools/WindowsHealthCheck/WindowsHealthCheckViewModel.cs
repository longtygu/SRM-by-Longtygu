using SRM_by_Longtygu.Commands;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SRM_by_Longtygu.Tools.WindowsHealthCheck
{
    public class WindowsHealthCheckViewModel : ViewModels.ViewModelBase
    {
        private bool _isBusy;
        private CancellationTokenSource _repairCts;
        private readonly StringBuilder _logBuilder = new StringBuilder();

        public ObservableCollection<HealthCheckItem> HealthItems { get; } = new ObservableCollection<HealthCheckItem>();
        public ObservableCollection<string> AvailableDrives { get; } = new ObservableCollection<string>();

        public bool IsAdministrator => WindowsHealthCheckHelper.IsAdministrator();

        private string _selectedChkdskDrive;
        public string SelectedChkdskDrive
        {
            get => _selectedChkdskDrive;
            set => SetProperty(ref _selectedChkdskDrive, value);
        }

        private string _statusText = "Sẵn sàng";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isCheckingStatus;
        public bool IsCheckingStatus
        {
            get => _isCheckingStatus;
            set => SetProperty(ref _isCheckingStatus, value);
        }

        private bool _isRepairRunning;
        public bool IsRepairRunning
        {
            get => _isRepairRunning;
            set => SetProperty(ref _isRepairRunning, value);
        }

        private string _currentRepairName = "";
        public string CurrentRepairName
        {
            get => _currentRepairName;
            set => SetProperty(ref _currentRepairName, value);
        }

        private string _logText = "";
        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }

        public ICommand RefreshStatusCommand { get; }
        public ICommand RunDismCommand { get; }
        public ICommand RunSfcCommand { get; }
        public ICommand RunChkdskScanCommand { get; }
        public ICommand RunChkdskFixCommand { get; }
        public ICommand ResetNetworkCommand { get; }
        public ICommand FlushDnsCommand { get; }
        public ICommand RepairWindowsUpdateCommand { get; }
        public ICommand CancelRepairCommand { get; }
        public ICommand ClearLogCommand { get; }

        public WindowsHealthCheckViewModel()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    AvailableDrives.Add(drive.Name);
            }
            SelectedChkdskDrive = AvailableDrives.FirstOrDefault();

            RefreshStatusCommand = new RelayCommand(async _ => await RefreshStatusAsync());

            RunDismCommand = new RelayCommand(async _ => await RunRepairAsync(
                "DISM - Sửa chữa Windows Image (RestoreHealth)",
                "dism.exe", "/Online /Cleanup-Image /RestoreHealth"));

            RunSfcCommand = new RelayCommand(async _ => await RunRepairAsync(
                "SFC - Kiểm tra file hệ thống (/scannow)",
                "sfc.exe", "/scannow"));

            RunChkdskScanCommand = new RelayCommand(async _ => await RunChkdskAsync(fix: false));
            RunChkdskFixCommand = new RelayCommand(async _ => await RunChkdskAsync(fix: true));

            ResetNetworkCommand = new RelayCommand(async _ => await RunRepairAsync(
                "Reset Network - Đặt lại Winsock & TCP/IP",
                "cmd.exe", "/c \"netsh winsock reset && netsh int ip reset\""));

            FlushDnsCommand = new RelayCommand(async _ => await RunRepairAsync(
                "Flush DNS - Xóa cache DNS",
                "ipconfig.exe", "/flushdns"));

            RepairWindowsUpdateCommand = new RelayCommand(async _ => await RunRepairAsync(
                "Repair Windows Update - Dọn cache & sửa lỗi cập nhật",
                "cmd.exe", "/c \"" + WindowsHealthCheckHelper.RepairWindowsUpdateScript + "\""));

            CancelRepairCommand = new RelayCommand(_ => _repairCts?.Cancel());
            ClearLogCommand = new RelayCommand(_ =>
            {
                _logBuilder.Clear();
                LogText = "";
            });

            _ = RefreshStatusAsync();
        }

        private async Task RefreshStatusAsync()
        {
            if (_isBusy) return;
            _isBusy = true;
            IsCheckingStatus = true;
            StatusText = "Đang kiểm tra tình trạng hệ thống...";
            HealthItems.Clear();

            var items = await Task.Run(() => WindowsHealthCheckHelper.GetAllHealthChecks());
            foreach (var item in items) HealthItems.Add(item);

            StatusText = "Sẵn sàng";
            IsCheckingStatus = false;
            _isBusy = false;
        }

        private async Task RunChkdskAsync(bool fix)
        {
            if (SelectedChkdskDrive == null) return;
            var drive = SelectedChkdskDrive.TrimEnd('\\');

            if (fix)
            {
                await RunRepairAsync(
                    $"CHKDSK /F /R - Quét và sửa lỗi ổ {drive}",
                    "chkdsk.exe", $"{drive} /f /r",
                    autoConfirmYes: true);
            }
            else
            {
                await RunRepairAsync(
                    $"CHKDSK /SCAN - Quét lỗi ổ {drive} (chỉ đọc)",
                    "chkdsk.exe", $"{drive} /scan");
            }
        }

        private async Task RunRepairAsync(string displayName, string fileName, string arguments, bool autoConfirmYes = false)
        {
            // Chỉ cho chạy 1 tác vụ sửa lỗi tại 1 thời điểm — nhiều lệnh (SFC/DISM/CHKDSK)
            // đụng chung tài nguyên hệ thống, chạy song song dễ xung đột hoặc báo lỗi sai.
            if (IsRepairRunning) return;

            IsRepairRunning = true;
            CurrentRepairName = displayName;

            AppendLog($"===== Bắt đầu: {displayName} =====");

            _repairCts = new CancellationTokenSource();
            var progress = new Progress<string>(AppendLog);

            try
            {
                var exitCode = await WindowsHealthCheckHelper.RunRepairCommandAsync(
                    fileName, arguments, progress, _repairCts.Token, autoConfirmYes);
                AppendLog($"===== Hoàn tất: {displayName} (mã thoát: {exitCode}) =====");
            }
            catch (OperationCanceledException)
            {
                AppendLog($"===== Đã hủy: {displayName} =====");
            }
            catch (Exception ex)
            {
                AppendLog($"===== Lỗi: {displayName} - {ex.Message} =====");
            }
            finally
            {
                IsRepairRunning = false;
                CurrentRepairName = "";
            }
        }

        private void AppendLog(string line)
        {
            _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line}");
            LogText = _logBuilder.ToString();
        }
    }
}
