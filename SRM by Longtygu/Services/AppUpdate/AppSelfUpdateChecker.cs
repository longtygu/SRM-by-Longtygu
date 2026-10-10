using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services.AppUpdate
{
    public class AppSelfUpdateChecker : IAppSelfUpdateChecker
    {
        // Dùng endpoint DANH SÁCH, KHÔNG dùng /releases/latest (endpoint đó bỏ qua pre-release và trả 404 khi chỉ có pre-release).
        public const string ReleasesApiUrl = "https://api.github.com/repos/longtygu/SRM-by-Longtygu/releases?per_page=30";
        public const string ReleasesPageUrl = "https://github.com/longtygu/SRM-by-Longtygu/releases";

        // Một HttpClient dùng chung cho toàn ứng dụng (tránh cạn socket khi tạo mới liên tục).
        private static readonly HttpClient Http = CreateHttpClient();

        private readonly ILogService _logService;

        public AppSelfUpdateChecker(ILogService logService)
        {
            _logService = logService;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SRM-by-Longtygu-UpdateChecker/1.0"); // GitHub API bắt buộc có User-Agent
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        // ====================================================================
        // PHIÊN BẢN ĐANG CHẠY
        // ====================================================================
        public Version GetCurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? new Version(0, 0, 0, 0) : Normalize(version);
        }

        // ====================================================================
        // KIỂM TRA CẬP NHẬT
        // ====================================================================
        public async Task<AppUpdateResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            var current = GetCurrentVersion();

            try
            {
                SafeLogInfo($"[Cập nhật SRM] Bắt đầu kiểm tra. Phiên bản đang chạy: {current}");

                using var response = await Http.GetAsync(ReleasesApiUrl, cancellationToken).ConfigureAwait(false);

                // Rate limit: HTTP 403/429 (chưa đăng nhập: 60 yêu cầu/giờ/IP)
                if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                {
                    return HandleRateLimit(response, current);
                }

                if (!response.IsSuccessStatusCode)
                {
                    SafeLogWarning($"[Cập nhật SRM] GitHub trả về mã lỗi {(int)response.StatusCode}.");
                    return Fail(AppUpdateStatus.InvalidResponse, current,
                        $"GitHub trả về mã lỗi {(int)response.StatusCode}. Vui lòng thử lại sau.");
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var releases = JsonSerializer.Deserialize<List<GitHubReleaseDto>>(json);

                if (releases == null)
                {
                    return Fail(AppUpdateStatus.InvalidResponse, current, "Phản hồi từ GitHub không đúng định dạng.");
                }

                // Chọn bản có version CAO NHẤT (không phụ thuộc thứ tự GitHub trả về), bỏ draft.
                GitHubReleaseDto? best = null;
                Version? bestVersion = null;

                foreach (var release in releases)
                {
                    if (release == null || release.Draft) continue;
                    if (!TryParseVersion(release.TagName, out var version)) continue; // tag lạ: bỏ qua, không crash

                    bool isBetter = bestVersion == null
                        || version.CompareTo(bestVersion) > 0
                        // Cùng số phiên bản (vd v1.0.1-beta và v1.0.1): ưu tiên bản chính thức
                        || (version.CompareTo(bestVersion) == 0 && best != null && best.PreRelease && !release.PreRelease);

                    if (isBetter)
                    {
                        best = release;
                        bestVersion = version;
                    }
                }

                if (best == null || bestVersion == null)
                {
                    SafeLogWarning("[Cập nhật SRM] Không tìm thấy bản phát hành hợp lệ nào trên GitHub.");
                    return Fail(AppUpdateStatus.InvalidResponse, current, "Không tìm thấy bản phát hành hợp lệ nào trên GitHub.");
                }

                bool hasUpdate = bestVersion.CompareTo(current) > 0;
                SafeLogInfo($"[Cập nhật SRM] Bản mới nhất trên GitHub: {best.TagName} ({bestVersion}){(best.PreRelease ? " [pre-release]" : "")}. Có bản mới: {hasUpdate}");

                // File gói cập nhật (asset zip) + mã SHA256 do GitHub công bố
                GitHubAssetDto? asset = FindPackageAsset(best.Assets);
                string? assetUrl = (asset != null && IsTrustedDownloadUrl(asset.DownloadUrl)) ? asset.DownloadUrl : null;
                string? assetSha256 = ExtractSha256(asset?.Digest);

                return new AppUpdateResult
                {
                    Status = hasUpdate ? AppUpdateStatus.UpdateAvailable : AppUpdateStatus.UpToDate,
                    CurrentVersion = current,
                    LatestVersion = bestVersion,
                    LatestTag = best.TagName,
                    LatestName = best.Name,
                    IsPreRelease = best.PreRelease,
                    ReleaseUrl = IsTrustedReleaseUrl(best.HtmlUrl) ? best.HtmlUrl : ReleasesPageUrl,
                    ReleaseNotes = best.Body,
                    PublishedAt = best.PublishedAt,
                    AssetName = asset?.Name,
                    AssetDownloadUrl = assetUrl,
                    AssetSize = asset?.Size ?? 0,
                    AssetSha256 = assetSha256,
                    CheckedAt = DateTime.Now
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Fail(AppUpdateStatus.Cancelled, current, "Đã hủy kiểm tra cập nhật.");
            }
            catch (OperationCanceledException ex) // không phải do người dùng hủy => hết thời gian chờ (timeout)
            {
                SafeLogError("[Cập nhật SRM] Hết thời gian chờ khi gọi GitHub.", ex);
                return Fail(AppUpdateStatus.NetworkError, current, "Hết thời gian chờ khi kết nối tới GitHub. Hãy kiểm tra mạng rồi thử lại.");
            }
            catch (HttpRequestException ex) // offline, DNS lỗi, bị chặn...
            {
                SafeLogError("[Cập nhật SRM] Không kết nối được tới GitHub.", ex);
                return Fail(AppUpdateStatus.NetworkError, current, "Không kết nối được tới GitHub. Hãy kiểm tra mạng rồi thử lại.");
            }
            catch (JsonException ex) // JSON lạ
            {
                SafeLogError("[Cập nhật SRM] JSON từ GitHub không hợp lệ.", ex);
                return Fail(AppUpdateStatus.InvalidResponse, current, "Dữ liệu từ GitHub không đọc được (định dạng lạ).");
            }
            catch (Exception ex) // lưới an toàn cuối cùng: không bao giờ ném lỗi ra UI
            {
                SafeLogError("[Cập nhật SRM] Lỗi không xác định khi kiểm tra cập nhật.", ex);
                return Fail(AppUpdateStatus.InvalidResponse, current, "Đã xảy ra lỗi không mong muốn khi kiểm tra cập nhật.");
            }
        }

        // ====================================================================
        // XỬ LÝ RATE LIMIT
        // ====================================================================
        private AppUpdateResult HandleRateLimit(HttpResponseMessage response, Version current)
        {
            string message = "GitHub đang tạm giới hạn số lần kiểm tra. Vui lòng thử lại sau ít phút.";

            // Header X-RateLimit-Reset = thời điểm reset (giây Unix, UTC)
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues))
            {
                foreach (var raw in resetValues)
                {
                    if (long.TryParse(raw, out long epoch))
                    {
                        var resetLocal = DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime();
                        message = $"GitHub đang tạm giới hạn số lần kiểm tra. Hãy thử lại sau {resetLocal:HH:mm dd/MM/yyyy}.";
                    }
                    break;
                }
            }

            string remaining = "?";
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
            {
                foreach (var raw in remainingValues) { remaining = raw; break; }
            }

            SafeLogWarning($"[Cập nhật SRM] Bị giới hạn tốc độ (HTTP {(int)response.StatusCode}, X-RateLimit-Remaining={remaining}).");
            return Fail(AppUpdateStatus.RateLimited, current, message);
        }

        private static AppUpdateResult Fail(AppUpdateStatus status, Version current, string message)
        {
            return new AppUpdateResult
            {
                Status = status,
                CurrentVersion = current,
                ErrorMessage = message,
                CheckedAt = DateTime.Now
            };
        }

        // ====================================================================
        // PHÂN TÍCH PHIÊN BẢN
        // ====================================================================

        /// <summary>
        /// "v1.2.3-beta+abc" -> Version(1,2,3,0).
        /// Bỏ chữ v đầu, cắt hậu tố sau '-' hoặc '+', chuẩn hóa đủ 4 thành phần, không bao giờ ném lỗi.
        /// Lưu ý: tag chỉ có 1 số (vd "v2") không hợp lệ với Version.TryParse nên bị bỏ qua.
        /// </summary>
        internal static bool TryParseVersion(string? raw, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string s = raw.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);

            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0) s = s.Substring(0, cut);

            if (!Version.TryParse(s, out var parsed) || parsed == null) return false;

            version = Normalize(parsed);
            return true;
        }

        // Trong .NET, Version("1.0.0") nhỏ hơn Version("1.0.0.0") (Revision = -1) => luôn chuẩn hóa về 4 thành phần trước khi so sánh.
        private static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }

        /// <summary>Chỉ cho phép mở link thuộc repo của SRM trên github.com (https).</summary>
        public static bool IsTrustedReleaseUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;

            const string repoPath = "/longtygu/SRM-by-Longtygu";
            return uri.AbsolutePath.Equals(repoPath, StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.StartsWith(repoPath + "/", StringComparison.OrdinalIgnoreCase);
        }

        // ====================================================================
        // FILE GÓI CẬP NHẬT (ASSET)
        // ====================================================================
        private static readonly Regex PackageAssetRegex =
            new Regex(@"^SRM-by-Longtygu-v.+-win-x64\.zip$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Sha256HexRegex =
            new Regex(@"^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

        private static GitHubAssetDto? FindPackageAsset(List<GitHubAssetDto>? assets)
        {
            if (assets == null) return null;

            foreach (var asset in assets)
            {
                if (asset == null || string.IsNullOrEmpty(asset.Name)) continue;
                if (!string.IsNullOrEmpty(asset.State) && !string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase)) continue;
                if (PackageAssetRegex.IsMatch(asset.Name)) return asset;
            }
            return null;
        }

        /// <summary>"sha256:ABC..." -> "abc..." (64 ký tự hex viết thường), null nếu thiếu hoặc sai định dạng.</summary>
        private static string? ExtractSha256(string? digest)
        {
            const string prefix = "sha256:";
            if (string.IsNullOrWhiteSpace(digest)) return null;
            if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            string hex = digest.Substring(prefix.Length).Trim();
            return Sha256HexRegex.IsMatch(hex) ? hex.ToLowerInvariant() : null;
        }

        /// <summary>Chỉ cho phép tải file từ mục Releases của repo SRM trên github.com (https).</summary>
        public static bool IsTrustedDownloadUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;

            return uri.AbsolutePath.StartsWith("/longtygu/SRM-by-Longtygu/releases/download/", StringComparison.OrdinalIgnoreCase);
        }

        // ====================================================================
        // GHI LOG AN TOÀN (việc ghi log lỗi không được làm hỏng luồng kiểm tra)
        // ====================================================================
        private void SafeLogInfo(string message) { try { _logService?.LogInfo(message); } catch { } }
        private void SafeLogWarning(string message) { try { _logService?.LogWarning(message); } catch { } }
        private void SafeLogError(string message, Exception ex) { try { _logService?.LogError(message, ex); } catch { } }

        // ====================================================================
        // DTO CHO JSON CỦA GITHUB (chỉ lấy các trường cần dùng)
        // ====================================================================
        internal sealed class GitHubReleaseDto
        {
            [JsonPropertyName("tag_name")] public string? TagName { get; set; }
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("draft")] public bool Draft { get; set; }
            [JsonPropertyName("prerelease")] public bool PreRelease { get; set; }
            [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
            [JsonPropertyName("body")] public string? Body { get; set; }
            [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
            [JsonPropertyName("assets")] public List<GitHubAssetDto>? Assets { get; set; }
        }

        internal sealed class GitHubAssetDto
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("state")] public string? State { get; set; }
            [JsonPropertyName("size")] public long Size { get; set; }
            [JsonPropertyName("digest")] public string? Digest { get; set; }
            [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
        }
    }
}
