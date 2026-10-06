using SRM_by_Longtygu.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SRM_by_Longtygu.Repositories
{
    public interface IPresetRepository
    {
        Task<IEnumerable<Preset>> GetAllPresetsAsync();

        // ĐÃ SỬA: softwareVersionSelections là Dictionary<SoftwareId, VersionId?>
        // VersionId = null nghĩa là không cố định version, luôn lấy bản mới nhất khi triển khai.
        Task AddPresetAsync(Preset preset, Dictionary<int, int?> softwareVersionSelections);

        // MỚI: Cập nhật thông tin Preset + đồng bộ lại toàn bộ Software/Version đã chọn (dùng khi Sửa Preset)
        Task UpdatePresetAsync(Preset preset, Dictionary<int, int?> softwareVersionSelections);
    }
}