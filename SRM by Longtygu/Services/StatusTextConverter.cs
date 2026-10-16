using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace SRM_by_Longtygu.Services
{
    /// <summary>
    /// Hiển thị trạng thái theo ngôn ngữ đang chọn mà KHÔNG đổi giá trị gốc của thuộc tính Status.
    /// Giá trị Status (ví dụ "Thành công") vẫn là "mã" dùng cho điều kiện, DataTrigger và so sánh; chỉ chữ HIỂN THỊ được dịch.
    /// Giá trị lạ (không có trong bảng) được hiển thị nguyên văn.
    /// Cách dùng trong XAML: Binding="{Binding Status, Converter={x:Static srmloc:StatusTextConverter.Instance}}"
    /// </summary>
    public sealed class StatusTextConverter : IValueConverter
    {
        public static StatusTextConverter Instance { get; } = new StatusTextConverter();

        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Đang chờ"] = "RamTest_Waiting",
            ["Đang xử lý..."] = "Status_Processing",
            ["Thành công"] = "PresetMain_Success",
            ["Lỗi"] = "Common_Error",
            ["Đang gỡ..."] = "Status_Uninstalling",
            ["Đã gỡ"] = "Status_Uninstalled",
            ["Lỗi / Hủy"] = "Status_ErrorCancelled"
        };

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string text && Map.TryGetValue(text, out var key))
            {
                return Loc.T(key);
            }
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
