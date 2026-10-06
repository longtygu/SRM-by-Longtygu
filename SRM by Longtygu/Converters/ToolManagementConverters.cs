using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SRM_by_Longtygu.Converters
{
    // Id của tool -> true nếu là công cụ tích hợp sẵn (Id bắt đầu bằng "builtin.")
    public class IsBuiltInConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var id = value as string;
            return !string.IsNullOrEmpty(id) && id.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Id của tool -> Visibility.Collapsed nếu là built-in, ngược lại Visible.
    // Dùng cho nút "Xuất" / "Xóa" trong bảng quản lý — công cụ built-in không có nút này.
    public class NotBuiltInToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var id = value as string;
            bool isBuiltIn = !string.IsNullOrEmpty(id) && id.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase);
            return isBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Id của tool -> nhãn trạng thái hiển thị trong bảng quản lý
    public class BuiltInStatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var id = value as string;
            bool isBuiltIn = !string.IsNullOrEmpty(id) && id.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase);
            return isBuiltIn ? "Tích hợp sẵn" : "Plugin";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
