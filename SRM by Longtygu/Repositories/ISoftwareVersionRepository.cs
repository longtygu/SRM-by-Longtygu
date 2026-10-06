using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Repositories
{
    public interface ISoftwareVersionRepository
    {
        // Các hàm cũ bạn đang có (ví dụ InsertAsync, GetAllAsync...)
        Task<int> InsertAsync(SoftwareVersion version);
        Task<IEnumerable<SoftwareVersion>> GetAllAsync();

        // BỔ SUNG DÒNG NÀY VÀO INTERFACE:
        Task<IEnumerable<SoftwareVersion>> GetBySoftwareIdAsync(int softwareId);
        Task<bool> DeleteAsync(int id);
    }
}