using SRM_by_Longtygu.Commands;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SRM_by_Longtygu.Tools.DiskHealth
{
    public class DiskHealthViewModel : ViewModels.ViewModelBase
    {
        private bool _isBusy;
        private CancellationTokenSource _cts;

        public ObservableCollection<string> AvailableDrives { get; } = new ObservableCollection<string>();
        public ObservableCollection<DiskHealthInfo> PhysicalDisks { get; } = new ObservableCollection<DiskHealthInfo>();

        private string _selectedDrive;
        public string SelectedDrive
        {
            get => _selectedDrive;
            set => SetProperty(ref _selectedDrive, value);
        }

        private string _statusText = "Sẵn sàng";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private int _benchmarkPercent;
        public int BenchmarkPercent
        {
            get => _benchmarkPercent;
            set => SetProperty(ref _benchmarkPercent, value);
        }

        private bool _isBenchmarking;
        public bool IsBenchmarking
        {
            get => _isBenchmarking;
            set => SetProperty(ref _isBenchmarking, value);
        }

        private string _writeSpeedResult = "--";
        public string WriteSpeedResult
        {
            get => _writeSpeedResult;
            set => SetProperty(ref _writeSpeedResult, value);
        }

        private string _readSpeedResult = "--";
        public string ReadSpeedResult
        {
            get => _readSpeedResult;
            set => SetProperty(ref _readSpeedResult, value);
        }

        public ICommand RunBenchmarkCommand { get; }
        public ICommand CancelBenchmarkCommand { get; }
        public ICommand RefreshHealthCommand { get; }

        public DiskHealthViewModel()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady) AvailableDrives.Add(drive.Name);
            }
            SelectedDrive = AvailableDrives.FirstOrDefault();

            RunBenchmarkCommand = new RelayCommand(async _ => await RunBenchmarkAsync());
            CancelBenchmarkCommand = new RelayCommand(_ => _cts?.Cancel());
            RefreshHealthCommand = new RelayCommand(async _ => await RefreshHealthAsync());

            _ = RefreshHealthAsync();
        }

        private async Task RunBenchmarkAsync()
        {
            if (_isBusy || SelectedDrive == null) return;
            _isBusy = true;
            IsBenchmarking = true;

            WriteSpeedResult = "--";
            ReadSpeedResult = "--";
            BenchmarkPercent = 0;

            _cts = new CancellationTokenSource();
            var progress = new Progress<BenchmarkProgress>(p =>
            {
                StatusText = p.StatusText;
                BenchmarkPercent = p.PercentComplete;
            });

            var result = await DiskInfoHelper.RunBenchmarkAsync(SelectedDrive, progress, _cts.Token);

            if (result.Cancelled)
            {
                StatusText = "Đã hủy kiểm tra";
            }
            else if (result.Success)
            {
                WriteSpeedResult = $"{result.WriteSpeedMBps:F1} MB/s";
                ReadSpeedResult = $"{result.ReadSpeedMBps:F1} MB/s";
                StatusText = "Hoàn tất kiểm tra tốc độ";
                BenchmarkPercent = 100;
            }
            else
            {
                StatusText = $"Lỗi: {result.ErrorMessage}";
            }

            IsBenchmarking = false;
            _isBusy = false;
        }

        private async Task RefreshHealthAsync()
        {
            if (_isBusy) return;
            _isBusy = true;

            StatusText = "Đang đọc thông tin ổ đĩa...";
            PhysicalDisks.Clear();

            var disks = await Task.Run(() => DiskInfoHelper.GetPhysicalDisksHealth());
            foreach (var d in disks) PhysicalDisks.Add(d);

            StatusText = "Sẵn sàng";
            _isBusy = false;
        }
    }
}