using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Helpers;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services;
using SRM_by_Longtygu.Services.UpdateChecking;

namespace SRM_by_Longtygu.ViewModels
{
    // Trang riêng biệt "Trung tâm cập nhật" - KHÔNG đụng gì tới LibraryViewModel.
    // Chỉ tập trung vào 1 việc: hiển thị trạng thái cập nhật của toàn bộ thư viện,
    // cho phép cấu hình nguồn, quét, tải về, cài đặt.
    public class UpdateCenterViewModel : ViewModelBase
    {
        private readonly ISoftwareRepository _softwareRepo;
        private readonly ISoftwareVersionRepository _versionRepo;
        private readonly IDialogService _dialogService;
        private readonly IUpdateCheckService _updateCheckService;
        private readonly IDeploymentService _deploymentService;

        public ObservableCollection<Software> Softwares { get; } = new ObservableCollection<Software>();
        public ICollectionView SoftwareView { get; }

        private Software _selectedSoftware;
        public Software SelectedSoftware
        {
            get => _selectedSoftware;
            set => SetProperty(ref _selectedSoftware, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    SoftwareView.Refresh();
            }
        }

        // Bộ lọc nhanh: "All" | "Configured" | "UpdateAvailable" | "NotConfigured"
        private string _filterMode = "All";
        public string FilterMode
        {
            get => _filterMode;
            set
            {
                if (SetProperty(ref _filterMode, value))
                    SoftwareView.Refresh();
            }
        }

        private bool _isCheckingUpdates;
        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            set => SetProperty(ref _isCheckingUpdates, value);
        }

        private int _updateCheckProgress;
        public int UpdateCheckProgress
        {
            get => _updateCheckProgress;
            set => SetProperty(ref _updateCheckProgress, value);
        }

        private DateTime? _lastScanTime;
        public DateTime? LastScanTime
        {
            get => _lastScanTime;
            set
            {
                if (SetProperty(ref _lastScanTime, value))
                    OnPropertyChanged(nameof(LastScanDisplayText));
            }
        }

        // Chuỗi hiển thị sẵn cho header, tránh phải viết converter riêng trong XAML
        public string LastScanDisplayText => _lastScanTime.HasValue
            ? $"Lần quét gần nhất: {_lastScanTime:dd/MM/yyyy HH:mm}"
            : "Chưa quét lần nào";

        // ===== Thẻ thống kê hiển thị trên đầu trang =====
        public int TotalSoftwareCount => Softwares.Count;
        public int ConfiguredCount => Softwares.Count(s => s.HasUpdateSourceConfigured);
        public int UpdateAvailableCount => Softwares.Count(s => s.HasUpdateAvailable);
        public int NotConfiguredCount => Softwares.Count(s => !s.HasUpdateSourceConfigured);

        // MỚI: Bật/tắt nhóm danh sách theo Category - mặc định BẬT vì rất cần khi thư viện có nhiều phần mềm
        private bool _groupByCategory = true;
        public bool GroupByCategory
        {
            get => _groupByCategory;
            set
            {
                if (SetProperty(ref _groupByCategory, value))
                    ApplyGrouping();
            }
        }

        // MỚI: Theo dõi trạng thái nút "Cập nhật toàn bộ"
        private bool _isUpdatingAll;
        public bool IsUpdatingAll
        {
            get => _isUpdatingAll;
            set => SetProperty(ref _isUpdatingAll, value);
        }

        private string _updateAllStatusText;
        public string UpdateAllStatusText
        {
            get => _updateAllStatusText;
            set => SetProperty(ref _updateAllStatusText, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand CheckAllUpdatesCommand { get; }
        public ICommand CheckOneUpdateCommand { get; }
        public ICommand ConfigureUpdateSourceCommand { get; }
        public ICommand DownloadUpdateCommand { get; }
        public ICommand InstallUpdateNowCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand AutoConfigureAllCommand { get; } // MỚI: tự động áp catalog gợi ý cho cả thư viện cùng lúc
        public ICommand ShowGuideCommand { get; } // MỚI: mở lại hướng dẫn bất cứ lúc nào
        public ICommand UpdateAllCommand { get; } // MỚI: tải tất cả bản cập nhật đang có về thư viện cùng lúc

        public UpdateCenterViewModel(
            ISoftwareRepository softwareRepo,
            ISoftwareVersionRepository versionRepo,
            IDialogService dialogService,
            IUpdateCheckService updateCheckService,
            IDeploymentService deploymentService)
        {
            _softwareRepo = softwareRepo;
            _versionRepo = versionRepo;
            _dialogService = dialogService;
            _updateCheckService = updateCheckService;
            _deploymentService = deploymentService;

            RefreshCommand = new RelayCommand(_ => _ = LoadDataAsync());
            CheckAllUpdatesCommand = new RelayCommand(_ => _ = ExecuteCheckAllUpdatesAsync(), _ => !IsCheckingUpdates);
            CheckOneUpdateCommand = new RelayCommand(p => _ = ExecuteCheckOneAsync(p as Software));
            ConfigureUpdateSourceCommand = new RelayCommand(ExecuteConfigureUpdateSource);
            DownloadUpdateCommand = new RelayCommand(p => _ = ExecuteDownloadUpdateAsync(p as Software));
            InstallUpdateNowCommand = new RelayCommand(p => _ = ExecuteInstallUpdateNowAsync(p as Software));
            SetFilterCommand = new RelayCommand(p => FilterMode = p as string ?? "All");
            AutoConfigureAllCommand = new RelayCommand(_ => _ = ExecuteAutoConfigureAllAsync(), _ => !IsCheckingUpdates);
            ShowGuideCommand = new RelayCommand(_ => _dialogService.ShowUpdateCenterGuideDialog());
            UpdateAllCommand = new RelayCommand(_ => _ = ExecuteUpdateAllAsync(), _ => !IsUpdatingAll && !IsCheckingUpdates);

            SoftwareView = CollectionViewSource.GetDefaultView(Softwares);
            SoftwareView.Filter = FilterPredicate;
            ApplyGrouping();

            _ = LoadDataAsync();
        }

        private void ApplyGrouping()
        {
            SoftwareView.GroupDescriptions.Clear();
            if (GroupByCategory)
                SoftwareView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Software.CategoryDisplay)));

            SoftwareView.SortDescriptions.Clear();
            if (GroupByCategory)
                SoftwareView.SortDescriptions.Add(new SortDescription(nameof(Software.CategoryDisplay), ListSortDirection.Ascending));
            SoftwareView.SortDescriptions.Add(new SortDescription(nameof(Software.Name), ListSortDirection.Ascending));
        }

        private bool FilterPredicate(object obj)
        {
            if (obj is not Software s) return false;

            bool matchesSearch = string.IsNullOrWhiteSpace(SearchText)
                || (s.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.Publisher?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);

            bool matchesFilter = FilterMode switch
            {
                "Configured" => s.HasUpdateSourceConfigured,
                "UpdateAvailable" => s.HasUpdateAvailable,
                "NotConfigured" => !s.HasUpdateSourceConfigured,
                _ => true
            };

            return matchesSearch && matchesFilter;
        }

        private async Task LoadDataAsync()
        {
            var data = await _softwareRepo.GetAllAsync();
            Application.Current.Dispatcher.Invoke(() => Softwares.Clear());

            foreach (var item in data)
            {
                var versions = await _versionRepo.GetBySoftwareIdAsync(item.Id);
                item.Versions = new ObservableCollection<SoftwareVersion>(versions);

                Application.Current.Dispatcher.Invoke(() => Softwares.Add(item));
            }

            RaiseStatsChanged();
        }

        private void RaiseStatsChanged()
        {
            OnPropertyChanged(nameof(TotalSoftwareCount));
            OnPropertyChanged(nameof(ConfiguredCount));
            OnPropertyChanged(nameof(UpdateAvailableCount));
            OnPropertyChanged(nameof(NotConfiguredCount));
        }

        // Quét TOÀN BỘ phần mềm đã cấu hình nguồn
        private async Task ExecuteCheckAllUpdatesAsync()
        {
            var candidates = Softwares.Where(s => s.HasUpdateSourceConfigured).ToList();
            if (candidates.Count == 0)
            {
                _dialogService.ShowMessage(
                    "Chưa có phần mềm nào được cấu hình nguồn kiểm tra cập nhật.\n" +
                    "Bấm icon ⚙ trên từng phần mềm để thiết lập nguồn (GitHub Releases hoặc quét trang web).",
                    "Chưa có gì để quét");
                return;
            }

            IsCheckingUpdates = true;
            UpdateCheckProgress = 0;
            int done = 0;
            int foundCount = 0;

            try
            {
                await _updateCheckService.CheckAllAsync(candidates, (software, result) =>
                {
                    done++;
                    if (result.Success) System.Threading.Interlocked.Increment(ref foundCount);
                    Application.Current.Dispatcher.Invoke(() =>
                        UpdateCheckProgress = (int)(done * 100.0 / candidates.Count));
                });

                await LoadDataAsync();
                LastScanTime = DateTime.Now;

                int updatesAvailable = Softwares.Count(s => s.HasUpdateAvailable);
                _dialogService.ShowMessage(
                    $"Đã kiểm tra xong {candidates.Count} phần mềm ({foundCount} quét thành công).\n" +
                    (updatesAvailable > 0
                        ? $"Tìm thấy {updatesAvailable} phần mềm có bản cập nhật mới."
                        : "Tất cả phần mềm đều đang ở phiên bản mới nhất."),
                    "Hoàn tất kiểm tra cập nhật");
            }
            finally
            {
                IsCheckingUpdates = false;
                UpdateCheckProgress = 0;
            }
        }

        // Quét 1 phần mềm duy nhất (nút nhỏ trên từng dòng)
        private async Task ExecuteCheckOneAsync(Software software)
        {
            if (software == null) return;
            if (!software.HasUpdateSourceConfigured)
            {
                _dialogService.ShowMessage("Phần mềm này chưa được cấu hình nguồn kiểm tra cập nhật.", "Thiếu cấu hình");
                return;
            }

            var result = await _updateCheckService.CheckOneAsync(software);
            await LoadDataAsync();

            _dialogService.ShowMessage(
                result.Success
                    ? $"'{software.Name}': phiên bản mới nhất tìm thấy là {result.LatestVersion}."
                    : $"Kiểm tra '{software.Name}' thất bại: {result.ErrorMessage}",
                result.Success ? "Kết quả kiểm tra" : "Lỗi");
        }

        private void ExecuteConfigureUpdateSource(object parameter)
        {
            if (parameter is not Software software) return;
            _dialogService.ShowConfigureUpdateSourceDialog(software);
            _ = LoadDataAsync();
        }

        // Hướng 2: Tải bản mới về và THÊM VÀO THƯ VIỆN (Data/ + DB), không đụng gì tới máy đang chạy app
        private async Task ExecuteDownloadUpdateAsync(Software software)
        {
            if (software == null) return;

            if (!software.HasUpdateSourceConfigured)
            {
                _dialogService.ShowMessage("Phần mềm này chưa được cấu hình nguồn kiểm tra cập nhật.", "Thiếu cấu hình");
                return;
            }

            var checkResult = await _updateCheckService.CheckOneAsync(software);
            if (!checkResult.Success)
            {
                _dialogService.ShowMessage($"Kiểm tra cập nhật thất bại: {checkResult.ErrorMessage}", "Lỗi");
                return;
            }
            if (string.IsNullOrWhiteSpace(checkResult.DownloadUrl))
            {
                string reason = software.UpdateSourceType == "Winget"
                    ? $"Tìm thấy phiên bản {checkResult.LatestVersion} qua Winget, nhưng Winget không lộ link tải trực tiếp cho gói này " +
                      "(thường do cài qua MSIX/Store). Dùng nút 'Cài ngay' để Winget tự tải+cài thẳng lên máy " +
                      "(sẽ KHÔNG thêm được bản này vào thư viện Data/)."
                    : $"Tìm thấy phiên bản {checkResult.LatestVersion} nhưng nguồn này không cung cấp link tải trực tiếp.\n" +
                      "Hãy tải thủ công rồi thêm bằng nút 'Thêm phiên bản' trong Kho phần mềm.";

                _dialogService.ShowMessage(reason, "Không có link tải tự động");
                return;
            }

            software.IsDownloadingUpdate = true;
            software.DownloadProgressPercent = 0;
            software.DownloadedBytes = 0;
            software.DownloadTotalBytes = 0;
            software.DownloadSpeedBytesPerSecond = 0;
            try
            {
                // Progress<T> tự bắt SynchronizationContext của UI thread tại thời điểm khởi tạo này
                // (đang chạy trong tay cầm command trên UI thread) -> callback bên dưới tự động chạy lại
                // trên UI thread, gán thẳng vào các property của software là an toàn, không cần Dispatcher.Invoke.
                var progress = new Progress<DownloadProgressInfo>(info =>
                {
                    software.DownloadProgressPercent = info.PercentComplete;
                    software.DownloadedBytes = info.BytesDownloaded;
                    software.DownloadTotalBytes = info.TotalBytes;
                    software.DownloadSpeedBytesPerSecond = info.SpeedBytesPerSecond;
                });

                bool ok = await _updateCheckService.DownloadAndAddVersionAsync(software, checkResult.DownloadUrl, checkResult.LatestVersion, progress);
                await LoadDataAsync();

                _dialogService.ShowMessage(
                    ok ? $"Đã tải và thêm phiên bản {checkResult.LatestVersion} của '{software.Name}' vào thư viện."
                       : "Tải hoặc thêm phiên bản mới thất bại. Xem chi tiết trong Nhật ký hoạt động.",
                    ok ? "Thành công" : "Lỗi");
            }
            finally
            {
                software.IsDownloadingUpdate = false;
                software.DownloadProgressPercent = 0;
                software.DownloadedBytes = 0;
                software.DownloadTotalBytes = 0;
                software.DownloadSpeedBytesPerSecond = 0;
            }
        }

        // Hướng 1: Tải bản mới, thêm vào thư viện, rồi cài luôn lên máy đang chạy app
        private async Task ExecuteInstallUpdateNowAsync(Software software)
        {
            if (software == null) return;

            if (!software.HasUpdateSourceConfigured)
            {
                _dialogService.ShowMessage("Phần mềm này chưa được cấu hình nguồn kiểm tra cập nhật.", "Thiếu cấu hình");
                return;
            }

            // MỚI: Nguồn Winget đi đường riêng - để chính Winget lo cả tải lẫn cài (đáng tin cậy hơn,
            // dùng đúng hạ tầng Microsoft), KHÔNG qua downloader tự viết của SRM.
            if (software.UpdateSourceType == "Winget")
            {
                var wingetCheck = await _updateCheckService.CheckOneAsync(software);
                if (!wingetCheck.Success)
                {
                    _dialogService.ShowMessage($"Kiểm tra cập nhật thất bại: {wingetCheck.ErrorMessage}", "Lỗi");
                    return;
                }

                // Winget không lộ % tiến trình thật qua cách gọi Process hiện tại -> dùng -1 để UI hiện
                // icon xoay (indeterminate) thay vì thanh phần trăm không có số liệu thật.
                software.IsDownloadingUpdate = true;
                software.DownloadProgressPercent = -1;
                try
                {
                    bool wingetOk = await WingetSource.UpgradeNowAsync(software.UpdateSourceUrl);
                    _dialogService.ShowMessage(
                        wingetOk
                            ? $"Đã cài đặt {software.Name} phiên bản {wingetCheck.LatestVersion} lên máy này qua Winget.\n" +
                              "(Lưu ý: bản này KHÔNG được thêm vào thư viện Data/ vì Winget tự quản lý cache installer riêng.)"
                            : "Cài đặt qua Winget thất bại. Kiểm tra máy đã cài Winget và có kết nối mạng, hoặc thử lại.",
                        wingetOk ? "Cài đặt thành công" : "Lỗi");
                }
                finally
                {
                    software.IsDownloadingUpdate = false;
                    software.DownloadProgressPercent = 0;
                }
                return;
            }

            var checkResult = await _updateCheckService.CheckOneAsync(software);
            if (!checkResult.Success || string.IsNullOrWhiteSpace(checkResult.DownloadUrl))
            {
                _dialogService.ShowMessage(
                    $"Không lấy được link tải tự động: {checkResult.ErrorMessage ?? "Nguồn không cung cấp link tải trực tiếp."}",
                    "Lỗi");
                return;
            }

            software.IsDownloadingUpdate = true;
            software.DownloadProgressPercent = 0;
            software.DownloadedBytes = 0;
            software.DownloadTotalBytes = 0;
            software.DownloadSpeedBytesPerSecond = 0;
            bool added;
            try
            {
                var progress = new Progress<DownloadProgressInfo>(info =>
                {
                    software.DownloadProgressPercent = info.PercentComplete;
                    software.DownloadedBytes = info.BytesDownloaded;
                    software.DownloadTotalBytes = info.TotalBytes;
                    software.DownloadSpeedBytesPerSecond = info.SpeedBytesPerSecond;
                });
                added = await _updateCheckService.DownloadAndAddVersionAsync(software, checkResult.DownloadUrl, checkResult.LatestVersion, progress);
            }
            finally
            {
                software.IsDownloadingUpdate = false;
                software.DownloadProgressPercent = 0;
                software.DownloadedBytes = 0;
                software.DownloadTotalBytes = 0;
                software.DownloadSpeedBytesPerSecond = 0;
            }

            if (!added)
            {
                _dialogService.ShowMessage("Tải bản cập nhật thất bại, không thể cài đặt.", "Lỗi");
                return;
            }

            await LoadDataAsync();

            var refreshedSoftware = Softwares.FirstOrDefault(s => s.Id == software.Id);
            var newVersion = refreshedSoftware?.Versions.OrderByDescending(v => v.Id).FirstOrDefault();
            if (newVersion == null)
            {
                _dialogService.ShowMessage("Đã thêm vào thư viện nhưng không tìm lại được phiên bản vừa tải để cài.", "Lỗi");
                return;
            }

            bool installOk = await _deploymentService.RunSilentInstallAsync(newVersion.FilePath, software.SilentInstallCommand);

            _dialogService.ShowMessage(
                installOk
                    ? $"Đã cài đặt {software.Name} phiên bản {newVersion.Version} lên máy."
                    : $"Đã thêm phiên bản {newVersion.Version} vào thư viện, nhưng cài đặt tự động thất bại " +
                      "(có thể do thiếu switch silent-install). Bạn có thể cài thủ công từ Kho phần mềm.",
                installOk ? "Cài đặt thành công" : "Cần cài thủ công");
        }
        // MỚI: Tải TẤT CẢ bản cập nhật đang có (HasUpdateAvailable) về thư viện cùng lúc.
        // Chỉ thêm vào Data/ + DB, KHÔNG đụng gì tới máy đang chạy app (giống hệt "Tải về" của từng dòng,
        // chỉ khác là chạy cho toàn bộ danh sách 1 lần thay vì bấm tay từng cái).
        private async Task ExecuteUpdateAllAsync()
        {
            var candidates = Softwares.Where(s => s.HasUpdateAvailable).ToList();
            if (candidates.Count == 0)
            {
                _dialogService.ShowMessage("Không có phần mềm nào đang có bản cập nhật mới.", "Không có gì để cập nhật");
                return;
            }

            var previewNames = candidates.Take(10).Select(s => $"• {s.Name} → {s.LatestKnownVersion}");
            string previewText = string.Join("\n", previewNames);
            if (candidates.Count > 10) previewText += $"\n... và {candidates.Count - 10} phần mềm khác";

            var confirm = MessageBox.Show(
                $"Sẽ tải bản mới nhất về THƯ VIỆN (thư mục Data/) cho {candidates.Count} phần mềm sau:\n\n{previewText}\n\n" +
                "KHÔNG đụng gì tới các phần mềm đang chạy trên máy này. Tiếp tục?",
                "Xác nhận cập nhật toàn bộ",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsUpdatingAll = true;
            int done = 0, successCount = 0, failedCount = 0;
            var failedNames = new List<string>();

            // Giới hạn tải tối đa 2 phần mềm cùng lúc, tránh nghẽn mạng/đĩa khi thư viện có nhiều app cần cập nhật
            var throttle = new SemaphoreSlim(2);
            UpdateAllStatusText = $"Đang cập nhật... 0/{candidates.Count}";

            try
            {
                var tasks = candidates.Select(async software =>
                {
                    await throttle.WaitAsync();
                    try
                    {
                        var checkResult = await _updateCheckService.CheckOneAsync(software);
                        if (!checkResult.Success || string.IsNullOrWhiteSpace(checkResult.DownloadUrl))
                        {
                            Interlocked.Increment(ref failedCount);
                            lock (failedNames) failedNames.Add($"{software.Name} (không có link tải tự động)");
                            return;
                        }

                        software.IsDownloadingUpdate = true;
                        software.DownloadProgressPercent = 0;
                        software.DownloadedBytes = 0;
                        software.DownloadTotalBytes = 0;
                        software.DownloadSpeedBytesPerSecond = 0;
                        try
                        {
                            var progress = new Progress<DownloadProgressInfo>(info =>
                            {
                                software.DownloadProgressPercent = info.PercentComplete;
                                software.DownloadedBytes = info.BytesDownloaded;
                                software.DownloadTotalBytes = info.TotalBytes;
                                software.DownloadSpeedBytesPerSecond = info.SpeedBytesPerSecond;
                            });
                            bool ok = await _updateCheckService.DownloadAndAddVersionAsync(
                                software, checkResult.DownloadUrl, checkResult.LatestVersion, progress);

                            if (ok) Interlocked.Increment(ref successCount);
                            else
                            {
                                Interlocked.Increment(ref failedCount);
                                lock (failedNames) failedNames.Add(software.Name);
                            }
                        }
                        finally
                        {
                            software.IsDownloadingUpdate = false;
                            software.DownloadProgressPercent = 0;
                            software.DownloadedBytes = 0;
                            software.DownloadTotalBytes = 0;
                            software.DownloadSpeedBytesPerSecond = 0;
                        }
                    }
                    finally
                    {
                        int completed = Interlocked.Increment(ref done);
                        Application.Current.Dispatcher.Invoke(() =>
                            UpdateAllStatusText = $"Đang cập nhật... {completed}/{candidates.Count}");
                        throttle.Release();
                    }
                });

                await Task.WhenAll(tasks);
                await LoadDataAsync();

                string summary = $"Đã tải thành công {successCount}/{candidates.Count} bản cập nhật vào thư viện.";
                if (failedCount > 0)
                    summary += $"\n\n{failedCount} phần mềm thất bại:\n{string.Join("\n", failedNames)}";

                _dialogService.ShowMessage(summary, failedCount == 0 ? "Hoàn tất cập nhật toàn bộ" : "Hoàn tất (có lỗi)");
            }
            finally
            {
                IsUpdatingAll = false;
                UpdateAllStatusText = null;
            }
        }

        // MỚI: Quét TOÀN BỘ phần mềm chưa cấu hình, tự áp catalog gợi ý cho những app khớp được,
        // giải quyết đúng nỗi đau "phải cấu hình tay từng phần mềm một" khi thư viện có nhiều app.
        private async Task ExecuteAutoConfigureAllAsync()
        {
            var candidates = Softwares.Where(s => !s.HasUpdateSourceConfigured).ToList();
            if (candidates.Count == 0)
            {
                _dialogService.ShowMessage("Tất cả phần mềm đều đã được cấu hình nguồn cập nhật rồi.", "Không có gì để áp dụng");
                return;
            }

            int matched = 0;
            foreach (var software in candidates)
            {
                if (KnownAppsCatalog.TryFindMatch(software.Name, out var entry))
                {
                    software.UpdateSourceType = entry.UpdateSourceType;
                    software.UpdateSourceUrl = entry.UpdateSourceUrl;
                    software.UpdateVersionPattern = null;
                    software.UpdateDownloadPattern = null;
                    await _softwareRepo.UpdateUpdateSourceConfigAsync(software);
                    matched++;
                }
            }

            await LoadDataAsync();

            _dialogService.ShowMessage(
                matched > 0
                    ? $"Đã tự động cấu hình cho {matched}/{candidates.Count} phần mềm dựa theo catalog gợi ý sẵn.\n" +
                      (candidates.Count - matched > 0
                          ? $"Còn {candidates.Count - matched} phần mềm không khớp catalog, cần cấu hình tay (bấm icon ⚙ trên từng dòng)."
                          : "Toàn bộ phần mềm chưa cấu hình đều đã được áp dụng tự động!")
                    : $"Không có phần mềm nào trong {candidates.Count} phần mềm chưa cấu hình khớp với catalog gợi ý sẵn.\n" +
                      "Cần cấu hình tay qua icon ⚙, hoặc tự bổ sung thêm vào KnownAppsCatalog.cs.",
                "Kết quả tự động cấu hình");
        }

        // MỚI: Gọi từ code-behind của View (sự kiện Loaded) - tự hiện hướng dẫn đúng 1 lần duy nhất
        // trong suốt vòng đời sử dụng app (ghi nhớ qua settings.json), các lần mở trang sau sẽ không tự hiện nữa.
        public void ShowGuideIfFirstTime()
        {
            var config = SettingsManager.LoadSettings();
            if (config.HasSeenUpdateCenterGuide) return;

            _dialogService.ShowUpdateCenterGuideDialog();

            config.HasSeenUpdateCenterGuide = true;
            SettingsManager.SaveSettings(config);
        }
    }
}
