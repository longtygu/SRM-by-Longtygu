using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Drawing; // Cần thư viện System.Drawing.Common vừa cài

namespace SRM_by_Longtygu.Services
{
    public interface IFileProcessingService
    {
        Task<string> CalculateSHA256Async(string filePath);
        Task<string> ExtractAndSaveIconAsync(string exePath, string softwareName);
        Task<string> CopyFileToDataFolderAsync(string sourceFilePath, string softwareName, string version);
        Task DeleteSoftwareFolderAsync(string softwareName);
        Task CleanUpOrphanedFilesAsync(System.Collections.Generic.IEnumerable<string> validSoftwareNames);

        // MỚI: Chuẩn bị sẵn thư mục đích Data/[SoftwareName]/[Version]/ và trả về đường dẫn TUYỆT ĐỐI
        // của file đích - KHÔNG copy gì cả, chỉ tạo thư mục. Dùng khi tải file trực tiếp từ mạng,
        // để ghi thẳng vào đúng vị trí cuối cùng, tránh phải tải/ghi 2 lần (tạm rồi copy) như trước đây.
        Task<string> PrepareVersionFilePathAsync(string softwareName, string version, string fileName);
    }


    public class FileProcessingService : IFileProcessingService
    {
        private readonly string _dataFolder;

        public FileProcessingService()
        {
            // Thiết lập đường dẫn thư mục Data nằm cùng cấp với file chạy App.exe
            _dataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (!Directory.Exists(_dataFolder))
            {
                Directory.CreateDirectory(_dataFolder);
            }
        }

        // 1. Hàm tính mã băm SHA256
        public async Task<string> CalculateSHA256Async(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;

            return await Task.Run(() =>
            {
                using (var sha256 = SHA256.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        var hash = sha256.ComputeHash(stream);
                        // Convert byte array sang chuỗi Hex viết thường
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            });
        }

        // 2. Hàm trích xuất Icon từ file .exe và lưu thành .png
        public async Task<string> ExtractAndSaveIconAsync(string exePath, string softwareName)
        {
            // Chỉ hỗ trợ trích xuất icon từ file thực thi .exe
            if (!File.Exists(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return await Task.Run(() =>
            {
                try
                {
                    var icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null)
                    {
                        // Tạo thư mục Data/[Tên_Phần_Mềm]
                        string softwareFolder = Path.Combine(_dataFolder, softwareName);
                        if (!Directory.Exists(softwareFolder))
                            Directory.CreateDirectory(softwareFolder);

                        // Lưu icon thành dạng PNG
                        string iconFileName = $"{softwareName}_icon.png";
                        string iconFilePath = Path.Combine(softwareFolder, iconFileName);

                        using (Bitmap bmp = icon.ToBitmap())
                        {
                            bmp.Save(iconFilePath, System.Drawing.Imaging.ImageFormat.Png);
                        }

                        // Trả về ĐƯỜNG DẪN TƯƠNG ĐỐI để lưu vào SQLite (Tránh lỗi sai ổ đĩa USB)
                        return Path.Combine("Data", softwareName, iconFileName);
                    }
                }
                catch
                {
                    // Bỏ qua lỗi nếu file exe bị mã hóa bảo vệ, không cho lấy icon
                }
                return string.Empty;
            });
        }

        // 3. Hàm copy file bộ cài vào cây thư mục quy chuẩn
        public async Task<string> CopyFileToDataFolderAsync(string sourceFilePath, string softwareName, string version)
        {
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("File bộ cài không tồn tại ở đường dẫn nguồn.");

            return await Task.Run(() =>
            {
                // Cấu trúc: Data\[SoftwareName]\[Version]
                string targetFolder = Path.Combine(_dataFolder, softwareName, version);
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                string fileName = Path.GetFileName(sourceFilePath);
                string targetFilePath = Path.Combine(targetFolder, fileName);

                // Thực thi copy file (ghi đè nếu đã có file trùng tên)
                File.Copy(sourceFilePath, targetFilePath, overwrite: true);

                // Trả về ĐƯỜNG DẪN TƯƠNG ĐỐI
                return Path.Combine("Data", softwareName, version, fileName);
            });
        }

        // MỚI: Chỉ tạo sẵn thư mục Data/[SoftwareName]/[Version]/ và trả đường dẫn tuyệt đối của file
        // đích - không copy/ghi gì cả. UpdateCheckService dùng hàm này để tải file mạng THẲNG vào đây,
        // thay vì tải ra Temp rồi copy sang (đỡ 1 lượt đọc + 1 lượt ghi, và đỡ bị quét virus 2 lần).
        public async Task<string> PrepareVersionFilePathAsync(string softwareName, string version, string fileName)
        {
            return await Task.Run(() =>
            {
                string targetFolder = Path.Combine(_dataFolder, softwareName, version);
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }
                return Path.Combine(targetFolder, fileName);
            });
        }

        // 4. Hàm xóa toàn bộ thư mục vật lý của phần mềm
        public async Task DeleteSoftwareFolderAsync(string softwareName)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Lấy đường dẫn thư mục Data/[Tên_Phần_Mềm]
                    string targetFolder = Path.Combine(_dataFolder, softwareName);

                    // Nếu thư mục tồn tại thì xóa toàn bộ thư mục và các file/folder con bên trong
                    if (Directory.Exists(targetFolder))
                    {
                        Directory.Delete(targetFolder, recursive: true);
                    }
                }
                catch
                {
                    // Bỏ qua nếu có lỗi (ví dụ file đang bị mở bởi chương trình khác)
                }
            });
        }
        // 5. Hàm dọn rác thủ công: Xóa các thư mục không còn tồn tại trong Database
        public async Task CleanUpOrphanedFilesAsync(System.Collections.Generic.IEnumerable<string> validSoftwareNames)
        {
            await Task.Run(() =>
            {
                if (!Directory.Exists(_dataFolder)) return;

                // Lấy danh sách tên thư mục rễ trong ổ cứng (thường là Tên phần mềm)
                var physicalDirectories = Directory.GetDirectories(_dataFolder);

                // Tạo một HashSet chứa tên hợp lệ để tra cứu tốc độ cao
                var validSet = new System.Collections.Generic.HashSet<string>(validSoftwareNames, StringComparer.OrdinalIgnoreCase);

                foreach (var dir in physicalDirectories)
                {
                    string folderName = Path.GetFileName(dir);

                    // Nếu tên thư mục vật lý KHÔNG nằm trong danh sách Database -> Đích thị là rác, xóa!
                    if (!validSet.Contains(folderName))
                    {
                        try
                        {
                            Directory.Delete(dir, recursive: true);
                        }
                        catch
                        {
                            // Bỏ qua nếu file đang bị hệ thống chiếm dụng
                        }
                    }
                }
            });
        }
    }
}
