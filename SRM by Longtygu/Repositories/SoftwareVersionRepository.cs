using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using SRM_by_Longtygu.Database;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Repositories
{
    public class SoftwareVersionRepository : ISoftwareVersionRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public SoftwareVersionRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<SoftwareVersion>> GetAllAsync()
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "SELECT * FROM SoftwareVersion;";
            return await connection.QueryAsync<SoftwareVersion>(sql);
        }

        public async Task<IEnumerable<SoftwareVersion>> GetBySoftwareIdAsync(int softwareId)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "SELECT * FROM SoftwareVersion WHERE SoftwareId = @SoftwareId ORDER BY CreatedDate DESC";
            return await connection.QueryAsync<SoftwareVersion>(sql, new { SoftwareId = softwareId });
        }

        public async Task<int> InsertAsync(SoftwareVersion version)
        {
            version.CreatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
    INSERT INTO SoftwareVersion (SoftwareId, Version, FilePath, FileSize, SHA256, IsPortable, CreatedDate) 
    VALUES (@SoftwareId, @Version, @FilePath, @FileSize, @SHA256, @IsPortable, @CreatedDate);
    SELECT last_insert_rowid();";
            return await connection.ExecuteScalarAsync<int>(sql, version);
        }
        // BỔ SUNG HÀM NÀY VÀO TRONG CLASS
        public async Task<bool> DeleteAsync(int id)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "DELETE FROM SoftwareVersion WHERE Id = @Id";
            int rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });
            return rowsAffected > 0;
        }

    }
}