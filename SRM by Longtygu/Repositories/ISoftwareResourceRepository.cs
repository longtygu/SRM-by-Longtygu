using System.Collections.Generic;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Repositories
{
    // MỚI: Quản lý việc gắn/gỡ tài nguyên (ResourceFile) vào từng phần mềm (Software)
    public interface ISoftwareResourceRepository
    {
        // Lấy toàn bộ tài nguyên đang được đính kèm vào 1 phần mềm
        Task<IEnumerable<ResourceFile>> GetResourcesForSoftwareAsync(int softwareId);

        // Lấy danh sách Id tài nguyên đã đính kèm (dùng để lọc trong form "Đính kèm tài nguyên")
        Task<IEnumerable<int>> GetAttachedResourceIdsAsync(int softwareId);

        // Đính kèm 1 tài nguyên vào phần mềm (bỏ qua nếu đã tồn tại)
        Task AttachAsync(int softwareId, int resourceId);

        // Đính kèm nhiều tài nguyên cùng lúc
        Task AttachManyAsync(int softwareId, IEnumerable<int> resourceIds);

        // Gỡ 1 tài nguyên khỏi phần mềm
        Task DetachAsync(int softwareId, int resourceId);
    }
}
