using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SRM_by_Longtygu.Converters
{
    // Software.DownloadProgressPercent >= 0 -> có % thật (tải qua HTTP) -> hiện thanh tiến trình
    public class NonNegativeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int percent = value is int i ? i : -1;
            return percent >= 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // Software.DownloadProgressPercent < 0 -> không xác định được % (VD: cài qua Winget) -> hiện icon xoay
    public class NegativeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int percent = value is int i ? i : -1;
            return percent < 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
