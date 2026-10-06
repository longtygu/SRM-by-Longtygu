using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SRM_by_Longtygu.Converters
{
    /// <summary>
    /// Trả về Visibility.Visible khi chuỗi truyền vào null/rỗng (dùng để hiện icon fallback
    /// khi phần mềm chưa có IconPath), và Collapsed khi đã có giá trị (ẩn fallback đi,
    /// tránh chồng chéo/lộ viền với icon thật đè lên trên).
    /// </summary>
    public class NullOrEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string;
            return string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
