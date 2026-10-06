using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services.UpdateChecking
{
    // Dùng Windows Package Manager (winget) làm nguồn dò cập nhật.
    // Đây là cách TỐN ÍT CÔNG CẤU HÌNH NHẤT trong 3 nguồn, vì winget đã có catalog cộng đồng
    // (kho winget-pkgs trên GitHub) với hàng chục nghìn phần mềm đã được xác thực sẵn.
    //
    // Software.UpdateSourceUrl (dùng chung field với GitHub) = ID gói Winget, VD: "VideoLAN.VLC".
    // Cách tra đúng ID: mở CMD gõ "winget search <tên phần mềm>", cột "Id" chính là giá trị cần điền.
    //
    // YÊU CẦU: máy đang chạy phải có sẵn Winget (App Installer từ Microsoft Store, có sẵn trên
    // hầu hết Windows 10/11 bản mới) VÀ có kết nối mạng tại thời điểm quét/cài — không hoạt động
    // được hoàn toàn offline, đúng bản chất của MỌI cơ chế kiểm tra cập nhật (xem giải thích đã trao đổi).
    public class WingetSource : IUpdateSource
    {
        public string SourceTypeKey => "Winget";

        public async Task<UpdateCheckResult> GetLatestAsync(Software software, CancellationToken ct)
        {
            var result = new UpdateCheckResult();

            if (string.IsNullOrWhiteSpace(software.UpdateSourceUrl))
            {
                result.ErrorMessage = "Chưa cấu hình ID gói Winget (VD: VideoLAN.VLC).";
                return result;
            }

            string packageId = software.UpdateSourceUrl.Trim();

            try
            {
                string output = await RunWingetAsync($"show --id \"{packageId}\" --exact --accept-source-agreements", ct);

                if (string.IsNullOrWhiteSpace(output) || output.Contains("No package found", StringComparison.OrdinalIgnoreCase))
                {
                    result.ErrorMessage = $"Winget không tìm thấy gói '{packageId}'. Chạy 'winget search {software.Name}' trong CMD để tra đúng ID.";
                    return result;
                }

                var versionMatch = Regex.Match(output, @"Version:\s*(.+)");
                if (!versionMatch.Success)
                {
                    result.ErrorMessage = "Không đọc được số phiên bản từ kết quả Winget (định dạng output có thể đã đổi giữa các phiên bản Winget).";
                    return result;
                }
                result.LatestVersion = versionMatch.Groups[1].Value.Trim();

                // Không phải gói nào cũng in ra Installer Url (nhiều gói cài qua MSIX/Store) -> có thể null,
                // khi đó chỉ dùng được để BÁO có bản mới, không tự tải vào thư viện được (dùng nút "Cài qua Winget" thay thế)
                var urlMatch = Regex.Match(output, @"Installer Url:\s*(\S+)");
                if (urlMatch.Success) result.DownloadUrl = urlMatch.Groups[1].Value.Trim();

                result.Success = true;
            }
            catch (Win32Exception)
            {
                result.ErrorMessage = "Không tìm thấy Winget trên máy này. Cần cài 'App Installer' từ Microsoft Store để dùng nguồn này.";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Lỗi khi gọi Winget: {ex.Message}";
            }

            return result;
        }

        // MỚI: Chạy thẳng "winget upgrade" - Winget tự lo cả bước tải lẫn cài đặt trong 1 lệnh duy nhất,
        // đáng tin cậy hơn nhiều so với tự viết downloader, vì dùng đúng hạ tầng Microsoft đã kiểm định.
        // LƯU Ý: cách này KHÔNG thêm file vào thư viện Data/ của SRM (Winget tự quản lý cache installer
        // riêng, SRM không có quyền/không nên đụng vào đó).
        public static async Task<bool> UpgradeNowAsync(string packageId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return false;

            try
            {
                string args = $"upgrade --id \"{packageId}\" --exact --silent --accept-package-agreements --accept-source-agreements";
                string output = await RunWingetAsync(args, ct);

                return output.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("No applicable update", StringComparison.OrdinalIgnoreCase); // đã ở bản mới nhất -> vẫn coi là thành công
            }
            catch
            {
                return false;
            }
        }

        private static Task<string> RunWingetAsync(string arguments, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Không khởi động được tiến trình winget.exe");
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30000); // winget đôi khi chậm do phải đồng bộ nguồn lần đầu, chờ tối đa 30s
                return output;
            }, ct);
        }
    }
}
