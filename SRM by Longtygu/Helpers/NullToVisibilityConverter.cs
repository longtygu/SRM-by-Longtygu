using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SRM_by_Longtygu.Helpers
{
    /// <summary>
    /// Chuyển đổi giá trị null/không null sang Visibility.
    /// - Giá trị khác null -> Visible (mặc định)
    /// - Truyền ConverterParameter="Inverse" để đảo ngược (null -> Visible, khác null -> Collapsed)
    /// Dùng để hiện/ẩn panel chi tiết tùy theo có item nào đang được chọn hay không.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null;
            bool inverse = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);

            bool showElement = inverse ? isNull : !isNull;

            return showElement ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("NullToVisibilityConverter chỉ hỗ trợ one-way binding.");
        }
    }
}
