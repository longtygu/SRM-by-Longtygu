using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SRM_by_Longtygu.Helpers
{
    /// <summary>
    /// Dùng trong XAML để trích icon thật của file .exe/.msi thông qua IconExtractor.
    /// Bind trực tiếp vào Software.FilePath (đường dẫn file cài đặt), không cần cột dữ liệu mới.
    /// Nếu trích xuất thất bại (file không tồn tại, không có icon...) trả về null,
    /// khi đó Image sẽ trong suốt và để lộ icon dự phòng (fallback) đặt phía sau nó trong XAML.
    /// </summary>
    public class FilePathToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string filePath = value as string;
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            ImageSource icon = IconExtractor.GetIconFromPath(filePath);
            return icon; // null nếu không trích được -> fallback icon phía sau sẽ tự động lộ ra
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
