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
        private readonly ILocalizationService _loc; // MỚI: đổi ngôn ngữ ngay trong app
        private bool _statusIsDefault = true;       // true = đang hiện chữ "Sẵn sàng" mặc định (được dịch lại khi đổi ngôn ngữ)

        // ================= CÁC BIẾN UI CHO THANH TIẾN ĐỘ & LOG =================
        private string _backupStatus = Loc.T("Settings_Ready");
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

        // ================= NGÔN NGỮ (ComboBox ở trang Cài đặt) =================
        public System.Collections.Generic.IReadOnlyList<LanguageOption> SupportedLanguages => _loc.SupportedLanguages;

        private LanguageOption? _selectedLanguage;
        public LanguageOption? SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (value == null || ReferenceEquals(value, _selectedLanguage)) return;
                SetProperty(ref _selectedLanguage, value);
                if (!string.Equals(value.Code, _loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    _loc.SetLanguage(value.Code); // đổi ngay + lưu settings.json
                }
            }
        }

        // ================= KHAI BÁO LỆNH =================
        public ICommand ToggleLanguageCommand { get; }
        public ICommand BackupDbCommand { get; }
        public ICommand FullBackupCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand ResetCommand { get; }

        public SettingsViewModel(ILogService logService, IDatabaseSchemaMigrator schemaMigrator, IBusyService busyService, ILocalizationService localization)
        {
            _logService = logService;
            _schemaMigrator = schemaMigrator; // MỚI
            _busy = busyService;
            _loc = localization;

            ToggleLanguageCommand = new RelayCommand(_ => ToggleLanguage());

            // MỚI: chọn sẵn ngôn ngữ đang dùng và đồng bộ khi ngôn ngữ đổi từ nơi khác
            _selectedLanguage = _loc.SupportedLanguages.FirstOrDefault(l => string.Equals(l.Code, _loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase));
            _loc.LanguageChanged += OnLanguageChanged;

            BackupDbCommand = new RelayCommand(async _ => await ExecuteBackupDbAsync(), _ => CanStartOperation());
            FullBackupCommand = new RelayCommand(async _ => await ExecuteFullBackupAsync(), _ => CanStartOperation());
            RestoreCommand = new RelayCommand(async _ => await ExecuteRestoreAsync(), _ => CanStartOperation());
            ResetCommand = new RelayCommand(async _ => await ExecuteResetAsync(), _ => CanStartOperation());
        }

        private void ToggleLanguage()
        {
            _loc.ToggleLanguage();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            var current = _loc.SupportedLanguages.FirstOrDefault(l => string.Equals(l.Code, _loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase));
            if (current != null && !ReferenceEquals(current, _selectedLanguage))
            {
                SetProperty(ref _selectedLanguage, current);
            }
            // Chữ trạng thái mặc định "Sẵn sàng" được dịch lại; thông báo của lần chạy trước giữ nguyên
            if (_statusIsDefault)
            {
                BackupStatus = Loc.T("Settings_Ready");
            }
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
                MessageBox.Show(Loc.T("Settings_DbNotFound"), Loc.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsBackingUp = true;
            BeginBusy(Loc.T("Settings_OpBackupDb"));
            UpdateProgress(40, Loc.F("Settings_BackupFound", Path.GetFileName(dbFile)));

            try
            {
                string backupDir = Path.Combine(baseDir, "Backups");
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                    WriteLog(Loc.F("Settings_BackupDirCreated", backupDir));
                }

                string dbName = Path.GetFileNameWithoutExtension(dbFile);
                string backupPath = Path.Combine(backupDir, $"{dbName}_Backup_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(dbFile)}");

                WriteLog(Loc.F("Settings_BackupCopying", dbFile));
                await Task.Run(() => File.Copy(dbFile, backupPath, true));
                WriteLog(Loc.F("Settings_BackupCopied", backupPath));

                UpdateProgress(100, Loc.F("Settings_BackupDone", Path.GetFileName(backupPath)));
            }
            catch (Exception ex)
            {
                UpdateProgress(BackupProgress, Loc.F("Settings_BackupFailed", ex.Message));
                WriteLog(Loc.F("Settings_FatalError", ex.Message), true);
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
                MessageBox.Show(Loc.T("Settings_DbNotFoundForPack"), Loc.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string appName = new DirectoryInfo(baseDir).Name.Replace(" ", "");
            if (string.IsNullOrWhiteSpace(appName)) appName = "SRM";

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Zip File (*.zip)|*.zip",
                FileName = $"{appName}_FullBackup_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                Title = Loc.T("Settings_FullBackupSaveTitle")
            };

            if (dialog.ShowDialog() != true) return;

            string zipPath = dialog.FileName;
            IsBackingUp = true;
            BeginBusy(Loc.T("Settings_OpFullBackup"));

            Application.Current.Dispatcher.Invoke(() => OperationLogs.Clear());
            IsLogExpanded = true; // Tự động mở khay Log

            UpdateProgress(5, Loc.T("Settings_FullInit"));

            try
            {
                await Task.Run(() =>
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "SRM_Backup_" + Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDir);
                    WriteLog(Loc.F("Settings_FullTempDir", tempDir));

                    UpdateProgress(10, Loc.F("Settings_FullCopyDb", Path.GetFileName(dbFile)));
                    File.Copy(dbFile, Path.Combine(tempDir, Path.GetFileName(dbFile)), true);
                    WriteLog(Loc.T("Settings_FullCopyDbDone"));

                    UpdateProgress(30, Loc.T("Settings_FullCopyData"));
                    string sourceDataDir = Path.Combine(baseDir, "Data");
                    if (Directory.Exists(sourceDataDir))
                    {
                        string destDataDir = Path.Combine(tempDir, "Data");
                        WriteLog(Loc.F("Settings_ProcessingFolder", sourceDataDir));
                        CopyDirectoryRecursively(sourceDataDir, destDataDir);
                    }
                    else WriteLog(Loc.T("Settings_DataMissingSkip"));

                    UpdateProgress(60, Loc.T("Settings_FullCopyLibrary"));
                    string sourceLibraryDir = Path.Combine(baseDir, "Library");
                    if (Directory.Exists(sourceLibraryDir))
                    {
                        string destLibraryDir = Path.Combine(tempDir, "Library");
                        WriteLog(Loc.F("Settings_ProcessingFolder", sourceLibraryDir));
                        CopyDirectoryRecursively(sourceLibraryDir, destLibraryDir);
                    }
                    else WriteLog(Loc.T("Settings_LibraryMissingSkip"));

                    UpdateProgress(85, Loc.T("Settings_FullZipping"));
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                        WriteLog(Loc.T("Settings_FullOverwroteZip"));
                    }
                    WriteLog(Loc.F("Settings_FullZipStart", zipPath));
                    ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Fastest, false);
                    WriteLog(Loc.T("Settings_FullZipDone"));

                    UpdateProgress(95, Loc.T("Settings_FullCleaning"));
                    Directory.Delete(tempDir, true);
                    WriteLog(Loc.T("Settings_FullCleaned"));
                });

                UpdateProgress(100, Loc.F("Settings_FullDone", Path.GetFileName(zipPath)));
            }
            catch (Exception ex)
            {
                UpdateProgress(BackupProgress, Loc.F("Settings_FullFailed", ex.Message));
                WriteLog(Loc.F("Settings_FatalError", ex.StackTrace), true);
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
                Title = Loc.T("Settings_RestoreOpenTitle")
            };

            if (dialog.ShowDialog() != true) return;
            string zipPath = dialog.FileName;

            var confirm = MessageBox.Show(Loc.T("Settings_RestoreConfirm"), Loc.T("Settings_RestoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            IsBackingUp = true;
            BeginBusy(Loc.T("Settings_OpRestore"));
            Application.Current.Dispatcher.Invoke(() => OperationLogs.Clear());
            IsLogExpanded = true; // Tự động mở khay Log

            UpdateProgress(5, Loc.T("Settings_RestorePrepare"));

            try
            {
                await Task.Run(() =>
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string tempDir = Path.Combine(Path.GetTempPath(), "SRM_Restore_" + Guid.NewGuid().ToString());
                    WriteLog(Loc.F("Settings_RestoreTempDir", tempDir));

                    UpdateProgress(20, Loc.T("Settings_RestoreExtracting"));
                    WriteLog(Loc.F("Settings_RestoreReadZip", zipPath));
                    ZipFile.ExtractToDirectory(zipPath, tempDir, true);
                    WriteLog(Loc.T("Settings_RestoreExtracted"));

                    UpdateProgress(60, Loc.T("Settings_RestoreLoadDb"));
                    string extractedDb = Directory.GetFiles(tempDir, "*.db").FirstOrDefault();
                    if (extractedDb != null)
                    {
                        string currentDb = FindDatabaseFile(baseDir) ?? Path.Combine(baseDir, "Database", "library.db");
                        Directory.CreateDirectory(Path.GetDirectoryName(currentDb));

                        string deadDb = currentDb + ".dead";
                        WriteLog(Loc.F("Settings_RestoreDisableOldDb", deadDb));
                        if (File.Exists(deadDb)) File.Delete(deadDb);
                        if (File.Exists(currentDb)) File.Move(currentDb, deadDb);

                        File.Copy(extractedDb, currentDb, true);
                        WriteLog(Loc.T("Settings_RestoreDbLoaded"));

                        // MỚI: DB vừa restore có thể là backup CŨ, thiếu bảng/cột mới nhất
                        // (ví dụ: chưa có tính năng đính kèm tài nguyên) -> tự động vá lại ngay tại đây
                        WriteLog(Loc.T("Settings_RestoreSchemaCheck"));
                        try
                        {
                            _schemaMigrator.EnsureSchemaAsync().GetAwaiter().GetResult();
                            WriteLog(Loc.T("Settings_RestoreSchemaDone"));
                        }
                        catch (Exception schemaEx)
                        {
                            WriteLog(Loc.F("Settings_RestoreSchemaFailed", schemaEx.Message), true);
                            // Không throw tiếp — vẫn giữ nguyên DB đã restore, chỉ cảnh báo,
                            // để người dùng không mất dữ liệu vừa phục hồi chỉ vì bước vá schema lỗi.
                        }
                    }

                    UpdateProgress(70, Loc.T("Settings_RestoreLoadData"));
                    string extractedData = Path.Combine(tempDir, "Data");
                    if (Directory.Exists(extractedData))
                    {
                        string currentData = Path.Combine(baseDir, "Data");
                        string deadData = currentData + "_dead";

                        WriteLog(Loc.T("Settings_RestoreDisableOldData"));
                        if (Directory.Exists(deadData)) Directory.Delete(deadData, true);
                        if (Directory.Exists(currentData)) Directory.Move(currentData, deadData);

                        WriteLog(Loc.T("Settings_RestoreSyncData"));
                        CopyDirectoryRecursively(extractedData, currentData);
                        if (Directory.Exists(deadData)) Directory.Delete(deadData, true);
                    }

                    UpdateProgress(85, Loc.T("Settings_RestoreLoadLibrary"));
                    string extractedLibrary = Path.Combine(tempDir, "Library");
                    if (Directory.Exists(extractedLibrary))
                    {
                        string currentLibrary = Path.Combine(baseDir, "Library");
                        string deadLibrary = currentLibrary + "_dead";

                        WriteLog(Loc.T("Settings_RestoreDisableOldLibrary"));
                        if (Directory.Exists(deadLibrary)) Directory.Delete(deadLibrary, true);
                        if (Directory.Exists(currentLibrary)) Directory.Move(currentLibrary, deadLibrary);

                        WriteLog(Loc.T("Settings_RestoreSyncLibrary"));
                        CopyDirectoryRecursively(extractedLibrary, currentLibrary);
                        if (Directory.Exists(deadLibrary)) Directory.Delete(deadLibrary, true);
                    }

                    UpdateProgress(95, Loc.T("Settings_RestoreCleanup"));
                    Directory.Delete(tempDir, true);
                    WriteLog(Loc.T("Settings_RestoreCleaned"));
                });

                UpdateProgress(100, Loc.T("Settings_RestoreSuccess"));
                MessageBox.Show(Loc.T("Settings_RestoreDoneMsg"), Loc.T("Common_Done"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateProgress(BackupProgress, Loc.F("Settings_RestoreFailed", ex.Message));
                WriteLog(Loc.F("Settings_FatalError", ex.StackTrace), true);
                MessageBox.Show(Loc.T("Settings_RestoreLockedMsg") + "\n" + Loc.F("Settings_Details", ex.Message), Loc.T("Common_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            var confirm = MessageBox.Show(Loc.T("Settings_ResetConfirm"), Loc.T("Settings_ResetTitle"), MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (confirm != MessageBoxResult.Yes) return;

            IsBackingUp = true;
            BeginBusy(Loc.T("Settings_OpReset"));
            Application.Current.Dispatcher.Invoke(() => OperationLogs.Clear());
            IsLogExpanded = true; // Tự động mở khay Log

            UpdateProgress(10, Loc.T("Settings_ResetPrepare"));

            try
            {
                await Task.Run(() =>
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                    UpdateProgress(30, Loc.T("Settings_ResetDeletingDb"));
                    string dbFile = FindDatabaseFile(baseDir);
                    if (!string.IsNullOrEmpty(dbFile))
                    {
                        string deadDb = dbFile + ".dead";
                        WriteLog(Loc.F("Settings_ResetDbFound", dbFile));
                        if (File.Exists(deadDb)) File.Delete(deadDb);

                        WriteLog(Loc.T("Settings_ResetReleasePools"));
                        System.Data.SQLite.SQLiteConnection.ClearAllPools();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        File.Move(dbFile, deadDb);
                        if (File.Exists(deadDb)) File.Delete(deadDb);
                        WriteLog(Loc.T("Settings_ResetOldDbRemoved"));

                        WriteLog(Loc.T("Settings_ResetCreatingDb"));
                        Database.DatabaseBootstrapper.InitializeDatabase(_logService);
                    }

                    UpdateProgress(50, Loc.T("Settings_ResetDeletingData"));
                    string dataDir = Path.Combine(baseDir, "Data");
                    if (Directory.Exists(dataDir))
                    {
                        string deadData = dataDir + "_dead";
                        WriteLog(Loc.T("Settings_ResetDisableData"));
                        if (Directory.Exists(deadData)) Directory.Delete(deadData, true);
                        Directory.Move(dataDir, deadData);
                        Directory.Delete(deadData, true);
                        WriteLog(Loc.T("Settings_ResetDataCleaned"));
                    }

                    UpdateProgress(75, Loc.T("Settings_ResetDeletingLibrary"));
                    string libraryDir = Path.Combine(baseDir, "Library");
                    if (Directory.Exists(libraryDir))
                    {
                        string deadLibrary = libraryDir + "_dead";
                        WriteLog(Loc.T("Settings_ResetDisableLibrary"));
                        if (Directory.Exists(deadLibrary)) Directory.Delete(deadLibrary, true);
                        Directory.Move(libraryDir, deadLibrary);
                        Directory.Delete(deadLibrary, true);
                        WriteLog(Loc.T("Settings_ResetLibraryCleaned"));
                    }
                });

                UpdateProgress(100, Loc.T("Settings_ResetSuccess"));
                WriteLog(Loc.T("Settings_ResetFinished"));
                MessageBox.Show(Loc.T("Settings_ResetDoneMsg"), Loc.T("Common_Done"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateProgress(BackupProgress, Loc.F("Settings_ResetFailed", ex.Message));
                WriteLog(Loc.F("Settings_FatalError", ex.StackTrace), true);
            }
            finally
            {
                IsBackingUp = false;
                EndBusy();
            }
        }

        // ================= HÀM HỖ TRỢ ĐỒNG BỘ LOG & TIẾN ĐỘ =================

        private void WriteLog(string message, bool isError = false)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                OperationLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            });

            // Ghi song song xuống file log để tab "Nhật ký hoạt động" đọc được
            if (isError)
                _logService?.LogError(message);
            else
                _logService?.LogInfo(message);
        }

        private void UpdateProgress(double progress, string status)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _statusIsDefault = false;
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
