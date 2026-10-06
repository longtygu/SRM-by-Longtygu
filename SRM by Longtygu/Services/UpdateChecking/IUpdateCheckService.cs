using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services.UpdateChecking
{
    public interface IUpdateCheckService
    {
        // Kiểm tra 1 phần mềm, lưu kết quả vào DB (LatestKnownVersion, LastCheckedDate) nếu thành công
        Task<UpdateCheckResult> CheckOneAsync(Software software, CancellationToken ct = default);

        // Kiểm tra nhiều phần mềm song song (giới hạn luồng), gọi callback sau mỗi phần mềm xong
        // để ViewModel cập nhật progress bar theo thời gian thực
        Task CheckAllAsync(IEnumerable<Software> softwares, Action<Software, UpdateCheckResult> onEachChecked, CancellationToken ct = default);

        // Tải file từ downloadUrl về, rồi đẩy qua pipeline có sẵn (SHA256 + copy vào Data/ + ghi DB)
        // để phiên bản mới xuất hiện trong "Danh sách phiên bản đã lưu" như thêm thủ công.
        // progress báo cáo định kỳ (~200ms/lần): %, số byte đã tải/tổng, tốc độ tải hiện tại.
        Task<bool> DownloadAndAddVersionAsync(Software software, string downloadUrl, string version, IProgress<DownloadProgressInfo> progress = null, CancellationToken ct = default);
    }
}
