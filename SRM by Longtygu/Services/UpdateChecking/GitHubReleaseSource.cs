using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services.UpdateChecking
{
    // Dùng cho phần mềm có repo GitHub public, publish release qua GitHub Releases.
    // Software.UpdateSourceUrl phải có dạng "owner/repo", VD: "videolan/vlc".
    //
    // LƯU Ý: nếu project của bạn là .NET Framework (chưa phải .NET 6+), cần cài thêm
    // gói NuGet "System.Text.Json" thì class này mới build được.
    public class GitHubReleaseSource : IUpdateSource
    {
        private static readonly HttpClient _http = new HttpClient();

        static GitHubReleaseSource()
        {
            // GitHub API bắt buộc phải có User-Agent, nếu không sẽ trả về lỗi 403
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SRM-by-Longtygu-UpdateChecker/1.0");
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        public string SourceTypeKey => "GitHubReleases";

        public async Task<UpdateCheckResult> GetLatestAsync(Software software, CancellationToken ct)
        {
            var result = new UpdateCheckResult();

            try
            {
                if (string.IsNullOrWhiteSpace(software.UpdateSourceUrl))
                {
                    result.ErrorMessage = "Chưa cấu hình 'owner/repo' GitHub cho phần mềm này.";
                    return result;
                }

                string repoPath = software.UpdateSourceUrl.Trim().Trim('/');
                string apiUrl = $"https://api.github.com/repos/{repoPath}/releases/latest";

                using var response = await _http.GetAsync(apiUrl, ct);
                if (!response.IsSuccessStatusCode)
                {
                    result.ErrorMessage = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "Không tìm thấy repo hoặc repo chưa có release nào."
                        : $"GitHub API trả về lỗi HTTP {(int)response.StatusCode}.";
                    return result;
                }

                string json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagEl) || string.IsNullOrWhiteSpace(tagEl.GetString()))
                {
                    result.ErrorMessage = "Phản hồi từ GitHub không có 'tag_name'.";
                    return result;
                }

                result.LatestVersion = tagEl.GetString().TrimStart('v', 'V');

                // Ưu tiên asset .exe / .msi làm link tải mặc định, nếu không có thì lấy asset đầu tiên
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array && assets.GetArrayLength() > 0)
                {
                    string bestUrl = null;
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                        string url = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
                        if (url == null) continue;

                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                        {
                            bestUrl = url;
                            break;
                        }
                        bestUrl ??= url;
                    }
                    result.DownloadUrl = bestUrl;
                }

                result.Success = true;
            }
            catch (TaskCanceledException)
            {
                result.ErrorMessage = "Hết thời gian chờ (timeout) khi kết nối GitHub.";
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
