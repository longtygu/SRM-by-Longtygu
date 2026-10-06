using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services;

namespace SRM_by_Longtygu.ViewModels
{
    public class DeployItem : ViewModelBase
    {
        public Software Software { get; set; }

        public ObservableCollection<SoftwareVersion> AvailableVersions { get; set; }

        // MỚI: Báo hiệu phần mềm này có tài nguyên đính kèm hay không, dùng để hiện icon ghim trên DataGrid
        public bool HasAttachedResources => Software?.AttachedResources != null && Software.AttachedResources.Count > 0;
        public int AttachedResourceCount => Software?.AttachedResources?.Count ?? 0;

        private SoftwareVersion _selectedVersion;
        public SoftwareVersion SelectedVersion
        {
            get => _selectedVersion;
            set => SetProperty(ref _selectedVersion, value);
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        private string _status = "Đang chờ";
        public string Status { get => _status; set => SetProperty(ref _status, value); }
    }

    public class DeploymentViewModel : ViewModelBase
    {
        private readonly ISoftwareRepository _softwareRepo;
        private readonly ISoftwareVersionRepository _versionRepo;
        private readonly IDeploymentService _deployService;
        private readonly ISoftwareResourceRepository _resourceMappingRepo; // MỚI: để nạp & mở tài nguyên đính kèm

        public ObservableCollection<DeployItem> Queue { get; } = new ObservableCollection<DeployItem>();
        public ICollectionView QueueView { get; }
        public ObservableCollection<string> LiveLogs { get; } = new ObservableCollection<string>();

        public int SelectedCount => Queue.Count(x => x.IsSelected);

        private bool _isDeploying;
        public bool IsDeploying { get => _isDeploying; set => SetProperty(ref _isDeploying, value); }

        private int _progressValue;
        public int ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

        private string _globalStatus = "Hãy chọn các phần mềm cần cài đặt";
        public string GlobalStatus { get => _globalStatus; set => SetProperty(ref _globalStatus, value); }

        private string _currentSortColumn = string.Empty;
        private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;

        // ================= TÌM KIẾM =================
        // FIX: property này trước đây KHÔNG TỒN TẠI trong ViewModel, trong khi
        // XAML lại binding "{Binding SearchText}" -> binding chết âm thầm,
        // gõ chữ vào ô tìm kiếm không có tác dụng gì với danh sách.
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    // Mỗi khi từ khóa thay đổi, yêu cầu CollectionView chạy lại hàm Filter bên dưới
                    QueueView.Refresh();
                }
            }
        }

        // ================= TÙY CHỈNH ĐƯỜNG DẪN CÀI ĐẶT =================
        private bool _isCustomPathEnabled;
        public bool IsCustomPathEnabled { get => _isCustomPathEnabled; set => SetProperty(ref _isCustomPathEnabled, value); }

        private string _customInstallPath = @"C:\PhanMem"; // Đổi mặc định thành ổ C để an toàn hơn
        public string CustomInstallPath { get => _customInstallPath; set => SetProperty(ref _customInstallPath, value); }

        // MỚI: Tự động mở vị trí (Explorer) của các tài nguyên đính kèm ngay sau khi cài đặt phần mềm thành công
        private bool _openResourceAfterInstall;
        public bool OpenResourceAfterInstall { get => _openResourceAfterInstall; set => SetProperty(ref _openResourceAfterInstall, value); }

        public ICommand SelectInstallFolderCommand { get; }
        public ICommand StartDeployCommand { get; }

        public DeploymentViewModel(ISoftwareRepository softwareRepo, ISoftwareVersionRepository versionRepo, IDeploymentService deployService,
                                    ISoftwareResourceRepository resourceMappingRepo)
        {
            _softwareRepo = softwareRepo;
            _versionRepo = versionRepo;
            _deployService = deployService;
            _resourceMappingRepo = resourceMappingRepo;

            QueueView = CollectionViewSource.GetDefaultView(Queue);
            QueueView.Filter = FilterQueueItem;

            // ĐÃ SỬA LỖI CRASH KHI CHỌN THƯ MỤC
            SelectInstallFolderCommand = new RelayCommand(_ =>
            {
                // Kiểm tra ổ D có tồn tại không, nếu không thì tự động lùi về ổ C
                string initDir = Directory.Exists(@"D:\") ? @"D:\" : @"C:\";

                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Chọn thư mục cài đặt phần mềm",
                    InitialDirectory = initDir
                };

                if (dialog.ShowDialog() == true)
                {
                    CustomInstallPath = dialog.FolderName;
                }
            });

            StartDeployCommand = new RelayCommand(_ => _ = ExecuteDeploymentAsync(), _ => CanDeploy());

            _ = LoadSoftwareQueueAsync();
        }

        private async Task LoadSoftwareQueueAsync()
        {
            var softwares = await _softwareRepo.GetAllAsync();
            Application.Current.Dispatcher.Invoke(() => Queue.Clear());

            foreach (var sw in softwares)
            {
                var versions = await _versionRepo.GetBySoftwareIdAsync(sw.Id);

                if (versions != null && versions.Any())
                {
                    // MỚI: Nạp danh sách tài nguyên đã đính kèm để hiện icon báo hiệu và phục vụ chức năng
                    // "Mở tài nguyên đính kèm khi cài xong"
                    var attachedResources = await _resourceMappingRepo.GetResourcesForSoftwareAsync(sw.Id);
                    sw.AttachedResources = new ObservableCollection<ResourceFile>(attachedResources);

                    var item = new DeployItem
                    {
                        Software = sw,
                        AvailableVersions = new ObservableCollection<SoftwareVersion>(versions),
                        SelectedVersion = versions.FirstOrDefault(),
                        IsSelected = false
                    };

                    item.PropertyChanged += (s, e) => {
                        if (e.PropertyName == nameof(DeployItem.IsSelected))
                        {
                            OnPropertyChanged(nameof(SelectedCount));
                            CommandManager.InvalidateRequerySuggested();
                        }
                    };

                    Application.Current.Dispatcher.Invoke(() => Queue.Add(item));
                }
            }
        }

        private void WriteLog(string message)
        {
            Application.Current?.Dispatcher.Invoke(() => {
                LiveLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            });
        }

        private void UpdateLastLog(string message)
        {
            Application.Current?.Dispatcher.Invoke(() => {
                if (LiveLogs.Count > 0)
                    LiveLogs[LiveLogs.Count - 1] = $"[{DateTime.Now:HH:mm:ss}] {message}";
            });
        }

        private bool CanDeploy() => !IsDeploying && SelectedCount > 0;

        // ================= HÀM LỌC CHO THANH TÌM KIẾM =================
        // Khớp với nội dung placeholder: "Nhập tên phần mềm, nhà phát triển
        // hoặc danh mục để tìm kiếm..." -> lọc theo cả 3 trường này.
        private bool FilterQueueItem(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (obj is not DeployItem item || item.Software == null) return false;

            string keyword = SearchText.Trim();

            bool matchName = item.Software.Name?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
            bool matchPublisher = item.Software.Publisher?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
            bool matchCategory = item.Software.Category?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

            return matchName || matchPublisher || matchCategory;
        }

        // HÀM TỰ ĐỘNG NỐI ĐƯỜNG DẪN CÀI ĐẶT
        private string BuildFinalInstallCommand(string originalCmd, string filePath)
        {
            string cmd = originalCmd ?? "";

            if (IsCustomPathEnabled && !string.IsNullOrWhiteSpace(CustomInstallPath))
            {
                string ext = Path.GetExtension(filePath)?.ToLower();

                if (ext == ".msi") cmd += $" INSTALLDIR=\"{CustomInstallPath}\"";
                else if (cmd.Contains("/VERYSILENT") || cmd.Contains("/SILENT")) cmd += $" /DIR=\"{CustomInstallPath}\"";
                else if (cmd.Contains("/S") || cmd.Contains("/s")) cmd += $" /D={CustomInstallPath}";
            }
            return cmd.Trim();
        }

        // ================= HÀM HỖ TRỢ: TẠO SHORTCUT DESKTOP BẰNG POWERSHELL =================
        private void CreateDesktopShortcut(string targetPath, string shortcutName)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, $"{shortcutName}.lnk");

                // Sử dụng PowerShell ngầm để tạo Shortcut (Không cần cài thêm thư viện ngoài)
                string psCommand = $"$wshell = New-Object -ComObject WScript.Shell; $shortcut = $wshell.CreateShortcut('{shortcutPath}'); $shortcut.TargetPath = '{targetPath}'; $shortcut.Save();";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"{psCommand}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(processInfo)?.WaitForExit();
            }
            catch { /* Bỏ qua lỗi hiển thị nếu quyền admin chặn tạo shortcut */ }
        }

        // MỚI: Mở Explorer tới vị trí (và bôi đen sẵn) TOÀN BỘ tài nguyên đính kèm của 1 phần mềm,
        // dùng ngay sau khi cài đặt/triển khai thành công nếu OpenResourceAfterInstall = true.
        // Hành vi giống hệt nút "Mở vị trí tài nguyên" bên LibraryView (không chạy/mở nội dung file).
        private void OpenAllResourceLocations(Software software)
        {
            if (software?.AttachedResources == null || software.AttachedResources.Count == 0) return;

            foreach (var resource in software.AttachedResources)
            {
                try
                {
                    string absoluteFolderPath = GetAbsoluteResourceFolderPath(resource.FolderPath);
                    string fullPath = Path.Combine(absoluteFolderPath ?? string.Empty, resource.FileName ?? string.Empty);

                    if (File.Exists(fullPath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                    }
                    else if (!string.IsNullOrEmpty(absoluteFolderPath) && Directory.Exists(absoluteFolderPath))
                    {
                        Process.Start("explorer.exe", absoluteFolderPath);
                    }
                    else
                    {
                        WriteLog($"[CẢNH BÁO] Không tìm thấy vị trí tài nguyên '{resource.Name}' trên ổ cứng.");
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"[CẢNH BÁO] Lỗi mở vị trí tài nguyên '{resource.Name}': {ex.Message}");
                }
            }
        }

        // MỚI: Tự phục hồi đường dẫn thư mục tài nguyên theo BaseDirectory hiện tại của máy đang chạy
        // (đồng bộ logic với GetAbsoluteFolderPath bên ResourceFileMainViewModel / LibraryViewModel)
        private string GetAbsoluteResourceFolderPath(string savedPath)
        {
            if (string.IsNullOrWhiteSpace(savedPath)) return null;

            string folderName = new DirectoryInfo(savedPath).Name;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library", "Resources", folderName);
        }

        private async Task ExecuteDeploymentAsync()
        {
            var selectedItems = Queue.Where(x => x.IsSelected).ToList();

            var alreadyInstalled = selectedItems.Where(x => x.Status == "Thành công").ToList();
            if (alreadyInstalled.Any())
            {
                string names = string.Join("\n- ", alreadyInstalled.Select(x => x.Software.Name));
                var result = MessageBox.Show($"Các phần mềm sau đã được cài đặt thành công trước đó:\n- {names}\n\nBạn có chắc chắn muốn ép cài đặt lại không?",
                                             "Cảnh báo trùng lặp", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No) return;
            }

            IsDeploying = true;
            ProgressValue = 0;
            LiveLogs.Clear();
            WriteLog("=== BẮT ĐẦU CHU TRÌNH DEPLOYMENT ===");

            GlobalStatus = "Đang tạo Restore Point...";
            WriteLog("Đang khởi tạo System Restore Point (Chờ cấp quyền Admin)...");

            bool rpCreated = await _deployService.CreateRestorePointAsync($"SRM Auto Backup {DateTime.Now:dd-MM-yyyy}");
            if (!rpCreated) WriteLog("Cảnh báo: Không thể tạo Restore Point.");
            else WriteLog("Tạo Restore Point thành công. Hệ thống an toàn.");

            int total = selectedItems.Count;
            int current = 0;

            foreach (var item in selectedItems)
            {
                GlobalStatus = $"Đang cài đặt {item.Software.Name} ({current + 1}/{total})...";
                item.Status = "Đang xử lý...";
                WriteLog($"-> Đang chuẩn bị dữ liệu: {item.Software.Name} [0%]");

                // ==============================================================
                // TÁCH LUỒNG 1: XỬ LÝ PHẦN MỀM PORTABLE
                // ==============================================================
                if (item.SelectedVersion.IsPortable)
                {
                    try
                    {
                        // Xác định nơi lưu (Tùy chỉnh hoặc C:\PortableApps)
                        string baseDest = IsCustomPathEnabled && !string.IsNullOrWhiteSpace(CustomInstallPath) ? CustomInstallPath : @"C:\PortableApps";
                        string destFolder = Path.Combine(baseDest, item.Software.Name);
                        if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

                        string fileExt = Path.GetExtension(item.SelectedVersion.FilePath)?.ToLower();
                        string targetExe = "";

                        await Task.Run(() =>
                        {
                            if (fileExt == ".zip")
                            {
                                UpdateLastLog($"-> Đang giải nén bộ cài Portable: {item.Software.Name}...");
                                ZipFile.ExtractToDirectory(item.SelectedVersion.FilePath, destFolder, true); // True = Ghi đè

                                // Tìm file .exe chạy chính trong thư mục vừa giải nén
                                var exeFiles = Directory.GetFiles(destFolder, "*.exe", SearchOption.AllDirectories);
                                targetExe = exeFiles.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Equals(item.Software.Name, StringComparison.OrdinalIgnoreCase)) ?? exeFiles.FirstOrDefault();
                            }
                            else
                            {
                                UpdateLastLog($"-> Đang copy tệp Portable: {item.Software.Name}...");
                                targetExe = Path.Combine(destFolder, Path.GetFileName(item.SelectedVersion.FilePath));
                                File.Copy(item.SelectedVersion.FilePath, targetExe, true);
                            }
                        });

                        // Tạo Shortcut ngay lập tức
                        if (!string.IsNullOrEmpty(targetExe))
                        {
                            UpdateLastLog($"-> Đang tạo Desktop Shortcut...");
                            CreateDesktopShortcut(targetExe, item.Software.Name);
                        }

                        item.Status = "Thành công";
                        WriteLog($"[OK] Hoàn tất triển khai Portable: {item.Software.Name}");

                        // MỚI: Mở vị trí tài nguyên đính kèm (nếu người dùng bật tùy chọn)
                        if (OpenResourceAfterInstall && item.Software.AttachedResources?.Count > 0)
                        {
                            WriteLog($"-> Đang mở vị trí {item.Software.AttachedResources.Count} tài nguyên đính kèm của {item.Software.Name}...");
                            OpenAllResourceLocations(item.Software);
                        }

                        item.IsSelected = false;
                    }
                    catch (Exception ex)
                    {
                        item.Status = "Lỗi";
                        WriteLog($"[ERROR] Lỗi Portable {item.Software.Name}: {ex.Message}");
                    }
                }
                // ==============================================================
                // TÁCH LUỒNG 2: XỬ LÝ CÀI ĐẶT THÔNG THƯỜNG (INSTALLER)
                // ==============================================================
                else
                {
                    string finalCommand = BuildFinalInstallCommand(item.Software.SilentInstallCommand, item.SelectedVersion.FilePath);
                    if (IsCustomPathEnabled) WriteLog($"   [Tùy chỉnh đường dẫn] Lệnh thực thi: {finalCommand}");

                    bool success = await _deployService.RunSilentInstallAsync(
                        item.SelectedVersion.FilePath,
                        finalCommand,
                        (percent) => {
                            UpdateLastLog($"-> Đang giải nén và thực thi: {item.Software.Name} [{percent}%]");
                        });

                    item.Status = success ? "Thành công" : "Lỗi";
                    if (success)
                    {
                        WriteLog($"[OK] Hoàn tất cài đặt: {item.Software.Name}");

                        // MỚI: Mở vị trí tài nguyên đính kèm (nếu người dùng bật tùy chọn)
                        if (OpenResourceAfterInstall && item.Software.AttachedResources?.Count > 0)
                        {
                            WriteLog($"-> Đang mở vị trí {item.Software.AttachedResources.Count} tài nguyên đính kèm của {item.Software.Name}...");
                            OpenAllResourceLocations(item.Software);
                        }

                        item.IsSelected = false;
                    }
                    else WriteLog($"[ERROR] Lỗi cài đặt: {item.Software.Name}");
                }

                current++;
                ProgressValue = (current * 100) / total;
            }

            GlobalStatus = $"Hoàn tất xử lý {total} phần mềm!";
            WriteLog("=== TẤT CẢ CHU TRÌNH ĐÃ KẾT THÚC ===");
            IsDeploying = false;
        }

        public void GroupOrSortBy(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return;

            if (_currentSortColumn == propertyName)
                _currentSortDirection = _currentSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            else { _currentSortColumn = propertyName; _currentSortDirection = ListSortDirection.Ascending; }

            QueueView.GroupDescriptions.Clear();
            QueueView.SortDescriptions.Clear();
            if (propertyName == "Software.Category" || propertyName == "Software.Publisher")
                QueueView.GroupDescriptions.Add(new PropertyGroupDescription(propertyName));

            QueueView.SortDescriptions.Add(new SortDescription(propertyName, _currentSortDirection));
        }
    }
}