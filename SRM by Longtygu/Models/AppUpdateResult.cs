using System;

namespace SRM_by_Longtygu.Models
{
    /// <summary>Các trạng thái có thể có của một lần kiểm tra cập nhật cho chính SRM.</summary>
    public enum AppUpdateStatus
    {
        UpToDate,        // Đang dùng bản mới nhất (hoặc mới hơn)
        UpdateAvailable, // Có bản mới trên GitHub
        NetworkError,    // Offline, DNS lỗi, hết thời gian chờ...
        RateLimited,     // GitHub giới hạn số yêu cầu (HTTP 403/429)
        InvalidResponse, // Mã lỗi lạ, JSON lạ, không có bản phát hành hợp lệ
        Cancelled        // Người dùng/hệ thống hủy
    }

    /// <summary>Kết quả của một lần kiểm tra cập nhật. Service không bao giờ ném lỗi ra ngoài, mọi lỗi nằm ở Status + ErrorMessage.</summary>
    public class AppUpdateResult
    {
        public AppUpdateStatus Status { get; init; }

        /// <summary>Phiên bản đang chạy (đã chuẩn hóa đủ 4 thành phần).</summary>
        public Version CurrentVersion { get; init; } = new Version(0, 0, 0, 0);

        /// <summary>Phiên bản mới nhất tìm được trên GitHub (null nếu kiểm tra thất bại).</summary>
        public Version? LatestVersion { get; init; }

        public string? LatestTag { get; init; }
        public string? LatestName { get; init; }
        public bool IsPreRelease { get; init; }

        /// <summary>Link trang Release (đã kiểm tra thuộc repo của SRM).</summary>
        public string? ReleaseUrl { get; init; }

        /// <summary>Ghi chú phát hành gốc (chưa cắt).</summary>
        public string? ReleaseNotes { get; init; }

        public DateTimeOffset? PublishedAt { get; init; }

        // ---- File gói cập nhật (asset .zip) của bản phát hành, dùng cho tính năng tự tải ----
        public string? AssetName { get; init; }

        /// <summary>Link tải file zip (đã kiểm tra thuộc repo của SRM), null nếu không hợp lệ.</summary>
        public string? AssetDownloadUrl { get; init; }

        public long AssetSize { get; init; }

        /// <summary>SHA256 do GitHub công bố, 64 ký tự hex viết thường. Null nếu GitHub không cung cấp.</summary>
        public string? AssetSha256 { get; init; }

        /// <summary>Chỉ cho tự tải khi có đủ link hợp lệ, kích thước và mã SHA256 để xác minh.</summary>
        public bool CanDownloadAutomatically =>
            !string.IsNullOrEmpty(AssetDownloadUrl) && !string.IsNullOrEmpty(AssetSha256) && AssetSize > 0;

        /// <summary>Thông báo thân thiện bằng tiếng Việt khi thất bại.</summary>
        public string? ErrorMessage { get; init; }

        public DateTime CheckedAt { get; init; } = DateTime.Now;
    }
}
