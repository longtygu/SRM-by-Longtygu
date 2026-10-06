using System;

namespace SRM_by_Longtygu.Models
{
    public class ResourceFile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; } // Ví dụ: Windows ISO, DLL, Driver
        public string FolderPath { get; set; } // Đường dẫn đến thư mục chứa file
        public string FileName { get; set; } // Tên file cụ thể (tùy chọn)
        public string Size { get; set; }
        public string Description { get; set; } // Chi tiết bổ sung hiển thị trong khay mở rộng
        public string IconPath { get; set; }
        public DateTime AddedDate { get; set; }
    }
}