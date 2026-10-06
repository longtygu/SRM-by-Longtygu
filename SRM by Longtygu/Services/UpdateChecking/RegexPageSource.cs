using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services.UpdateChecking
{
    // Dùng cho phần mềm KHÔNG có API chính thức (Zalo, UltraViewer...):
    // fetch 1 trang HTML rồi áp regex để trích ra số phiên bản (bắt buộc) và link tải (tuỳ chọn).
    //
    // Software.UpdateSourceUrl      = URL trang cần quét (thường là trang download chính thức)
    // Software.UpdateVersionPattern = regex có ĐÚNG 1 capture group lấy version
    //                                  VD: @"Phi.n b.n[^\d]*(\d+(?:\.\d+)+)"
    // Software.UpdateDownloadPattern = regex có ĐÚNG 1 capture group lấy link tải (tuỳ chọn)
    //                                  VD: @"href=""([^""]+\.exe)"""
    //
    // CẢNH BÁO: cách này phụ thuộc vào cấu trúc HTML hiện tại của trang — nếu nhà phát hành
    // đổi giao diện trang download, regex sẽ không khớp nữa và cần cập nhật lại thủ công.
    public class RegexPageSource : IUpdateSource
    {
        private static readonly HttpClient _http = new HttpClient();

        static RegexPageSource()
        {
            // Giả lập User-Agent trình duyệt thật, vì nhiều trang chặn request không có UA hợp lệ
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 SRM-UpdateChecker/1.0");
            _http.Timeout = TimeSpan.FromSeconds(12);
        }

        public string SourceTypeKey => "RegexPage";

        public async Task<UpdateCheckResult> GetLatestAsync(Software software, CancellationToken ct)
        {
            var result = new UpdateCheckResult();

            try
            {
                if (string.IsNullOrWhiteSpace(software.UpdateSourceUrl) || string.IsNullOrWhiteSpace(software.UpdateVersionPattern))
                {
                    result.ErrorMessage = "Chưa cấu hình URL hoặc mẫu regex phiên bản.";
                    return result;
                }

                string html = await _http.GetStringAsync(software.UpdateSourceUrl, ct);

                var versionMatch = Regex.Match(html, software.UpdateVersionPattern, RegexOptions.IgnoreCase);
                if (!versionMatch.Success || versionMatch.Groups.Count < 2)
                {
                    result.ErrorMessage = "Regex phiên bản không khớp được với nội dung trang (trang có thể đã đổi cấu trúc).";
                    return result;
                }
                result.LatestVersion = versionMatch.Groups[1].Value.Trim();

                if (!string.IsNullOrWhiteSpace(software.UpdateDownloadPattern))
                {
                    var downloadMatch = Regex.Match(html, software.UpdateDownloadPattern, RegexOptions.IgnoreCase);
                    if (downloadMatch.Success && downloadMatch.Groups.Count >= 2)
                    {
                        string link = downloadMatch.Groups[1].Value.Trim();

                        // Link trong trang có thể là tương đối (VD: "/download/file.exe") -> ghép về URL tuyệt đối
                        if (Uri.TryCreate(new Uri(software.UpdateSourceUrl), link, out var absoluteUri))
                            result.DownloadUrl = absoluteUri.ToString();
                        else
                            result.DownloadUrl = link;
                    }
                }

                result.Success = true;
            }
            catch (TaskCanceledException)
            {
                result.ErrorMessage = "Hết thời gian chờ (timeout) khi tải trang.";
            }
            catch (HttpRequestException ex)
            {
                result.ErrorMessage = $"Lỗi kết nối mạng: {ex.Message}";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Lỗi không xác định: {ex.Message}";
            }

            return result;
        }
    }
}
