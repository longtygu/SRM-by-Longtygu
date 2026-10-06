using System;
using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services.AppUpdate
{
    /// <summary>
    /// Kiểm tra cập nhật cho CHÍNH SRM (qua GitHub Releases).
    /// Khác hẳn IUpdateCheckService (Trung tâm cập nhật cho 120 phần mềm trong kho).
    /// </summary>
    public interface IAppSelfUpdateChecker
    {
        /// <summary>Phiên bản đang chạy, đọc từ assembly, chuẩn hóa đủ 4 thành phần.</summary>
        Version GetCurrentVersion();

        /// <summary>Gọi GitHub, không bao giờ ném exception: mọi lỗi nằm trong AppUpdateResult.</summary>
        Task<AppUpdateResult> CheckAsync(CancellationToken cancellationToken = default);
    }
}
