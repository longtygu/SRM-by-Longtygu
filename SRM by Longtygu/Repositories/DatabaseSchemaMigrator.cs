using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using SRM_by_Longtygu.Database;

namespace SRM_by_Longtygu.Repositories
{
    // MỚI: Đồng bộ schema DB cũ (backup cũ) lên schema mới nhất.
    // Chạy an toàn (idempotent) dù DB đã đủ bảng/cột hay chưa - gọi bao nhiêu lần cũng không sao.
    // Gọi ở 2 nơi: (1) lúc App khởi động, (2) ngay sau khi Restore ghi đè file .db.
    public interface IDatabaseSchemaMigrator
    {
        Task EnsureSchemaAsync();
    }

    public class DatabaseSchemaMigrator : IDatabaseSchemaMigrator
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public DatabaseSchemaMigrator(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task EnsureSchemaAsync()
        {
            using var connection = _connectionFactory.CreateConnection();

            // 1) Bảng dùng cho tính năng "Đính kèm tài nguyên" (không có trong backup cũ)
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS ResourceFiles (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT,
                    FileName TEXT,
                    FolderPath TEXT,
                    FileSize INTEGER,
                    CreatedDate TEXT
                );");

            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS SoftwareResourceMapping (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SoftwareId INTEGER NOT NULL,
                    ResourceId INTEGER NOT NULL,
                    UNIQUE(SoftwareId, ResourceId)
                );");

            // 2) Các cột mới trong bảng Software (tính năng check-update)
            await EnsureColumnAsync(connection, "Software", "LatestKnownVersion", "TEXT");
            await EnsureColumnAsync(connection, "Software", "LastCheckedDate", "TEXT");
            await EnsureColumnAsync(connection, "Software", "UpdateSourceType", "TEXT");
            await EnsureColumnAsync(connection, "Software", "UpdateSourceUrl", "TEXT");
            await EnsureColumnAsync(connection, "Software", "UpdateVersionPattern", "TEXT");
            await EnsureColumnAsync(connection, "Software", "UpdateDownloadPattern", "TEXT");

            // 3) Phòng hờ các cột trong SoftwareVersion (nếu có backup rất cũ)
            await EnsureColumnAsync(connection, "SoftwareVersion", "SHA256", "TEXT");
            await EnsureColumnAsync(connection, "SoftwareVersion", "IsPortable", "INTEGER");

            // MỚI: Cột lưu phiên bản cụ thể mà người dùng đã chọn cho từng phần mềm trong 1 Preset.
            // NULL = chưa từng lưu version cụ thể (dữ liệu cũ) -> Repository sẽ tự fallback về bản mới nhất.
            await EnsureColumnAsync(connection, "PresetSoftwareMapping", "VersionId", "INTEGER");

            // TODO: sau này thêm bảng/cột mới -> chỉ cần thêm 1 dòng EnsureColumnAsync
            // hoặc 1 khối CREATE TABLE IF NOT EXISTS tương tự ở đây.
        }

        private async Task EnsureColumnAsync(IDbConnection connection, string table, string column, string sqliteType)
        {
            // Bảng chưa tồn tại thì bỏ qua (trường hợp cực hiếm, backup lỗi nặng hơn schema)
            var tableExists = await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@Table", new { Table = table });
            if (tableExists == 0) return;

            var existingColumns = await connection.QueryAsync<string>(
                $"SELECT name FROM pragma_table_info('{table}')");

            if (!existingColumns.Any(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase)))
            {
                await connection.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {sqliteType}");
            }
        }
    }
}