using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Repositories
{
    public interface ISoftwareRepository
    {
        Task<IEnumerable<Software>> GetAllAsync();
        Task<Software> GetByIdAsync(int id);
        Task<int> InsertAsync(Software software);
        Task<bool> UpdateAsync(Software software);
        Task<bool> DeleteAsync(int id);
        Task<IEnumerable<Software>> SearchAsync(string keyword);
        Task CleanUpOrphanedDataAsync();

        Task<Software> GetByNameAsync(string name);

        // MỚI: Lưu kết quả lần quét cập nhật gần nhất (phiên bản mới nhất tìm thấy + thời điểm quét)
        Task<bool> UpdateUpdateCheckInfoAsync(int softwareId, string latestKnownVersion, DateTime checkedDate);

        // MỚI: Lưu cấu hình nguồn kiểm tra cập nhật (loại nguồn, URL, regex...)
        Task<bool> UpdateUpdateSourceConfigAsync(Software software);
    }
}
