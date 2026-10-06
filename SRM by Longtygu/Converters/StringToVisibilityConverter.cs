using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SRM_by_Longtygu.Converters
{
    // Ẩn/hiện phần tử dựa theo chuỗi có rỗng hay không - dùng cho các banner thông báo
    // chỉ nên hiện khi có nội dung thật (VD: SuggestionMessage trong ConfigureUpdateSourceViewModel).
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
