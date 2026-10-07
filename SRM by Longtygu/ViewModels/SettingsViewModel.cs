using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services;

namespace SRM_by_Longtygu.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ILogService _logService;
        private readonly IDatabaseSchemaMigrator _schemaMigrator; // MỚI: tự vá schema DB cũ sau khi restore
        private readonly IBusyService _busy;   // MỚI: khóa toàn app khi đang chạy tiến trình
        private IDisposable? _busyLease;       // khóa đang giữ của thao tác hiện tại

        // ================= CÁC BIẾN UI CHO THANH TIẾN ĐỘ & LOG =================
        private string _backupStatus = "Sẵn sàng";
        public string BackupStatus { get => _backupStatus; set => SetProperty(ref _backupStatus, value); }

        private double _backupProgress = 0;
        public double BackupProgress { get => _backupProgress; set => SetProperty(ref _backupProgress, value); }

        private bool _isBackingUp = false;
        public bool IsBackingUp { get => _isBackingUp; set => SetProperty(ref _isBackingUp, value); }

        // Biến kiểm soát Trạng thái Ẩn/Hiện của khay Log
        private bool _isLogExpanded = false;
        public bool IsLogExpanded { get => _isLogExpanded; set => SetProperty(ref _isLogExpanded, value); }

        private ObservableCollection<string> _operationLogs = new ObservableCollection<string>();
        public ObservableCollection<string> OperationLogs
        {
            get => _operationLogs;
            set => SetProperty(ref _operationLogs, value);
        }

        // ================= KHAI BÁO LỆNH =================
        public ICommand ToggleLanguageCommand { get; }
        public ICommand BackupDbCommand { get; }
        public ICommand FullBackupCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand ResetCommand { get; }

        public SettingsViewModel(ILogService logService, IDatabaseSchemaMigrator schemaMigrator, IBusyService busyService)
        {
            _logService = logService;
            _schemaMigrator = schemaMigrator; // MỚI
            _busy = busyService;

            ToggleLanguageCommand = new RelayCommand(_ => ToggleLanguage());

            BackupDbCommand = new RelayCommand(async _ => await ExecuteBackupDbAsync(), _ => CanStartOperation());
            FullBackupCommand = new RelayCommand(async _ => await ExecuteFullBackupAsync(), _ => CanStartOperation());
            RestoreCommand = new RelayCommand(async _ => await ExecuteRestoreAsync(), _ => CanStartOperation());
            ResetCommand = new RelayCommand(async _ => await ExecuteResetAsync(), _ => CanStartOperation());
        }

        private void ToggleLanguage()
        {
            MessageBox.Show("Chức năng chuyển đổi ngôn ngữ đang được phát triển!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ================= KHÓA TOÀN APP KHI ĐANG CHẠY TIẾN TRÌNH =================
        // Chỉ cho bắt đầu thao tác mới khi không có tiến trình nào (ở bất kỳ tab nào) đang chạy
        private bool CanStartOperation() => !IsBackingUp && !_busy.IsBusy;

        private void BeginBusy(string operationName)
        {
            _busyLease?.Dispose();
            _busyLease = _busy.Begin(operationName);
        }

        private void EndBusy()
        {
            _busyLease?.Dispose();
            _busyLease = null;
        }

        // ================= HÀM HỖ TRỢ: QUÉT TÌM DATABASE =================
        private string FindDatabaseFile(string baseDir)
        {
            string myDbName = "library.db";
            string dbFolderPath = Path.Combine(baseDir, "Database", myDbName);
            if (File.Exists(dbFolderPath)) return dbFolderPath;
            string exactPath = Path.Combine(baseDir, myDbName);
            if (File.Exists(exactPath)) return exactPath;
            string exactDataPath = Path.Combine(baseDir, "Data", myDbName);
            if (File.Exists(exactDataPath)) return exactDataPath;

            string[] possibleExtensions = { "*.db", "*.sqlite", "*.sqlite3" };
            foreach (var ext in possibleExtensions)
            {
                var file = Directory.GetFiles(baseDir, ext, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (file != null) return file;

                string[] subDirs = { "Database", "Data" };
                foreach (var dir in subDirs)
                {
                    string targetDir = Path.Combine(baseDir, dir);
                    if (Directory.Exists(targetDir))
                    {
                        file = Directory.GetFiles(targetDir, ext, SearchOption.TopDirectoryOnly).FirstOrDefault();
                        if (file != null) return file;
                    }
                }
            }
            return null;
        }

        // ================= THUẬT TOÁN 1: BACKUP DATABASE =================
        private async Task ExecuteBackupDbAsync()
        {
            Application.Current.Dispatcher.Invoke(() => OperationLogs.Clear());
            IsLogExpanded = true; // Tự động mở khay Log

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dbFile = FindDatabaseFile(baseDir);

            if (string.IsNullOrEmpty(dbFile))
            {
                MessageBox.Show("Lỗi: Không tìm thấy tệp cơ sở dữ liệu SQLite trong hệ thống!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsBackingUp = true;
            BeginBusy("Sao lưu cơ sở dữ liệu");
            UpdateProgress(40, $"Đã thấy DB: {Path.GetFileName(dbFile)}. Đang chuẩn bị...");

            try
            {
                string backupDir = Path.Combine(baseDir, "Backups");
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                    WriteLog($"Đã tạo thư mục lưu trữ tại: {backupDir}");
                }

                string dbName = Path.GetFileNameWithoutExtension(dbFile);
                string backupPath = Path.Combine(backupDir, $"{dbName}_Backup_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(dbFile)}");

                WriteLog($"Tiến hành sao chép: {dbFile}");
                await Task.Run(() => File.Copy(dbFile, backupPath, true));
                WriteLog($"Sao chép thành công. Đích đến: {backupPath}");

                UpdateProgress(100, $"Thành công! Đã sao lưu tại: Backups/{Path.GetFileName(backupPath)}");
            }
            catch (Exception ex)
            {
                UpdateProgress(BackupProgress, $"Lỗi sao lưu: {ex.Message}");
                WriteLog($"[LỖI NGHIÊM TRỌNG] {ex.Message}");
            }
            finally
            {
                IsBackingUp = false;
                EndBusy();
            }
        }

        // ================= THUẬT TOÁN 2: FULL BACKUP (.zip) =================
        private async Task ExecuteFullBackupAsync()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dbFile = FindDatabaseFile(baseDir);

            if (string.IsNullOrEmpty(dbFile))
            {
                MessageBox.Show("Lỗi: Không tìm thấy tệp cơ sở dữ liệu SQLite để đóng gói!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string appName = new DirectoryInfo(baseDir).Name.Replace(" ", "");
            if (string.IsNullOrWhiteSpace(appName)) appName = "SRM";

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Zip File (*.zip)|*.zip",
                FileName = $"{appName}_FullBackup_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                Title = "Chọn nơi lưu bản sao lưu toàn bộ"
            };

            if (dialog.ShowDialog() != true) return;

            string zipPath = dialog.FileName;
            IsBackingUp = true;
            BeginBusy("Xuất toàn bộ dữ liệu (Full Zip)");

            Application.Current.Dispatcher.Invoke(() => OperationLogs.Clear());
            IsLogExpanded = true; // Tự động mở khay Log

            UpdateProgress(5, "Đang khởi tạo môi trường đóng gói...");

            try
            {
                await Task.Run(() =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "SRM_Backup_" + Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDir);
                    WriteLog($"Tạo thư mục Workspace tạm thời tại: {tempDir}");

                    UpdateProgress(10, $"Đang sao chép Database ({Path.GetFileName(dbFile)})...");
                    File.Copy(dbFile, Path.Combine(tempDir, Path.GetFileName(dbFile)), true);
                    WriteLog("Đã sao chép file Database.");

                    UpdateProgress(30, "Đang sao chép các bộ cài và hình ảnh (Data)...");
                    string sourceDataDir = Path.Combine(baseDir, "Data");
                    if (Directory.Exists(sourceDataDir))
                    {
                        string destDataDir = Path.Combine(tempDir, "Data");
                        WriteLog($"Đang xử lý thư mục: {sourceDataDir}");
                        CopyDirectoryRecursively(sourceDataDir, destDataDir);
                    }
                    else WriteLog("Không tìm thấy thư mục Data, bỏ qua bước này.");

                    UpdateProgress(60, "Đang sao chép thư viện tài nguyên (Library)...");
                    string sourceLibraryDir = Path.Combine(baseDir, "Library");
                    if (Directory.Exists(sourceLibraryDir))
                    {
                        string destLibraryDir = Path.Combine(tempDir, "Library");
                        WriteLog($"Đang xử lý thư mục: {sourceLibraryDir}");
                        CopyDirectoryRecursively(sourceLibraryDir, destLibraryDir);
                    }
                    else WriteLog("Không tìm thấy thư mục Library, bỏ qua bước này.");

                    UpdateProgress(85, "Đang nén dữ liệu thành file Zip (Vui lòng chờ)...");
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                        WriteLog("Đã ghi đè file zip cũ.");
                    }
                    WriteLog($"Bắt đầu thuật toán nén ZIP tối ưu. Đích đến: {zipPath}");
                    ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Fastest, false);
                    WriteLog("Nén dữ liệu thành công.");

                    UpdateProgress(95, "Đang dọn dẹp bộ nhớ tạm...");
                    Directory.Delete(tempDir, true);
                    WriteLog("Hoàn tất dọn rác bộ nhớ.");
                });

                UpdateProgress(100, $"Hoàn tất xuất dữ liệu! Đã lưu tại: {Path.GetFileName(zipPath)}");
            }
            catch (Exception ex)
            {
                UpdateProgress(BackupProgress, $"Lỗi xuất dữ liệu: {ex.Message}");
                WriteLog($"[LỖI NGHIÊM TRỌNG] {ex.StackTrace}");
            }
            finally
            {
                IsBackingUp = false;
                EndBusy();
            }
        }

        // ================= THUẬT TOÁN 3: PHỤC HỒI DỮ LIỆU (RESTORE) =================
        private async Task ExecuteRestoreAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Zip File (*.zip)|*.zip",
                Title = "Chọn file Full Backup (.zip) để phục hồi"
            };

            if (dialog.ShowDialog() != true) return;
            string zipPath = dialog.FileName;

            var confirm = MessageBox.Show("CẢNH BÁO: Quá trình phục hồi sẽ GHI ĐÈ mọi dữ liệu hiện tại. Ứng dụng sẽ cần khởi động lại. Tiếp tục?", "Phục hồi dữ liệu", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            IsBackingUp = true;
            BeginBusy("Phục hồi dữ liệu (Restore)");
            Application.Current.Dispatcher.Invoke(() => OperationLogs.Clear());
            IsLogExpanded = true; // Tự động mở khay Log

            UpdateProgress(5, "Đang chuẩn bị giải nén và nạp dữ liệu...");

            try
            {
                await Task.Run(() =>
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string tempDir = Path.Combine(Path.GetTempPath(), "SRM_Restore_" + Guid.NewGuid().ToString());
                    WriteLog($"Tạo thư mục giải nén tạm tại: {tempDir}");

                    UpdateProgress(20, "Đang giải nén gói Backup...");
                    WriteLog($"Bắt đầu đọc cấu trúc Zip: {zipPath}");
                    ZipFile.ExtractToDirectory(zipPath, tempDir, true);
                    WriteLog("Đã bung nén xong file cài đặt.");

                    UpdateProgress(60, "Đang nạp Database...");
                    string extractedDb = Directory.GetFiles(tempDir, "*.db").FirstOrDefault();
                    if (extractedDb != null)
                    {
                        string currentDb = FindDatabaseFile(baseDir) ?? Path.Combine(baseDir, "Database", "library.db");
                        Directory.CreateDirectory(Path.GetDirectoryName(currentDb));

                        string deadDb = currentDb + ".dead";
                        WriteLog($"Đang vô hiệu hóa DB cũ thành: {deadDb}");
                        if (File.Exists(deadDb)) File.Delete(deadDb);
                        if (File.Exists(currentDb)) File.Move(currentDb, deadDb);

                        File.Copy(extractedDb, currentDb, true);
                        WriteLog("Nạp Database thành công.");

                        // MỚI: DB vừa restore có thể là backup CŨ, thiếu bảng/cột mới nhất
                        // (ví dụ: chưa có tính năng đính kèm tài nguyên) -> tự động vá lại ngay tại đây
                        WriteLog("Đang kiểm tra & đồng bộ cấu trúc dữ liệu (schema)...");
                        try
                        {
                            _schemaMigrator.EnsureSchemaAsync().GetAwaiter().GetResult();
                            WriteLog("Đã đồng bộ cấu trúc dữ liệu lên phiên bản mới nhất.");
                        }
                        catch (Exception schemaEx)
                        {
                            WriteLog($"[LỖI] Đồng bộ schema thất bại: {schemaEx.Message}");
                            // Không throw tiếp — vẫn giữ nguyên DB đã restore, chỉ cảnh báo,
                            // để người dùng không mất dữ liệu vừa phục hồi chỉ vì bước vá schema lỗi.
                        }
                    }

                    UpdateProgress(70, "Đang nạp các bộ cài và hình ảnh (Data)...");
                    string extractedData = Path.Combine(tempDir, "Data");
                    if (Directory.Exists(extractedData))
                    {
                        string currentData = Path.Combine(baseDir, "Data");
                        string deadData = currentData + "_dead";

                        WriteLog("Vô hiệu hóa thư mục Data cũ...");
                        if (Directory.Exists(deadData)) Directory.Delete(deadData, true);
                        if (Directory.Exists(currentData)) Directory.Move(currentData, deadData);

                        WriteLog("Đang đồng bộ Data mới...");
                        CopyDirectoryRecursively(extractedData, currentData);
                        if (Directory.Exists(deadData)) Directory.Delete(deadData, true);
                    }

                    UpdateProgress(85, "Đang nạp thư viện tài nguyên (Library)...");
                    string extractedLibrary = Path.Combine(tempDir, "Library");
                    if (Directory.Exists(extractedLibrary))
                    {
                        string currentLibrary = Path.Combine(baseDir, "Library");
                        string deadLibrary = currentLibrary + "_dead";

                        WriteLog("Vô hiệu hóa thư viện Library cũ...");
                        if (Directory.Exists(deadLibrary)) Directory.Delete(deadLibrary, true);
                        if (Directory.Exists(currentLibrary)) Directory.Move(currentLibrary, deadLibrary);

                        WriteLog("Đang đồng bộ Library mới...");
                        CopyDirectoryRecursively(extractedLibrary, currentLibrary);
                        if (Directory.Exists(deadLibrary)) Directory.Delete(deadLibrary, true);
                    }

                    UpdateProgress(95, "Dọn dẹp rác giải nén...");
                    Directory.Delete(tempDir, true);
                    WriteLog("Đã làm sạch môi trường hoạt động.");
                });

                UpdateProgress(100, "Phục hồi thành công!");
                MessageBox.Show("Đã phục hồi dữ liệu thành công! Vui lòng KHỞI ĐỘNG LẠI phần mềm để nhận dữ liệu mới.", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateProgress(BackupProgress, $"Lỗi phục hồi: {ex.Message}");
                WriteLog($"[LỖI NGHIÊM TRỌNG] {ex.StackTrace}");
                MessageBox.Show($"Quá trình phục hồi gặp lỗi hoặc file đang bị khóa. Vui lòng thử lại sau khi tắt mở lại app.\nChi tiết: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBackingUp = false;
                EndBusy();
            }
        }

        // ================= THUẬT TOÁN 4: RESET HỆ THỐNG (LÀM SẠCH) =================
        private async Task ExecuteResetAsync()
        {
            var confirm = MessageBox.Show("CẢNH BÁO ĐỎ: Toàn bộ danh sách phần mềm, file tài nguyên và hình ảnh sẽ bị XÓA SẠCH VĨNH VIỄN. Bạn có chắc chắn không?", "Reset Hệ Thống", MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (confirm != MessageBoxResult.Yes) return;

            IsBackingUp = true;
            BeginBusy("Làm sạch hệ thống (Reset)");
            Application.Current.Dispatcher.Invoke(() => OperationLogs.Clear());
            IsLogExpanded = true; // Tự động mở khay Log

            UpdateProgress(10, "Đang chuẩn bị dọn dẹp hệ thống...");

            try
            {
                await Task.Run(() =>
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                    UpdateProgress(30, "Đang xóa Database...");
                    string dbFile = FindDatabaseFile(baseDir);
                    if (!string.IsNullOrEmpty(dbFile))
                    {
                        string deadDb = dbFile + ".dead";
                        WriteLog($"Phát hiện Database tại: {dbFile}");
                        if (File.Exists(deadDb)) File.Delete(deadDb);

                        WriteLog("Giải phóng kết nối SQLite Pools trong RAM...");
                        System.Data.SQLite.SQLiteConnection.ClearAllPools();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        File.Move(dbFile, deadDb);
                        if (File.Exists(deadDb)) File.Delete(deadDb);
                        WriteLog("Đã hủy file DB cũ.");

                        WriteLog("Đang chạy luồng tạo Database mới...");
                        Database.DatabaseBootstrapper.InitializeDatabase(_logService);
                    }

                    UpdateProgress(50, "Đang xóa thư mục Data bộ cài...");
                    string dataDir = Path.Combine(baseDir, "Data");
                    if (Directory.Exists(dataDir))
                    {
                        string deadData = dataDir + "_dead";
                        WriteLog("Vô hiệu hóa thư mục Data...");
                        if (Directory.Exists(deadData)) Directory.Delete(deadData, true);
                        Directory.Move(dataDir, deadData);
                        Directory.Delete(deadData, true);
                        WriteLog("Đã làm sạch cấu trúc Data.");
                    }

                    UpdateProgress(75, "Đang xóa thư viện tài nguyên (Library)...");
                    string libraryDir = Path.Combine(baseDir, "Library");
                    if (Directory.Exists(libraryDir))
                    {
                        string deadLibrary = libraryDir + "_dead";
                        WriteLog("Vô hiệu hóa thư mục Library...");
                        if (Directory.Exists(deadLibrary)) Directory.Delete(deadLibrary, true);
                        Directory.Move(libraryDir, deadLibrary);
                        Directory.Delete(deadLibrary, true);
                        WriteLog("Đã làm sạch cấu trúc Library.");
                    }
                });

                UpdateProgress(100, "Đã Reset thành công!");
                WriteLog("Quy trình hoàn tất 100%. Sẵn sàng phục vụ.");
                MessageBox.Show("Hệ thống đã được dọn sạch hoàn toàn! Database mới đã sẵn sàng để sử dụng ngay.", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateProgress(BackupProgress, $"Lỗi Reset: {ex.Message}");
                WriteLog($"[LỖI NGHIÊM TRỌNG] {ex.StackTrace}");
            }
            finally
            {
                IsBackingUp = false;
                EndBusy();
            }
        }

        // ================= HÀM HỖ TRỢ ĐỒNG BỘ LOG & TIẾN ĐỘ =================

        private void WriteLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                OperationLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            });

            // Ghi song song xuống file log để tab "Nhật ký hoạt động" đọc được
            if (message.StartsWith("[LỖI", StringComparison.OrdinalIgnoreCase))
                _logService?.LogError(message);
            else
                _logService?.LogInfo(message);
        }

        private void UpdateProgress(double progress, string status)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                BackupProgress = progress;
                BackupStatus = status;
                OperationLogs.Add($"[{DateTime.Now:HH:mm:ss}] {status}");
            });

            _logService?.LogInfo(status);
        }

        private void CopyDirectoryRecursively(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                string destDir = Path.Combine(targetDir, Path.GetFileName(directory));
                CopyDirectoryRecursively(directory, destDir);
            }
        }
    }
}
