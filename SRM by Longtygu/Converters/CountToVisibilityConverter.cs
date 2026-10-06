using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SRM_by_Longtygu.Converters
{
    /// <summary>
    /// Chuyển số lượng phần tử (int) thành Visibility.
    /// Mặc định (không truyền ConverterParameter):
    ///   count == 0  -> Visible  (hiện thông báo "chưa có dữ liệu")
    ///   count > 0   -> Collapsed (ẩn thông báo, hiện danh sách)
    ///
    /// Truyền ConverterParameter="Invert" để đảo ngược logic, dùng cho các icon báo hiệu
    /// kiểu "chỉ hiện khi CÓ dữ liệu" (vd: icon ghim báo có tài nguyên đính kèm):
    ///   count > 0   -> Visible
    ///   count == 0  -> Collapsed
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = 0;
            if (value is int i) count = i;

            bool invert = parameter != null && parameter.ToString().Equals("Invert", StringComparison.OrdinalIgnoreCase);

            bool isVisible = invert ? count > 0 : count == 0;
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}