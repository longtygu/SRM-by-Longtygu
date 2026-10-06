using System;
using System.Collections.Generic;
using System.Data.SQLite;
using SRM_by_Longtygu.Database;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Services;

namespace SRM_by_Longtygu.Repositories
{
    public class ResourceFileRepository : IResourceFileRepository
    {
        private readonly ILogService _logService;

        // ILogService được inject qua DI (đã đăng ký Singleton trong App.xaml.cs)
        public ResourceFileRepository(ILogService logService)
        {
            _logService = logService;
        }

        public IEnumerable<ResourceFile> GetAll()
        {
            var list = new List<ResourceFile>();
            using (var connection = new SQLiteConnection(DatabaseBootstrapper.GetConnectionString()))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM ResourceFiles";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new ResourceFile
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Name = reader["Name"]?.ToString(),
                            Category = reader["Category"]?.ToString(),
                            FileName = reader["FileName"]?.ToString(),
                            Size = reader["Size"]?.ToString(),
                            Description = reader["Description"]?.ToString(),
                            IconPath = reader["IconPath"]?.ToString(),
                            FolderPath = reader["FolderPath"]?.ToString(),
                            AddedDate = reader["AddedDate"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(reader["AddedDate"])
                        });
                    }
                }
            }
            return list;
        }

        public void Add(ResourceFile file)
        {
            try
            {
                using (var connection = new SQLiteConnection(DatabaseBootstrapper.GetConnectionString()))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO ResourceFiles (Name, Category, FileName, Size, Description, IconPath, FolderPath, AddedDate) 
                        VALUES (@Name, @Category, @FileName, @Size, @Description, @IconPath, @FolderPath, @AddedDate)";

                    cmd.Parameters.AddWithValue("@Name", file.Name);
                    cmd.Parameters.AddWithValue("@Category", file.Category);
                    cmd.Parameters.AddWithValue("@FileName", file.FileName);
                    cmd.Parameters.AddWithValue("@Size", file.Size);
                    cmd.Parameters.AddWithValue("@Description", file.Description);
                    cmd.Parameters.AddWithValue("@IconPath", file.IconPath);
                    cmd.Parameters.AddWithValue("@FolderPath", file.FolderPath);
                    cmd.Parameters.AddWithValue("@AddedDate", file.AddedDate.ToString("yyyy-MM-dd HH:mm:ss"));

                    cmd.ExecuteNonQuery();
                }

                _logService?.LogInfo($"Đã thêm tài nguyên mới: \"{file.Name}\" ({file.FileName}, {file.Size})");
            }
            catch (Exception ex)
            {
                _logService?.LogError($"Lỗi khi thêm tài nguyên \"{file.Name}\"", ex);
                throw;
            }
        }

        public void Update(ResourceFile file)
        {
            try
            {
                using (var connection = new SQLiteConnection(DatabaseBootstrapper.GetConnectionString()))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        UPDATE ResourceFiles 
                        SET Name=@Name, Category=@Category, FileName=@FileName, Size=@Size, 
                            Description=@Description, IconPath=@IconPath, FolderPath=@FolderPath 
                        WHERE Id=@Id";

                    cmd.Parameters.AddWithValue("@Id", file.Id);
                    cmd.Parameters.AddWithValue("@Name", file.Name);
                    cmd.Parameters.AddWithValue("@Category", file.Category);
                    cmd.Parameters.AddWithValue("@FileName", file.FileName);
                    cmd.Parameters.AddWithValue("@Size", file.Size);
                    cmd.Parameters.AddWithValue("@Description", file.Description);
                    cmd.Parameters.AddWithValue("@IconPath", file.IconPath);
                    cmd.Parameters.AddWithValue("@FolderPath", file.FolderPath);

                    cmd.ExecuteNonQuery();
                }

                _logService?.LogInfo($"Đã cập nhật tài nguyên: \"{file.Name}\" (Id={file.Id})");
            }
            catch (Exception ex)
            {
                _logService?.LogError($"Lỗi khi cập nhật tài nguyên Id={file.Id}", ex);
                throw;
            }
        }

        public void Delete(int id)
        {
            try
            {
                using (var connection = new SQLiteConnection(DatabaseBootstrapper.GetConnectionString()))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM ResourceFiles WHERE Id=@Id";
                    cmd.Parameters.AddWithValue("@Id", id);
                    cmd.ExecuteNonQuery();
                }

                _logService?.LogWarning($"Đã xóa tài nguyên có Id={id}");
            }
            catch (Exception ex)
            {
                _logService?.LogError($"Lỗi khi xóa tài nguyên Id={id}", ex);
                throw;
            }
        }
    }
}
