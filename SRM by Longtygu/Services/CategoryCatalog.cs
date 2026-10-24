using System;
using System.Collections.Generic;

namespace SRM_by_Longtygu.Services
{
    // LỚP PHỦ HIỂN THỊ cho "Danh mục".
    // Giá trị LƯU trong CSDL giữ nguyên tiếng Việt (không đổi dữ liệu, không ảnh hưởng backup cũ);
    // chỉ chữ HIỂN THỊ được dịch theo ngôn ngữ đang chọn qua khóa Category_* trong từ điển.
    // Giá trị lạ (không có trong bảng) hiển thị nguyên văn. Danh mục rỗng -> "Chưa phân loại".
    public static class CategoryCatalog
    {
        // Giá trị lưu của nhóm "Khác" (dùng khi gom danh mục rỗng ở Dashboard)
        public const string OtherStored = "Khác";

        private static readonly (string Stored, string Key)[] Entries =
        {
            ("Trình duyệt Web", "Category_Browser"),
            ("Tiện ích hệ thống", "Category_SystemUtilities"),
            ("Văn phòng & Công việc", "Category_Office"),
            ("Thiết kế & Đồ họa", "Category_Design"),
            ("Đa phương tiện (Media)", "Category_Media"),
            ("Lập trình & IT", "Category_Dev"),
            ("Bảo mật & Diệt Virus", "Category_Security"),
            ("Trò chơi (Games)", "Category_Games"),
            ("Khác", "Category_Other"),
            ("Windows ISO", "Category_WindowsIso"),
            ("Office ISO", "Category_OfficeIso"),
            ("System Components (DLL, C++)", "Category_SystemComponents"),
            ("Drivers", "Category_Drivers"),
            ("Scripts & Configs", "Category_ScriptsConfigs"),
            ("Công cụ (Tools)", "Category_ToolsRes")
        };

        private static readonly Dictionary<string, string> ByStored = BuildMap();

        private static Dictionary<string, string> BuildMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Entries) map[e.Stored] = e.Key;
            return map;
        }

        // Giá trị lưu -> chữ hiển thị theo ngôn ngữ hiện tại
        public static string Display(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return Loc.T("Software_Uncategorized");
            if (ByStored.TryGetValue(stored.Trim(), out var key))
            {
                string text = Loc.T(key);
                if (!string.Equals(text, key, StringComparison.Ordinal)) return text;
            }
            return stored;
        }

        // Tìm kiếm: khớp theo giá trị lưu HOẶC theo chữ hiển thị (gõ "browser" hay "trình duyệt" đều ra)
        public static bool Matches(string stored, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;
            if (!string.IsNullOrEmpty(stored) && stored.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return Display(stored).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
