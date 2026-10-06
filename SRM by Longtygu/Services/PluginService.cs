using SRM_by_Longtygu.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SRM_by_Longtygu.Services
{
    public interface IPluginService
    {
        List<IToolModule> LoadTools();
        bool InstallToolFromFile(string sourceFilePath, out string errorMessage);

        // Gỡ bỏ 1 công cụ đã cài (theo Id). Trả về false + errorMessage nếu không xóa được
        // (ví dụ: công cụ tích hợp sẵn, không có file .dll tương ứng).
        // Trường hợp đặc biệt: nếu file .dll đang bị khóa do đang chạy trong session hiện tại,
        // hàm sẽ đổi tên file để "ẩn" nó khỏi LoadTools() lần sau, và trả về errorMessage = "PENDING_RESTART"
        // kèm return true (coi như đã gỡ thành công về mặt danh sách hiển thị).
        bool RemoveTool(string toolId, out string errorMessage);

        // Xuất (copy) file .dll của 1 công cụ đã cài ra vị trí do người dùng chọn — dùng để backup/chia sẻ.
        bool ExportTool(string toolId, string destinationFilePath, out string errorMessage);

        // Lấy tên file .dll gốc của 1 công cụ (để gợi ý tên file khi Export). Trả về false nếu là công cụ built-in.
        bool TryGetSourceFileName(string toolId, out string fileName);
    }

    public class PluginService : IPluginService
    {
        private readonly string _pluginFolder;

        // Ghi nhớ tool Id nào tương ứng với file .dll nào, để RemoveTool() biết cần xóa file gì.
        // Được nạp lại mỗi lần LoadTools() chạy.
        private readonly Dictionary<string, string> _toolIdToFilePath = new Dictionary<string, string>();

        private const string PendingDeleteSuffix = ".pending_delete";

        public PluginService()
        {
            _pluginFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

            if (!Directory.Exists(_pluginFolder))
            {
                Directory.CreateDirectory(_pluginFolder);
            }

            // Dọn các file đã được đánh dấu xóa từ phiên trước (lúc đó bị khóa nên chưa xóa được)
            CleanupPendingDeletes();
        }

        public List<IToolModule> LoadTools()
        {
            var tools = new List<IToolModule>();
            _toolIdToFilePath.Clear();

            var dllFiles = Directory.GetFiles(_pluginFolder, "*.dll", SearchOption.TopDirectoryOnly);

            foreach (var dllPath in dllFiles)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dllPath);

                    var toolTypes = assembly.GetTypes()
                        .Where(t => typeof(IToolModule).IsAssignableFrom(t)
                                    && !t.IsInterface
                                    && !t.IsAbstract
                                    && t.GetConstructor(Type.EmptyTypes) != null);

                    foreach (var type in toolTypes)
                    {
                        if (Activator.CreateInstance(type) is IToolModule instance)
                        {
                            tools.Add(instance);
                            _toolIdToFilePath[instance.Id] = dllPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginService] Lỗi khi nạp {Path.GetFileName(dllPath)}: {ex.Message}");
                }
            }

            return tools;
        }

        public bool RemoveTool(string toolId, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrEmpty(toolId) || !_toolIdToFilePath.TryGetValue(toolId, out var filePath))
            {
                errorMessage = "Không tìm thấy file tương ứng với công cụ này (có thể là công cụ tích hợp sẵn).";
                return false;
            }

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                _toolIdToFilePath.Remove(toolId);
                return true;
            }
            catch (IOException)
            {
                // File .dll đang bị khóa vì assembly còn được nạp trong session hiện tại.
                // Đổi tên để LoadTools() lần sau không nhận diện nó nữa, xóa hẳn khi khởi động lại app.
                try
                {
                    var pendingPath = filePath + PendingDeleteSuffix;
                    if (File.Exists(pendingPath))
                    {
                        File.Delete(pendingPath);
                    }

                    File.Move(filePath, pendingPath);
                    _toolIdToFilePath.Remove(toolId);

                    errorMessage = "PENDING_RESTART";
                    return true;
                }
                catch (Exception ex2)
                {
                    errorMessage = ex2.Message;
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool ExportTool(string toolId, string destinationFilePath, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrEmpty(toolId) || !_toolIdToFilePath.TryGetValue(toolId, out var sourcePath))
            {
                errorMessage = "Không tìm thấy file tương ứng với công cụ này (có thể là công cụ tích hợp sẵn, không thể xuất ra file riêng).";
                return false;
            }

            try
            {
                if (!File.Exists(sourcePath))
                {
                    errorMessage = "File gốc của công cụ không còn tồn tại trong thư mục Plugins.";
                    return false;
                }

                File.Copy(sourcePath, destinationFilePath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool TryGetSourceFileName(string toolId, out string fileName)
        {
            fileName = null;

            if (string.IsNullOrEmpty(toolId) || !_toolIdToFilePath.TryGetValue(toolId, out var sourcePath))
            {
                return false;
            }

            fileName = Path.GetFileName(sourcePath);
            return true;
        }

        // Xóa các file đã đánh dấu ".pending_delete" từ phiên chạy trước (nếu lúc đó chưa xóa được do bị khóa)
        private void CleanupPendingDeletes()
        {
            try
            {
                var pendingFiles = Directory.GetFiles(_pluginFolder, "*" + PendingDeleteSuffix, SearchOption.TopDirectoryOnly);
                foreach (var f in pendingFiles)
                {
                    try
                    {
                        File.Delete(f);
                    }
                    catch
                    {
                        // Vẫn còn bị khóa (hiếm khi xảy ra) — để lần khởi động sau thử lại
                    }
                }
            }
            catch
            {
                // Không nghiêm trọng, bỏ qua
            }
        }

        // Copy file .dll người dùng chọn vào thư mục Plugins/ để lần LoadTools() kế tiếp nhận diện được
        public bool InstallToolFromFile(string sourceFilePath, out string errorMessage)
        {
            errorMessage = null;

            try
            {
                if (!File.Exists(sourceFilePath))
                {
                    errorMessage = "File không tồn tại.";
                    return false;
                }

                if (!sourceFilePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "Chỉ hỗ trợ file .dll.";
                    return false;
                }

                var fileName = Path.GetFileName(sourceFilePath);
                var destPath = Path.Combine(_pluginFolder, fileName);

                File.Copy(sourceFilePath, destPath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}