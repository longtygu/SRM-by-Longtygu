using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SRM_by_Longtygu.Converters
{
    // Chuyển đường dẫn tương đối lưu trong DB (VD: "Data\VLC\VLC_icon.png") thành ImageSource để Image hiển thị.
    // Trả về null nếu không có đường dẫn hoặc file không còn tồn tại trên đĩa — Image sẽ không hiển thị gì
    // (không throw exception làm crash UI), cho phép icon placeholder phía sau lộ ra.
    public class IconPathToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string relativePath = value as string;
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            try
            {
                string fullPath = Path.IsPathRooted(relativePath)
                    ? relativePath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

                if (!File.Exists(fullPath)) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // đọc hết vào RAM ngay, tránh khoá file icon trên đĩa
                bitmap.EndInit();
                bitmap.Freeze(); // cho phép dùng an toàn dù binding chạy trên thread khác
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
