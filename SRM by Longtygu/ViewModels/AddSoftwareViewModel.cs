using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;

namespace SRM_by_Longtygu.ViewModels
{
    public class AddSoftwareViewModel : ViewModelBase
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

        // Các biến UI
        private string _name; public string Name { get => _name; set => SetProperty(ref _name, value); }
        private string _version; public string Version { get => _version; set => SetProperty(ref _version, value); }
        private string _publisher; public string Publisher { get => _publisher; set => SetProperty(ref _publisher, value); }
        private string _category; public string Category { get => _category; set => SetProperty(ref _category, value); }
        private string _silentCommand; public string SilentCommand { get => _silentCommand; set => SetProperty(ref _silentCommand, value); }
        private string _silentUninstallCommand; public string SilentUninstallCommand { get => _silentUninstallCommand; set => SetProperty(ref _silentUninstallCommand, value); }
        private bool _isPortable; public bool IsPortable { get => _isPortable; set => SetProperty(ref _isPortable, value); }
        private string _licenseKey; public string LicenseKey { get => _licenseKey; set => SetProperty(ref _licenseKey, value); }
        private string _description; public string Description { get => _description; set => SetProperty(ref _description, value); }
        private string _readme; public string Readme { get => _readme; set => SetProperty(ref _readme, value); }

        private string _filePath; public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }
        private string _iconPath; public string IconPath { get => _iconPath; set => SetProperty(ref _iconPath, value); }
        private bool _isSaving; public bool IsSaving { get => _isSaving; set => SetProperty(ref _isSaving, value); }

        public ICommand SaveCommand { get; }
        public ICommand SelectFileCommand { get; }
        public ICommand SelectIconCommand { get; }

        private bool _isSafeToSilentInstall;
        public bool IsSafeToSilentInstall
        {
            get => _isSafeToSilentInstall;
            set
            {
                if (SetProperty(ref _isSafeToSilentInstall, value))
                {
                    // Tự động phân tích và hiển thị lên giao diện ngay khi tick Checkbox
                    if (value && !string.IsNullOrWhiteSpace(FilePath))
                    {
                        var commands = SRM_by_Longtygu.Helpers.InstallerAnalyzer.Analyze(FilePath);
                        SilentCommand = commands.InstallCmd;
                        SilentUninstallCommand = commands.UninstallCmd;
                    }
                    // Tự động xóa sạch nếu người dùng bỏ tick
                    else if (!value)
                    {
                        SilentCommand = "";
                        SilentUninstallCommand = "";
                    }
                }
            }
        }

        public AddSoftwareViewModel(ISoftwareRepository softwareRepo, ISoftwareVersionRepository versionRepo)
        {
            _softwareRepo = softwareRepo;
            _versionRepo = versionRepo;

            SelectFileCommand = new RelayCommand(_ => SelectFile());
            SelectIconCommand = new RelayCommand(_ => SelectIcon());
            SaveCommand = new RelayCommand(async _ => await ExecuteSaveAsync(), _ => !IsSaving);
        }

        private void SelectFile()
        {
            var dialog = new OpenFileDialog { Filter = "Tệp cài đặt (*.exe;*.msi;*.zip)|*.exe;*.msi;*.zip|Tất cả các tệp (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                FilePath = dialog.FileName;
                AutoFillData(FilePath);
            }
        }

        private void SelectIcon()
        {
            var dialog = new OpenFileDialog { Filter = "Tệp hình ảnh (*.png;*.jpg;*.ico)|*.png;*.jpg;*.ico" };
            if (dialog.ShowDialog() == true) IconPath = dialog.FileName;
        }

        private void AutoFillData(string filePath)
        {
            string fileNameOnly = Path.GetFileNameWithoutExtension(filePath);
            try
            {
                var info = FileVersionInfo.GetVersionInfo(filePath);
                if (string.IsNullOrWhiteSpace(Name)) Name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName : fileNameOnly;
                if (string.IsNullOrWhiteSpace(Publisher)) Publisher = info.CompanyName;
                if (string.IsNullOrWhiteSpace(Version))
                {
                    string extractedVer = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
                    if (string.IsNullOrWhiteSpace(extractedVer) || extractedVer.Trim() == "1.0" || extractedVer.Trim() == "0.0.0.0")
                    {
                        var match = Regex.Match(fileNameOnly, @"\d+(\.\d+)+");
                        if (match.Success) extractedVer = match.Value;
                    }
                    Version = !string.IsNullOrWhiteSpace(extractedVer) ? extractedVer : "1.0";
                }
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(Name)) Name = fileNameOnly;
            }
            // ====== BỔ SUNG PHẦN NÀY VÀO CUỐI HÀM ======
            // Nếu Checkbox đang bật sẵn, tự update luôn lệnh khi có file mới
            if (IsSafeToSilentInstall)
            {
                var commands = SRM_by_Longtygu.Helpers.InstallerAnalyzer.Analyze(filePath);
                SilentCommand = commands.InstallCmd;
                SilentUninstallCommand = commands.UninstallCmd;
            }
        }

        private async Task ExecuteSaveAsync()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                MessageBox.Show("Vui lòng chọn File cài đặt trước!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(Name))
            {
                MessageBox.Show("Vui lòng nhập Tên phần mềm!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(Version))
            {
                MessageBox.Show("Vui lòng nhập Phiên bản!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(Category))
            {
                MessageBox.Show("Vui lòng chọn Danh mục cho phần mềm!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // KIỂM TRA TRÙNG LẶP
            var existingSoftware = await _softwareRepo.GetByNameAsync(Name.Trim());
            if (existingSoftware != null)
            {
                MessageBox.Show($"Phần mềm '{Name}' đã tồn tại trong thư viện.\nVui lòng sử dụng tính năng [Thêm phiên bản] ở tab Quản lý bộ cài.", "Trùng lặp", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsSaving = true;

            try
            {
                // 1. CHUẨN BỊ VÀ ĐÓNG GÓI DỮ LIỆU TỪ UI
                string captureName = Name.Trim();
                string capturePublisher = Publisher?.Trim() ?? "Unknown";
                string captureCategory = Category?.Trim();
                string captureDescription = Description?.Trim() ?? "";
                string captureLicenseKey = LicenseKey?.Trim() ?? "";
                string captureReadme = Readme?.Trim() ?? "";
                string captureVersion = Version.Trim();
                bool captureIsPortable = IsPortable;
                string captureIcon = IconPath;
                string captureFile = FilePath;
                bool captureIsSafeToSilent = IsSafeToSilentInstall;
                string baseSilentCmd = SilentCommand?.Trim() ?? "";
                string baseSilentUninstallCmd = SilentUninstallCommand?.Trim() ?? "";

                // 2. CHẠY LUỒNG NỀN XỬ LÝ (Không làm đơ UI)
                await Task.Run(async () =>
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string installersDir = Path.Combine(baseDir, "Data", "Installers");
                    string iconsDir = Path.Combine(baseDir, "Data", "Icons");

                    if (!Directory.Exists(installersDir)) Directory.CreateDirectory(installersDir);
                    if (!Directory.Exists(iconsDir)) Directory.CreateDirectory(iconsDir);

                    // TẠO ĐỐI TƯỢNG PHẦN MỀM GỐC
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



                    // XỬ LÝ ẢNH ICON
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
                                    System.Diagnostics.Debug.WriteLine($"[AddSoftware] Không lấy được icon (imgSource null/không phải BitmapSource) cho file: {captureFile}");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AddSoftware] Lỗi khi lưu icon cho '{captureFile}': {ex}");
                            }
                        });
                    }

                    // XỬ LÝ COPY FILE CÀI ĐẶT
                    string fileExt = Path.GetExtension(captureFile);
                    string newFileName = $"{Guid.NewGuid()}{fileExt}";
                    string destFilePath = Path.Combine(installersDir, newFileName);

                    File.Copy(captureFile, destFilePath, true);

                    // LƯU DATABASE
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

                Application.Current.Dispatcher.Invoke(() => {
                    MessageBox.Show("Thêm phần mềm mới thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    CloseWindow?.Invoke();
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => {
                    MessageBox.Show($"Lỗi xử lý file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsSaving = false;
            }
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