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
            ? Loc.F("UpdateCenter_LastScan0Dd", _lastScanTime)
            : Loc.T("UpdateCenter_NeverScanned");

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
                    Loc.T("UpdateCenter_NoSoftwareHasAn") + "\n" +
                    Loc.T("UpdateCenter_ClickTheIconOn"),
                    Loc.T("UpdateCenter_NothingToScan"));
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
                    Loc.F("UpdateCenter_FinishedChecking0Software", candidates.Count, foundCount) + "\n" +
                    (updatesAvailable > 0
                        ? Loc.F("UpdateCenter_Found0SoftwareWith", updatesAvailable)
                        : Loc.T("UpdateCenter_AllSoftwareIsOn")),
                    Loc.T("UpdateCenter_UpdateCheckComplete"));
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
                _dialogService.ShowMessage(Loc.T("UpdateCenter_ThisSoftwareHasNo"), Loc.T("UpdateCenter_MissingConfiguration"));
                return;
            }

            var result = await _updateCheckService.CheckOneAsync(software);
            await LoadDataAsync();

            _dialogService.ShowMessage(
                result.Success
                    ? Loc.F("UpdateCenter_0TheLatestVersion", software.Name, result.LatestVersion)
                    : Loc.F("UpdateCenter_CheckOf0Failed", software.Name, result.ErrorMessage),
                result.Success ? Loc.T("UpdateCenter_CheckResult") : Loc.T("Common_Error"));
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
                _dialogService.ShowMessage(Loc.T("UpdateCenter_ThisSoftwareHasNo"), Loc.T("UpdateCenter_MissingConfiguration"));
                return;
            }

            var checkResult = await _updateCheckService.CheckOneAsync(software);
            if (!checkResult.Success)
            {
                _dialogService.ShowMessage(Loc.F("UpdateCenter_UpdateCheckFailed0", checkResult.ErrorMessage), Loc.T("Common_Error"));
                return;
            }
            if (string.IsNullOrWhiteSpace(checkResult.DownloadUrl))
            {
                string reason = software.UpdateSourceType == "Winget"
                    ? Loc.F("UpdateCenter_FoundVersion0Via", checkResult.LatestVersion) +
                      Loc.T("UpdateCenter_UsuallyBecauseItWas") +
                      Loc.T("UpdateCenter_ThisVersionWillNOT")
                    : Loc.F("UpdateCenter_FoundVersion0But", checkResult.LatestVersion) + "\n" +
                      Loc.T("UpdateCenter_DownloadItManuallyThen");

                _dialogService.ShowMessage(reason, Loc.T("UpdateCenter_NoAutomaticDownloadLink"));
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
                    ok ? Loc.F("UpdateCenter_DownloadedAndAddedVersion", checkResult.LatestVersion, software.Name)
                       : Loc.T("UpdateCenter_DownloadingOrAddingThe"),
                    ok ? Loc.T("PresetMain_Success") : Loc.T("Common_Error"));
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
                _dialogService.ShowMessage(Loc.T("UpdateCenter_ThisSoftwareHasNo"), Loc.T("UpdateCenter_MissingConfiguration"));
                return;
            }

            // MỚI: Nguồn Winget đi đường riêng - để chính Winget lo cả tải lẫn cài (đáng tin cậy hơn,
            // dùng đúng hạ tầng Microsoft), KHÔNG qua downloader tự viết của SRM.
            if (software.UpdateSourceType == "Winget")
            {
                var wingetCheck = await _updateCheckService.CheckOneAsync(software);
                if (!wingetCheck.Success)
                {
                    _dialogService.ShowMessage(Loc.F("UpdateCenter_UpdateCheckFailed0", wingetCheck.ErrorMessage), Loc.T("Common_Error"));
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
                            ? Loc.F("UpdateCenter_Installed0Version1", software.Name, wingetCheck.LatestVersion) + "\n" +
                              Loc.T("UpdateCenter_NoteThisVersionWas")
                            : Loc.T("UpdateCenter_InstallingViaWingetFailed"),
                        wingetOk ? Loc.T("UpdateCenter_InstallationSuccessful") : Loc.T("Common_Error"));
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
                    Loc.F("UpdateCenter_NoAutoLink", checkResult.ErrorMessage ?? Loc.T("UpdateCenter_SourceNoDirectLink")),
                    Loc.T("Common_Error"));
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
                _dialogService.ShowMessage(Loc.T("UpdateCenter_DownloadingTheUpdateFailed"), Loc.T("Common_Error"));
                return;
            }

            await LoadDataAsync();

            var refreshedSoftware = Softwares.FirstOrDefault(s => s.Id == software.Id);
            var newVersion = refreshedSoftware?.Versions.OrderByDescending(v => v.Id).FirstOrDefault();
            if (newVersion == null)
            {
                _dialogService.ShowMessage(Loc.T("UpdateCenter_AddedToTheLibrary"), Loc.T("Common_Error"));
                return;
            }

            bool installOk = await _deploymentService.RunSilentInstallAsync(newVersion.FilePath, software.SilentInstallCommand);

            _dialogService.ShowMessage(
                installOk
                    ? Loc.F("UpdateCenter_Installed0Version12", software.Name, newVersion.Version)
                    : Loc.F("UpdateCenter_AddedVersion0To", newVersion.Version) +
                      Loc.T("UpdateCenter_PossiblyBecauseTheSilent"),
                installOk ? Loc.T("UpdateCenter_InstallationSuccessful") : Loc.T("UpdateCenter_ManualInstallationNeeded"));
        }
        // MỚI: Tải TẤT CẢ bản cập nhật đang có (HasUpdateAvailable) về thư viện cùng lúc.
        // Chỉ thêm vào Data/ + DB, KHÔNG đụng gì tới máy đang chạy app (giống hệt "Tải về" của từng dòng,
        // chỉ khác là chạy cho toàn bộ danh sách 1 lần thay vì bấm tay từng cái).
        private async Task ExecuteUpdateAllAsync()
        {
            var candidates = Softwares.Where(s => s.HasUpdateAvailable).ToList();
            if (candidates.Count == 0)
            {
                _dialogService.ShowMessage(Loc.T("UpdateCenter_NoSoftwareHasA"), Loc.T("UpdateCenter_NothingToUpdate"));
                return;
            }

            var previewNames = candidates.Take(10).Select(s => $"• {s.Name} → {s.LatestKnownVersion}");
            string previewText = string.Join("\n", previewNames);
            if (candidates.Count > 10) previewText += "\n" + Loc.F("UpdateCenter_AndMoreSoftware", candidates.Count - 10);

            var confirm = MessageBox.Show(
                Loc.F("UpdateCenter_ConfirmUpdateAllIntro", candidates.Count) + "\n\n" + previewText + "\n\n" +
                Loc.T("UpdateCenter_ThisDoesNOTTouch"),
                Loc.T("UpdateCenter_ConfirmUpdateAll"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsUpdatingAll = true;
            int done = 0, successCount = 0, failedCount = 0;
            var failedNames = new List<string>();

            // Giới hạn tải tối đa 2 phần mềm cùng lúc, tránh nghẽn mạng/đĩa khi thư viện có nhiều app cần cập nhật
            var throttle = new SemaphoreSlim(2);
            UpdateAllStatusText = Loc.F("UpdateCenter_Updating00", candidates.Count);

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
                            lock (failedNames) failedNames.Add(Loc.F("UpdateCenter_0NoAutomaticDownload", software.Name));
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
                            UpdateAllStatusText = Loc.F("UpdateCenter_Updating01", completed, candidates.Count));
                        throttle.Release();
                    }
                });

                await Task.WhenAll(tasks);
                await LoadDataAsync();

                string summary = Loc.F("UpdateCenter_SuccessfullyDownloaded01", successCount, candidates.Count);
                if (failedCount > 0)
                    summary += "\n\n" + Loc.F("UpdateCenter_FailedSoftwareList", failedCount) + "\n" + string.Join("\n", failedNames);

                _dialogService.ShowMessage(summary, failedCount == 0 ? Loc.T("UpdateCenter_UpdateAllComplete") : Loc.T("UpdateCenter_CompleteWithErrors"));
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
                _dialogService.ShowMessage(Loc.T("UpdateCenter_AllSoftwareAlreadyHas"), Loc.T("UpdateCenter_NothingToApply"));
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
                    ? Loc.F("UpdateCenter_AutomaticallyConfigured01", matched, candidates.Count) + "\n" +
                      (candidates.Count - matched > 0
                          ? Loc.F("UpdateCenter_0SoftwareDidNot", candidates.Count - matched)
                          : Loc.T("UpdateCenter_AllUnconfiguredSoftwareWas"))
                    : Loc.F("UpdateCenter_NoneOfThe0", candidates.Count) + "\n" +
                      Loc.T("UpdateCenter_ConfigureByHandVia"),
                Loc.T("UpdateCenter_AutoConfigureResult"));
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
