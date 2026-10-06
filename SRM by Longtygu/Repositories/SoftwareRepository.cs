using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using SRM_by_Longtygu.Database;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Repositories
{
    public class SoftwareRepository : ISoftwareRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public SoftwareRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<Software>> GetAllAsync()
        {
            using var connection = _connectionFactory.CreateConnection();

            // JOIN bảng Software và bảng SoftwareVersion (Lưu ý kiểm tra lại tên bảng SoftwareVersion trong DB của bạn xem có chữ 's' không nhé, tôi đang giả định là không có 's' dựa theo bảng Software)
            string sql = @"
        SELECT s.*, v.* 
        FROM Software s
        LEFT JOIN SoftwareVersion v ON s.Id = v.SoftwareId
        ORDER BY s.Name ASC";

            var softwareDict = new Dictionary<int, Software>();

            var softwares = await connection.QueryAsync<Software, SoftwareVersion, Software>(
                sql,
                (software, version) =>
                {
                    // Nếu phần mềm chưa có trong từ điển thì thêm vào
                    if (!softwareDict.TryGetValue(software.Id, out var currentSoftware))
                    {
                        currentSoftware = software;
                        softwareDict.Add(currentSoftware.Id, currentSoftware);
                    }

                    // Nếu phần mềm có phiên bản đi kèm thì nhét vào danh sách Versions
                    if (version != null && version.Id > 0)
                    {
                        currentSoftware.Versions.Add(version);
                    }

                    return currentSoftware;
                }
            );

            // Trả về danh sách các phần mềm (đã được gom gọn, không bị lặp dòng)
            return softwareDict.Values;
        }

        public async Task<Software> GetByIdAsync(int id)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "SELECT * FROM Software WHERE Id = @Id";
            return await connection.QuerySingleOrDefaultAsync<Software>(sql, new { Id = id });
        }

        public async Task<Software> GetByNameAsync(string name)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "SELECT * FROM Software WHERE Name = @Name COLLATE NOCASE LIMIT 1;";
            return await connection.QueryFirstOrDefaultAsync<Software>(sql, new { Name = name });
        }

        public async Task<int> InsertAsync(Software software)
        {
            software.CreatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
    INSERT INTO Software (Name, Publisher, Category, Description, LicenseKey, Readme, IconPath, SilentInstallCommand, SilentUninstallCommand, CreatedDate) 
    VALUES (@Name, @Publisher, @Category, @Description, @LicenseKey, @Readme, @IconPath, @SilentInstallCommand, @SilentUninstallCommand, @CreatedDate);
    SELECT last_insert_rowid();";
            return await connection.ExecuteScalarAsync<int>(sql, software);
        }

        public async Task<bool> UpdateAsync(Software software)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
                UPDATE Software 
                SET Name = @Name, Publisher = @Publisher, Category = @Category, Description = @Description, 
                    LicenseKey = @LicenseKey, Readme = @Readme, IconPath = @IconPath
                WHERE Id = @Id";
            int rowsAffected = await connection.ExecuteAsync(sql, software);
            return rowsAffected > 0;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "DELETE FROM Software WHERE Id = @Id";
            int rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });
            return rowsAffected > 0;
        }

        public async Task<IEnumerable<Software>> SearchAsync(string keyword)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT * FROM Software 
                WHERE Name LIKE @Keyword OR Publisher LIKE @Keyword OR Category LIKE @Keyword
                ORDER BY Name ASC";
            return await connection.QueryAsync<Software>(sql, new { Keyword = $"%{keyword}%" });
        }

        public async Task CleanUpOrphanedDataAsync()
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "DELETE FROM SoftwareVersion WHERE SoftwareId NOT IN (SELECT Id FROM Software)";
            await connection.ExecuteAsync(sql);
        }

        // MỚI: Lưu kết quả lần quét cập nhật gần nhất
        public async Task<bool> UpdateUpdateCheckInfoAsync(int softwareId, string latestKnownVersion, DateTime checkedDate)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
                UPDATE Software 
                SET LatestKnownVersion = @LatestKnownVersion, LastCheckedDate = @LastCheckedDate
                WHERE Id = @Id";
            int rowsAffected = await connection.ExecuteAsync(sql, new
            {
                LatestKnownVersion = latestKnownVersion,
                LastCheckedDate = checkedDate.ToString("yyyy-MM-dd HH:mm:ss"),
                Id = softwareId
            });
            return rowsAffected > 0;
        }

        // MỚI: Lưu cấu hình nguồn kiểm tra cập nhật cho 1 phần mềm
        public async Task<bool> UpdateUpdateSourceConfigAsync(Software software)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
                UPDATE Software 
                SET UpdateSourceType = @UpdateSourceType, 
                    UpdateSourceUrl = @UpdateSourceUrl,
                    UpdateVersionPattern = @UpdateVersionPattern,
                    UpdateDownloadPattern = @UpdateDownloadPattern
                WHERE Id = @Id";
            int rowsAffected = await connection.ExecuteAsync(sql, software);
            return rowsAffected > 0;
        }
    }
}
