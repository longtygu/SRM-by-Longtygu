using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using SRM_by_Longtygu.Database;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Repositories
{
    // MỚI: Repository quản lý bảng ánh xạ SoftwareResourceMapping (Software <-> ResourceFile)
    public class SoftwareResourceRepository : ISoftwareResourceRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public SoftwareResourceRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<ResourceFile>> GetResourcesForSoftwareAsync(int softwareId)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT r.* 
                FROM ResourceFiles r
                INNER JOIN SoftwareResourceMapping m ON r.Id = m.ResourceId
                WHERE m.SoftwareId = @SoftwareId
                ORDER BY r.Name ASC";
            return await connection.QueryAsync<ResourceFile>(sql, new { SoftwareId = softwareId });
        }

        public async Task<IEnumerable<int>> GetAttachedResourceIdsAsync(int softwareId)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "SELECT ResourceId FROM SoftwareResourceMapping WHERE SoftwareId = @SoftwareId";
            return await connection.QueryAsync<int>(sql, new { SoftwareId = softwareId });
        }

        public async Task AttachAsync(int softwareId, int resourceId)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
                INSERT INTO SoftwareResourceMapping (SoftwareId, ResourceId)
                SELECT @SoftwareId, @ResourceId
                WHERE NOT EXISTS (
                    SELECT 1 FROM SoftwareResourceMapping WHERE SoftwareId = @SoftwareId AND ResourceId = @ResourceId
                );";
            await connection.ExecuteAsync(sql, new { SoftwareId = softwareId, ResourceId = resourceId });
        }

        public async Task AttachManyAsync(int softwareId, IEnumerable<int> resourceIds)
        {
            if (resourceIds == null) return;
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
                INSERT INTO SoftwareResourceMapping (SoftwareId, ResourceId)
                SELECT @SoftwareId, @ResourceId
                WHERE NOT EXISTS (
                    SELECT 1 FROM SoftwareResourceMapping WHERE SoftwareId = @SoftwareId AND ResourceId = @ResourceId
                );";

            foreach (var resourceId in resourceIds.Distinct())
            {
                await connection.ExecuteAsync(sql, new { SoftwareId = softwareId, ResourceId = resourceId });
            }
        }

        public async Task DetachAsync(int softwareId, int resourceId)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = "DELETE FROM SoftwareResourceMapping WHERE SoftwareId = @SoftwareId AND ResourceId = @ResourceId";
            await connection.ExecuteAsync(sql, new { SoftwareId = softwareId, ResourceId = resourceId });
        }
    }
}
