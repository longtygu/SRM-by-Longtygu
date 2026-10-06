using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services.UpdateChecking
{
    // Mỗi "nguồn" là 1 cách khác nhau để tra ra phiên bản mới nhất của 1 phần mềm.
    // Thêm nguồn mới (VD: Winget, Chocolatey...) chỉ cần implement thêm 1 class,
    // không phải sửa UpdateCheckService hay ViewModel.
    public interface IUpdateSource
    {
        // Khớp với giá trị lưu trong Software.UpdateSourceType (VD: "GitHubReleases", "RegexPage")
        string SourceTypeKey { get; }

        Task<UpdateCheckResult> GetLatestAsync(Software software, CancellationToken ct);
    }
}
