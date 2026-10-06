using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;

namespace SRM_by_Longtygu.ViewModels
{
    // ViewModel riêng cho chức năng "Thêm nhiều phần mềm cùng lúc".
    // Tách biệt hoàn toàn khỏi AddSoftwareViewModel (luồng thêm 1 phần mềm) để không đụng chạm code cũ.
    public class AddMultipleSoftwareViewModel : ViewModelBase
    {
        private readonly ISoftwareRepository _softwareRepo;
        private readonly ISoftwareVersionRepository _versionRepo;
        public Action CloseWindow { get; set; }

        // ================= DANH SÁCH CATEGORY CHO SẴN =================
        public ObservableCollection<string> ExistingCategories { get; } = new ObservableCollection<string>
        {
            "Trình duyệt Web",
            "Tiện ích hệ thống",
            "Văn phòng & Công việc",
            "Thiết kế & Đồ họa",
            "Đa phương tiện (Media)",
            "Lập trình & IT",
            "Bảo mật & Diệt Virus",
            "Trò chơi (Games)",
            "Khác"
        };

        // ================= HÀNG ĐỢI PHẦN MỀM =================
        public ObservableCollection<SoftwareEntryItem> Queue { get; } = new ObservableCollection<SoftwareEntryItem>();

        private SoftwareEntryItem _selectedEntry;
        public SoftwareEntryItem SelectedEntry
        {
            get => _selectedEntry;
            set => SetProperty(ref _selectedEntry, value);
        }

        // Danh mục chung áp dụng nhanh cho toàn bộ hàng đợi
        private string _sharedCategory;
        public string SharedCategory
        {
            get => _sharedCategory;
            set => SetProperty(ref _sharedCategory, value);
        }

        // ================= TRẠNG THÁI XỬ LÝ HÀNG LOẠT =================
        private bool _isSaving;
        public bool IsSaving { get => _isSaving; set => SetProperty(ref _isSaving, value); }

        private int _totalCount;
        public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

        private int _processedCount;
        public int ProcessedCount { get => _processedCount; set => SetProperty(ref _processedCount, value); }

        private string _currentProcessingName;
        public string CurrentProcessingName { get => _currentProcessingName; set => SetProperty(ref _currentProcessingName, value); }

        public ICommand AddFilesCommand { get; }
        public ICommand RemoveEntryCommand { get; }
        public ICommand ClearQueueCommand { get; }
        public ICommand SelectIconCommand { get; }
        public ICommand ApplyCategoryToAllCommand { get; }
        public ICommand SaveAllCommand { get; }

        public AddMultipleSoftwareViewModel(ISoftwareRepository softwareRepo, ISoftwareVersionRepository versionRepo)
        {
            _softwareRepo = softwareRepo;
            _versionRepo = versionRepo;

            AddFilesCommand = new RelayCommand(_ => AddFiles());
            RemoveEntryCommand = new RelayCommand(p => RemoveEntry(p as SoftwareEntryItem));
            ClearQueueCommand = new RelayCommand(_ => Queue.Clear(), _ => Queue.Count > 0 && !IsSaving);
            SelectIconCommand = new RelayCommand(_ => SelectIconForSelected(), _ => SelectedEntry != null);
            ApplyCategoryToAllCommand = new RelayCommand(_ => ApplyCategoryToAll(), _ => !string.IsNullOrWhiteSpace(SharedCategory) && Queue.Count > 0);
            SaveAllCommand = new RelayCommand(async _ => await ExecuteSaveAllAsync(), _ => Queue.Count > 0 && !IsSaving);
        }

        // Cho phép chọn NHIỀU file cùng lúc, mỗi file thành 1 item trong hàng đợi
        private void AddFiles()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Tệp cài đặt (*.exe;*.msi;*.zip)|*.exe;*.msi;*.zip|Tất cả các tệp (*.*)|*.*",
                Title = "Chọn nhiều file cài đặt",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;

            foreach (var path in dialog.FileNames)
            {
                // Tránh thêm trùng file vật lý đã có sẵn trong hàng đợi
                if (Queue.Any(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var entry = new SoftwareEntryItem { FilePath = path };
                entry.AutoFillFromFile();

                // Chế độ nhanh gọn: nếu đã chọn sẵn danh mục chung thì áp luôn cho item mới
                if (!string.IsNullOrWhiteSpace(SharedCategory))
                    entry.Category = SharedCategory;

                Queue.Add(entry);
            }

            if (SelectedEntry == null && Queue.Count > 0)
                SelectedEntry = Queue[0];
        }

        private void RemoveEntry(SoftwareEntryItem entry)
        {
            if (entry == null) return;
            if (SelectedEntry == entry) SelectedEntry = null;
            Queue.Remove(entry);
        }

        private void SelectIconForSelected()
        {
            if (SelectedEntry == null) return;
            var dialog = new OpenFileDialog { Filter = "Tệp hình ảnh (*.png;*.jpg;*.ico)|*.png;*.jpg;*.ico" };
            if (dialog.ShowDialog() == true) SelectedEntry.IconPath = dialog.FileName;
        }

        private void ApplyCategoryToAll()
        {
            foreach (var item in Queue) item.Category = SharedCategory;
        }

        // Xử lý tuần tự toàn bộ hàng đợi (tuần tự để tránh nhiều luồng ghi SQLite cùng lúc)
        private async Task ExecuteSaveAllAsync()
        {
            var itemsToProcess = Queue.Where(x => x.Status != EntryStatus.Success).ToList();
            if (itemsToProcess.Count == 0) return;

            TotalCount = itemsToProcess.Count;
            ProcessedCount = 0;
            IsSaving = true;

            int successCount = 0, duplicateCount = 0, errorCount = 0;

            foreach (var entry in itemsToProcess)
            {
                CurrentProcessingName = !string.IsNullOrWhiteSpace(entry.Name)
                    ? entry.Name
                    : Path.GetFileName(entry.FilePath ?? "");
                entry.Status = EntryStatus.Processing;
                entry.StatusMessage = "";

                try
                {
                    if (string.IsNullOrWhiteSpace(entry.Name)) throw new InvalidOperationException("Thiếu tên phần mềm");
                    if (string.IsNullOrWhiteSpace(entry.Version)) throw new InvalidOperationException("Thiếu phiên bản");
                    if (string.IsNullOrWhiteSpace(entry.Category)) throw new InvalidOperationException("Thiếu danh mục");
                    if (string.IsNullOrWhiteSpace(entry.FilePath) || !File.Exists(entry.FilePath))
                        throw new InvalidOperationException("File cài đặt không còn tồn tại trên ổ cứng");

                    var existingSoftware = await _softwareRepo.GetByNameAsync(entry.Name.Trim());
                    if (existingSoftware != null)
                    {
                        entry.Status = EntryStatus.Duplicate;
                        entry.StatusMessage = $"'{entry.Name}' đã tồn tại trong thư viện. Hãy đổi tên hoặc dùng chức năng [Thêm phiên bản].";
                        duplicateCount++;
                        continue;
                    }

                    await ProcessSingleEntryAsync(entry);

                    entry.Status = EntryStatus.Success;
                    entry.StatusMessage = "Đã thêm thành công";
                    successCount++;
                }
                catch (Exception ex)
                {
                    entry.Status = EntryStatus.Error;
                    entry.StatusMessage = ex.Message;
                    errorCount++;
                }
                finally
                {
                    ProcessedCount++;
                }
            }

            IsSaving = false;
            CurrentProcessingName = string.Empty;

            // Loại các item đã xử lý THÀNH CÔNG ra khỏi hàng đợi, giữ lại Trùng lặp/Lỗi để sửa & thử lại
            var doneItems = Queue.Where(x => x.Status == EntryStatus.Success).ToList();
            foreach (var d in doneItems) Queue.Remove(d);

            if (SelectedEntry == null || SelectedEntry.Status == EntryStatus.Success)
                SelectedEntry = Queue.FirstOrDefault();

            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    $"Hoàn tất xử lý {successCount + duplicateCount + errorCount} phần mềm:\n" +
                    $"✔ Thành công: {successCount}\n" +
                    $"⚠ Trùng lặp: {duplicateCount}\n" +
                    $"✘ Lỗi: {errorCount}",
                    "Kết quả thêm phần mềm hàng loạt",
                    MessageBoxButton.OK,
                    (errorCount > 0 || duplicateCount > 0) ? MessageBoxImage.Warning : MessageBoxImage.Information);
            });

            // Nếu không còn item nào tồn đọng -> tự đóng cửa sổ & refresh thư viện
            if (Queue.Count == 0)
            {
                CloseWindow?.Invoke();
            }
        }

        // Xử lý copy file, icon, và ghi database cho 1 item (giữ nguyên logic như luồng Save đơn lẻ)
        private async Task ProcessSingleEntryAsync(SoftwareEntryItem entry)
        {
            string captureName = entry.Name.Trim();
            string capturePublisher = entry.Publisher?.Trim() ?? "Unknown";
            string captureCategory = entry.Category?.Trim();
            string captureDescription = entry.Description?.Trim() ?? "";
            string captureLicenseKey = entry.LicenseKey?.Trim() ?? "";
            string captureReadme = entry.Readme?.Trim() ?? "";
            string captureVersion = entry.Version.Trim();
            bool captureIsPortable = entry.IsPortable;
            string captureIcon = entry.IconPath;
            string captureFile = entry.FilePath;
            string baseSilentCmd = entry.SilentCommand?.Trim() ?? "";
            string baseSilentUninstallCmd = entry.SilentUninstallCommand?.Trim() ?? "";

            await Task.Run(async () =>
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string installersDir = Path.Combine(baseDir, "Data", "Installers");
                string iconsDir = Path.Combine(baseDir, "Data", "Icons");

                if (!Directory.Exists(installersDir)) Directory.CreateDirectory(installersDir);
                if (!Directory.Exists(iconsDir)) Directory.CreateDirectory(iconsDir);

                var sw = new Software
                {
                    Name = captureName,
                    Publisher = capturePublisher,
                    Category = captureCategory,
                    Description = captureDescription,
                    LicenseKey = captureLicenseKey,
                    Readme = captureReadme,
                    CreatedDate = DateTime.Now.ToString("dd/MM/yyyy"),
                    IconPath = "",
                    SilentInstallCommand = baseSilentCmd,
                    SilentUninstallCommand = baseSilentUninstallCmd
                };

                if (!string.IsNullOrWhiteSpace(captureIcon) && File.Exists(captureIcon))
                {
                    string iconExt = Path.GetExtension(captureIcon);
                    string newIconName = $"{Guid.NewGuid()}{iconExt}";
                    File.Copy(captureIcon, Path.Combine(iconsDir, newIconName), true);
                    sw.IconPath = Path.Combine("Data", "Icons", newIconName);
                }
                else if (!string.IsNullOrWhiteSpace(captureFile))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var imgSource = SRM_by_Longtygu.Helpers.IconExtractor.GetIconFromPath(captureFile);
                            if (imgSource is System.Windows.Media.Imaging.BitmapSource bmpSrc)
                            {
                                string newIconName = $"{Guid.NewGuid()}.png";
                                string destIconPath = Path.Combine(iconsDir, newIconName);

                                using (var fileStream = new FileStream(destIconPath, FileMode.Create))
                                {
                                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmpSrc));
                                    encoder.Save(fileStream);
                                }
                                sw.IconPath = Path.Combine("Data", "Icons", newIconName);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[AddMultipleSoftware] Không lấy được icon (imgSource null/không phải BitmapSource) cho file: {captureFile}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AddMultipleSoftware] Lỗi khi lưu icon cho '{captureFile}': {ex}");
                        }
                    });
                }

                string fileExt = Path.GetExtension(captureFile);
                string newFileName = $"{Guid.NewGuid()}{fileExt}";
                string destFilePath = Path.Combine(installersDir, newFileName);

                File.Copy(captureFile, destFilePath, true);

                int newSoftwareId = await _softwareRepo.InsertAsync(sw);

                await _versionRepo.InsertAsync(new SoftwareVersion
                {
                    SoftwareId = newSoftwareId,
                    Version = captureVersion,
                    FilePath = Path.Combine("Data", "Installers", newFileName),
                    FileSize = new FileInfo(destFilePath).Length,
                    SHA256 = CalculateSHA256(destFilePath),
                    IsPortable = captureIsPortable
                });
            });
        }

        private string CalculateSHA256(string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
            }
        }
    }
}
