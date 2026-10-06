using System;
using System.Globalization;
using System.Windows.Data;

namespace SRM_by_Longtygu.Converters
{
    // Dùng cho thanh tiến trình tự vẽ bằng Border (tránh dùng ProgressBar mặc định của WPF vì
    // control template hệ thống dễ dính lỗi màu sắc tương tự ComboBox đã gặp trước đó).
    // values[0] = % tiến trình (0-100, hoặc -1 nếu không xác định)
    // values[1] = ActualWidth của khung chứa (Grid cha)
    // Trả về độ rộng (double) của phần "đã tải" bên trong khung chứa.
    public class ProgressPercentToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return 0.0;

            if (!double.TryParse(values[0]?.ToString(), out double percent)) return 0.0;
            if (!double.TryParse(values[1]?.ToString(), out double totalWidth)) return 0.0;

            if (percent < 0) return 0.0; // -1 = không xác định, phần gọi UI nên hiện icon xoay thay vì thanh này

            double clamped = Math.Max(0, Math.Min(100, percent));
            return totalWidth * (clamped / 100.0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
