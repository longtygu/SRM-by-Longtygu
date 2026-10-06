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

        /// <summary>Thông báo thân thiện bằng tiếng Việt khi thất bại.</summary>
        public string? ErrorMessage { get; init; }

        public DateTime CheckedAt { get; init; } = DateTime.Now;
    }
}
