using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using SRM_by_Longtygu.Database;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services
{
    public interface IDashboardService
    {
        Task<int> GetTotalSoftwareAsync();
        Task<int> GetTotalVersionsAsync();
        Task<long> GetTotalStorageUsedAsync();
        Task<IEnumerable<Software>> GetRecentSoftwareAsync(int limit = 5);
    }

    public class DashboardService : IDashboardService
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public DashboardService(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<int> GetTotalSoftwareAsync()
        {
            using var connection = _connectionFactory.CreateConnection();
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(Id) FROM Software");
        }

        public async Task<int> GetTotalVersionsAsync()
        {
            using var connection = _connectionFactory.CreateConnection();
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(Id) FROM SoftwareVersion");
        }

        public async Task<long> GetTotalStorageUsedAsync()
        {
            using var connection = _connectionFactory.CreateConnection();
            // Hàm COALESCE giúp trả về 0 nếu bảng rỗng, tránh lỗi null
            return await connection.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(FileSize), 0) FROM SoftwareVersion");
        }

        public async Task<IEnumerable<Software>> GetRecentSoftwareAsync(int limit = 5)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "SELECT * FROM Software ORDER BY CreatedDate DESC LIMIT @Limit";
            return await connection.QueryAsync<Software>(sql, new { Limit = limit });
        }
    }
}