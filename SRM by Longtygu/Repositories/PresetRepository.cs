using SRM_by_Longtygu.Database;
using SRM_by_Longtygu.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SRM_by_Longtygu.Repositories
{
    public class PresetRepository : IPresetRepository
    {
        private readonly IDatabaseConnectionFactory _connectionFactory;

        public PresetRepository(IDatabaseConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task AddPresetAsync(Preset preset, Dictionary<int, int?> softwareVersionSelections)
        {
            using (var connection = _connectionFactory.CreateConnection()) // Hoặc CreateConnection() tùy cách bạn đặt tên
            {
                if (connection.State != ConnectionState.Open) connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 1. Thêm Preset mới và lấy ID vừa tạo
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"
                            INSERT INTO Presets (Name, IconPath, TotalSize) 
                            VALUES (@Name, @IconPath, @TotalSize);
                            SELECT last_insert_rowid();";

                        var pName = cmd.CreateParameter(); pName.ParameterName = "@Name"; pName.Value = preset.Name; cmd.Parameters.Add(pName);
                        var pIcon = cmd.CreateParameter(); pIcon.ParameterName = "@IconPath"; pIcon.Value = preset.IconPath ?? (object)DBNull.Value; cmd.Parameters.Add(pIcon);
                        var pSize = cmd.CreateParameter(); pSize.ParameterName = "@TotalSize"; pSize.Value = preset.TotalSize ?? "0 MB"; cmd.Parameters.Add(pSize);

                        var presetIdObj = await Task.Run(() => cmd.ExecuteScalar());
                        int newPresetId = Convert.ToInt32(presetIdObj);

                        // 2. Map các Software (kèm Version đã chọn) vào Preset (Nhiều - Nhiều)
                        await InsertSoftwareMappingsAsync(connection, transaction, newPresetId, softwareVersionSelections);

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        // MỚI: Dùng khi Sửa Preset - cập nhật tên/icon rồi xóa map cũ, ghi lại map mới (kèm VersionId đã chọn)
        public async Task UpdatePresetAsync(Preset preset, Dictionary<int, int?> softwareVersionSelections)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                if (connection.State != ConnectionState.Open) connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var updateCmd = connection.CreateCommand();
                        updateCmd.Transaction = transaction;
                        updateCmd.CommandText = "UPDATE Presets SET Name = @Name, IconPath = @IconPath WHERE Id = @Id";
                        var pName = updateCmd.CreateParameter(); pName.ParameterName = "@Name"; pName.Value = preset.Name; updateCmd.Parameters.Add(pName);
                        var pIcon = updateCmd.CreateParameter(); pIcon.ParameterName = "@IconPath"; pIcon.Value = preset.IconPath ?? (object)DBNull.Value; updateCmd.Parameters.Add(pIcon);
                        var pId = updateCmd.CreateParameter(); pId.ParameterName = "@Id"; pId.Value = preset.Id; updateCmd.Parameters.Add(pId);
                        await Task.Run(() => updateCmd.ExecuteNonQuery());

                        var deleteCmd = connection.CreateCommand();
                        deleteCmd.Transaction = transaction;
                        deleteCmd.CommandText = "DELETE FROM PresetSoftwareMapping WHERE PresetId = @PId";
                        var dpId = deleteCmd.CreateParameter(); dpId.ParameterName = "@PId"; dpId.Value = preset.Id; deleteCmd.Parameters.Add(dpId);
                        await Task.Run(() => deleteCmd.ExecuteNonQuery());

                        await InsertSoftwareMappingsAsync(connection, transaction, preset.Id, softwareVersionSelections);

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        // MỚI: Logic dùng chung cho cả AddPresetAsync và UpdatePresetAsync để tránh lặp code
        private async Task InsertSoftwareMappingsAsync(IDbConnection connection, IDbTransaction transaction, int presetId, Dictionary<int, int?> softwareVersionSelections)
        {
            if (softwareVersionSelections == null || softwareVersionSelections.Count == 0) return;

            foreach (var kvp in softwareVersionSelections)
            {
                var mapCmd = connection.CreateCommand();
                mapCmd.Transaction = transaction;
                mapCmd.CommandText = "INSERT INTO PresetSoftwareMapping (PresetId, SoftwareId, VersionId) VALUES (@PId, @SId, @VId)";

                var pId = mapCmd.CreateParameter(); pId.ParameterName = "@PId"; pId.Value = presetId; mapCmd.Parameters.Add(pId);
                var sId = mapCmd.CreateParameter(); sId.ParameterName = "@SId"; sId.Value = kvp.Key; mapCmd.Parameters.Add(sId);
                var vId = mapCmd.CreateParameter(); vId.ParameterName = "@VId"; vId.Value = (object)kvp.Value ?? DBNull.Value; mapCmd.Parameters.Add(vId);

                await Task.Run(() => mapCmd.ExecuteNonQuery());
            }
        }

        public async Task<IEnumerable<Preset>> GetAllPresetsAsync()
        {
            var presets = new List<Preset>();
            using (var connection = _connectionFactory.CreateConnection())
            {
                if (connection.State != ConnectionState.Open) connection.Open();

                // Lấy danh sách Preset
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, IconPath, TotalSize FROM Presets";
                using (var reader = await Task.Run(() => cmd.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        presets.Add(new Preset
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Name = reader["Name"].ToString(),
                            IconPath = reader["IconPath"].ToString(),
                            TotalSize = reader["TotalSize"].ToString(),
                            Softwares = new List<Software>() // Sẽ nạp ở bước sau
                        });
                    }
                }

                // Lấy các phần mềm nằm trong từng Preset
                foreach (var preset in presets)
                {
                    var swCmd = connection.CreateCommand();

                    // ĐÃ SỬA: Ưu tiên lấy đúng phiên bản đã lưu trong PresetSoftwareMapping.VersionId (cột "chosen").
                    // Nếu VersionId là NULL (preset cũ chưa từng lưu, hoặc version đó đã bị xóa) -> COALESCE tự fallback
                    // về phiên bản mới nhất ("latest") như hành vi cũ.
                    swCmd.CommandText = @"
        SELECT s.*, 
               COALESCE(chosen.Id, latest.Id) AS ResolvedVersionId,
               COALESCE(chosen.Version, latest.Version) AS ResolvedVersion,
               COALESCE(chosen.FileSize, latest.FileSize) AS ResolvedFileSize,
               COALESCE(chosen.FilePath, latest.FilePath) AS ResolvedFilePath
        FROM Software s
        INNER JOIN PresetSoftwareMapping psm ON s.Id = psm.SoftwareId
        LEFT JOIN SoftwareVersion chosen ON chosen.Id = psm.VersionId
        LEFT JOIN SoftwareVersion latest ON latest.Id = (
            SELECT v.Id FROM SoftwareVersion v WHERE v.SoftwareId = s.Id ORDER BY v.Id DESC LIMIT 1
        )
        WHERE psm.PresetId = @PresetId";

                    var pId = swCmd.CreateParameter(); pId.ParameterName = "@PresetId"; pId.Value = preset.Id; swCmd.Parameters.Add(pId);

                    long totalPresetBytes = 0;

                    using (var swReader = await Task.Run(() => swCmd.ExecuteReader()))
                    {
                        while (swReader.Read())
                        {
                            long sizeBytes = swReader["ResolvedFileSize"] != DBNull.Value ? Convert.ToInt64(swReader["ResolvedFileSize"]) : 0;
                            string sizeStr = sizeBytes > 0 ? (sizeBytes / 1048576.0).ToString("0.##") + " MB" : "N/A";
                            totalPresetBytes += sizeBytes;

                            var software = new Software
                            {
                                Id = Convert.ToInt32(swReader["Id"]),
                                Name = swReader["Name"].ToString(),
                                Category = swReader["Category"].ToString(),
                                IconPath = swReader["IconPath"].ToString(),

                                // Nạp đường dẫn và lệnh cài ngầm
                                SilentInstallCommand = swReader["SilentInstallCommand"] != DBNull.Value ? swReader["SilentInstallCommand"].ToString() : "",
                                FilePath = swReader["ResolvedFilePath"] != DBNull.Value ? swReader["ResolvedFilePath"].ToString() : "",

                                Size = sizeStr // Đẩy dung lượng ra UI (nếu UI bạn bind biến tên khác như FileSize thì đổi lại cho khớp)
                            };

                            // MỚI: Gán SelectedVersion để PresetMainView hiển thị ĐÚNG tên phiên bản đã chọn
                            // (thay vì luôn rơi vào FallbackValue "Mặc định" như trước).
                            if (swReader["ResolvedVersionId"] != DBNull.Value)
                            {
                                software.SelectedVersion = new SoftwareVersion
                                {
                                    Id = Convert.ToInt32(swReader["ResolvedVersionId"]),
                                    SoftwareId = software.Id,
                                    Version = swReader["ResolvedVersion"] != DBNull.Value ? swReader["ResolvedVersion"].ToString() : null,
                                    FileSize = sizeBytes,
                                    FilePath = software.FilePath
                                };
                            }

                            preset.Softwares.Add(software);
                        }
                    }

                    // Cộng tổng dung lượng Preset
                    preset.TotalSize = totalPresetBytes > 0 ? (totalPresetBytes / 1048576.0).ToString("0.##") + " MB" : "0 MB";
                }
            }
            return presets;
        }
    }
}