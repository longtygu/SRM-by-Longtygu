using System;
using System.Globalization;
using System.Windows.Data;

namespace SRM_by_Longtygu.Converters
{
    // Đảo ngược giá trị bool - dùng cho IsEnabled="{Binding IsDownloadingUpdate, Converter=...}"
    // để disable nút thao tác trong lúc đang tải, tránh bấm chồng nhiều lệnh cùng lúc trên cùng 1 phần mềm.
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : true;
    }
}
