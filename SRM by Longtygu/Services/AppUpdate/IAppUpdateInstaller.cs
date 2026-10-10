using System;
using System.Threading;
using System.Threading.Tasks;

namespace SRM_by_Longtygu.Services.AppUpdate
{
    /// <summary>Kết quả cài đặt bản cập nhật. Service không ném lỗi ra ngoài, mọi lỗi nằm ở đây.</summary>
    public sealed class AppUpdateInstallResult
    {
        public bool Success { get; init; }

        /// <summary>True nếu đã có file bị đổi và đã hoàn tác thành công về bản cũ.</summary>
        public bool RolledBack { get; init; }

        public string? ErrorMessage { get; init; }
        public int FilesReplaced { get; init; }
        public int FilesAdded { get; init; }
        public int FilesUnchanged { get; init; }
        public int ProtectedSkipped { get; init; }

        /// <summary>Đường dẫn bản sao lưu library.db trước khi cập nhật (null nếu không có DB để sao lưu).</summary>
        public string? DatabaseBackupPath { get; init; }
    }

    /// <summary>
    /// Cài bản cập nhật đã giải nén vào thư mục của ứng dụng đang chạy:
    /// chỉ ghi các file CÓ trong gói, không bao giờ xóa gì, không đụng dữ liệu người dùng, tự hoàn tác khi lỗi.
    /// </summary>
    public interface IAppUpdateInstaller
    {
        /// <summary>Thư mục cài đặt (nơi chứa file exe đang chạy).</summary>
        string InstallFolderPath { get; }

        /// <summary>Đường dẫn file exe chính trong thư mục cài đặt (dùng để mở lại ứng dụng sau khi cập nhật).</summary>
        string MainExePath { get; }

        /// <summary>Kiểm tra có ghi được vào thư mục cài đặt không. Trả về null nếu ổn, hoặc thông báo lỗi tiếng Việt.</summary>
        string? CheckInstallFolderWritable();

        Task<AppUpdateInstallResult> InstallAsync(
            string stagedFolder,
            IProgress<AppUpdateProgress>? progress,
            CancellationToken cancellationToken = default);
    }
}
