using System;
using System.IO;
using System.Data.SQLite;

namespace SRM_by_Longtygu.Database
{
    public static class DatabaseBootstrapper
    {
        private static readonly string DbFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database");
        private static readonly string DbPath = Path.Combine(DbFolder, "library.db");

        public static string GetConnectionString()
        {
            if (!Directory.Exists(DbFolder))
            {
                Directory.CreateDirectory(DbFolder);
            }
            return $"Data Source={DbPath};Version=3;Foreign Keys=True;";
        }

        public static void InitializeDatabase(SRM_by_Longtygu.Services.ILogService logService = null)
        {
            // Nếu không có logService truyền vào (VD: gọi từ nơi chưa có DI), tự tạo 1 instance tạm để vẫn ghi được log ra file
            logService ??= new SRM_by_Longtygu.Services.LogService();

            try
            {
                string connectionString = GetConnectionString();
                bool isNewDb = !File.Exists(DbPath);

                if (isNewDb)
                {
                    SQLiteConnection.CreateFile(DbPath);
                    logService.LogInfo($"Đã tạo mới file Database tại: {DbPath}");
                }
                else
                {
                    logService.LogInfo($"Đang sử dụng Database sẵn có tại: {DbPath}");
                }

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string createSoftwareTable = @"
                    CREATE TABLE IF NOT EXISTS Software (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Publisher TEXT,
                        Category TEXT,
                        Description TEXT,
                        LicenseKey TEXT,
                        Readme TEXT,
                        IconPath TEXT,
                        SilentInstallCommand TEXT,      
                        SilentUninstallCommand TEXT,    
                        CreatedDate TEXT
                    );";

                    string createVersionTable = @"
                    CREATE TABLE IF NOT EXISTS SoftwareVersion (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SoftwareId INTEGER,
                        Version TEXT,
                        FilePath TEXT,
                        FileSize INTEGER,
                        SHA256 TEXT,
                        IsPortable INTEGER,
                        CreatedDate TEXT,
                        FOREIGN KEY(SoftwareId) REFERENCES Software(Id) ON DELETE CASCADE
                    );";

                    string createPresetsTable = @"
                    CREATE TABLE IF NOT EXISTS Presets (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT,
                        IconPath TEXT,
                        TotalSize TEXT,
                        Description TEXT
                    );";

                    string createPresetSoftwareMappingTable = @"
                    CREATE TABLE IF NOT EXISTS PresetSoftwareMapping (
                        PresetId INTEGER,
                        SoftwareId INTEGER,
                        FOREIGN KEY(PresetId) REFERENCES Presets(Id) ON DELETE CASCADE,
                        FOREIGN KEY(SoftwareId) REFERENCES Software(Id) ON DELETE CASCADE
                    );";

                    string createLogsTable = @"
                    CREATE TABLE IF NOT EXISTS InstallLogs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SoftwareVersionId INTEGER,
                        Status TEXT,
                        StartTime TEXT,
                        EndTime TEXT,
                        Message TEXT
                    );";

                    // ĐÃ THÊM: Tạo bảng ResourceFiles cho module Thư viện Tài nguyên
                    string createResourceFilesTable = @"
                    CREATE TABLE IF NOT EXISTS ResourceFiles (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT,
                        Category TEXT,
                        FolderPath TEXT,
                        FileName TEXT,
                        Size TEXT,
                        Description TEXT,
                        IconPath TEXT,
                        AddedDate TEXT
                    );";

                    // MỚI: Bảng ánh xạ N-N giữa Software và ResourceFiles
                    // (Phần mềm nào đang gắn kèm những tài nguyên nào: ISO, DLL, driver...)
                    string createSoftwareResourceMappingTable = @"
                    CREATE TABLE IF NOT EXISTS SoftwareResourceMapping (
                        SoftwareId INTEGER NOT NULL,
                        ResourceId INTEGER NOT NULL,
                        PRIMARY KEY (SoftwareId, ResourceId),
                        FOREIGN KEY(SoftwareId) REFERENCES Software(Id) ON DELETE CASCADE,
                        FOREIGN KEY(ResourceId) REFERENCES ResourceFiles(Id) ON DELETE CASCADE
                    );";

                    using (var command = new SQLiteCommand(connection))
                    {
                        command.CommandText = createSoftwareTable; command.ExecuteNonQuery();
                        command.CommandText = createVersionTable; command.ExecuteNonQuery();
                        command.CommandText = createPresetsTable; command.ExecuteNonQuery();
                        command.CommandText = createPresetSoftwareMappingTable; command.ExecuteNonQuery();
                        command.CommandText = createLogsTable; command.ExecuteNonQuery();

                        // Thực thi tạo bảng mới
                        command.CommandText = createResourceFilesTable; command.ExecuteNonQuery();

                        // MỚI: Thực thi tạo bảng ánh xạ Software <-> ResourceFiles
                        command.CommandText = createSoftwareResourceMappingTable; command.ExecuteNonQuery();
                    }

                    // ===== MỚI: MIGRATION AN TOÀN CHO TÍNH NĂNG KIỂM TRA CẬP NHẬT =====
                    // Dùng PRAGMA table_info thay vì ALTER TABLE ... ADD COLUMN IF NOT EXISTS,
                    // vì cú pháp IF NOT EXISTS chỉ có ở SQLite 3.35+ và có thể chưa được driver hỗ trợ.
                    // Cách này chạy an toàn cả trên DB cũ (đã có dữ liệu) lẫn DB mới tạo.
                    EnsureColumnExists(connection, "Software", "UpdateSourceType", "TEXT DEFAULT 'None'");
                    EnsureColumnExists(connection, "Software", "UpdateSourceUrl", "TEXT");
                    EnsureColumnExists(connection, "Software", "UpdateVersionPattern", "TEXT");
                    EnsureColumnExists(connection, "Software", "UpdateDownloadPattern", "TEXT");
                    EnsureColumnExists(connection, "Software", "LatestKnownVersion", "TEXT");
                    EnsureColumnExists(connection, "Software", "LastCheckedDate", "TEXT");
                }

                logService.LogInfo("Đã khởi tạo/xác nhận đầy đủ cấu trúc bảng trong Database (Software, SoftwareVersion, Presets, InstallLogs, ResourceFiles, SoftwareResourceMapping).");
            }
            catch (Exception ex)
            {
                logService.LogError("Lỗi nghiêm trọng khi khởi tạo Database", ex);
                throw; // Ném lại lỗi để nơi gọi (App.xaml.cs / SettingsViewModel) vẫn xử lý được như cũ
            }
        }

        // MỚI: Kiểm tra cột đã tồn tại chưa trước khi ALTER TABLE, tránh lỗi "duplicate column name"
        // khi InitializeDatabase() chạy lại nhiều lần trên cùng 1 DB đã có sẵn dữ liệu người dùng.
        private static void EnsureColumnExists(SQLiteConnection connection, string tableName, string columnName, string columnDefinitionSql)
        {
            bool exists = false;
            using (var checkCmd = new SQLiteCommand($"PRAGMA table_info({tableName});", connection))
            using (var reader = checkCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (!exists)
            {
                using var alterCmd = new SQLiteCommand($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinitionSql};", connection);
                alterCmd.ExecuteNonQuery();
            }
        }
    }
}
