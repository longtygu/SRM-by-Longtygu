using System;
using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services.AppUpdate
{
    public enum AppUpdatePhase
    {
        Downloading,
        Verifying,
        Extracting,
        BackingUp,
        Installing
    }

    /// <summary>Tiến độ của quá trình tải/kiểm tra/giải nén gói cập nhật.</summary>
    public sealed class AppUpdateProgress
    {
        public AppUpdatePhase Phase { get; init; }
        public long BytesDone { get; init; }
        public long BytesTotal { get; init; }
        public double BytesPerSecond { get; init; }

        /// <summary>Số giây liên tục KHÔNG nhận được dữ liệu từ máy chủ (0 = đang nhận bình thường).</summary>
        public int StalledSeconds { get; init; }

        /// <summary>Sau bao nhiêu giây không có dữ liệu thì tự hủy (để hiển thị đếm ngược).</summary>
        public int StallLimitSeconds { get; init; }

        public double Percent => BytesTotal > 0 ? Math.Min(100.0, BytesDone * 100.0 / BytesTotal) : 0;
    }

    /// <summary>Kết quả chuẩn bị gói cập nhật. Service không ném lỗi ra ngoài, mọi lỗi nằm ở đây.</summary>
    public sealed class AppUpdateStageResult
    {
        public bool Success { get; init; }
        public bool Cancelled { get; init; }
        public string? ErrorMessage { get; init; }

        /// <summary>Thư mục chứa gói đã giải nén (đã bỏ thư mục gốc của file zip). Chỉ có khi Success.</summary>
        public string? StagedFolder { get; init; }
    }

    /// <summary>
    /// Tải gói cập nhật từ GitHub vào thư mục tạm, kiểm tra SHA256 và giải nén thử.
    /// Không ghi gì vào thư mục cài đặt của ứng dụng.
    /// </summary>
    public interface IAppUpdateDownloader
    {
        /// <summary>Thư mục tạm dùng để tải và giải nén (hiển thị cho người dùng biết vị trí).</summary>
        string StagingFolderPath { get; }

        Task<AppUpdateStageResult> DownloadAndStageAsync(
            AppUpdateResult update,
            IProgress<AppUpdateProgress>? progress,
            CancellationToken cancellationToken = default);

        /// <summary>Xóa toàn bộ file tạm (file zip tải về và thư mục giải nén). Không ném lỗi.</summary>
        void CleanupStaging();
    }
}
