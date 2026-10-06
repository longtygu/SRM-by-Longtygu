using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace SRM_by_Longtygu.Helpers
{
    /// <summary>
    /// Dùng trong ResourceFileMainView để xác định icon hiển thị cho 1 ResourceFile,
    /// giống cách một thư viện phần mềm tự hiển thị icon thật của từng file.
    ///
    /// Thứ tự ưu tiên:
    /// 1) Icon do người dùng tự chọn thủ công (IconPath) - luôn được ưu tiên cao nhất
    /// 2) Icon tự động trích xuất từ chính file tài nguyên (FolderPath + FileName), dùng lại
    ///    FilePathToIconConverter/IconExtractor đã có sẵn - áp dụng khi tài nguyên là 1 file đơn
    /// 3) null -> icon dự phòng (Material icon) đặt phía sau trong XAML sẽ tự lộ ra
    ///
    /// values[0] = IconPath, values[1] = FolderPath, values[2] = FileName
    /// </summary>
    public class CustomOrAutoIconConverter : IMultiValueConverter
    {
        private static readonly ImagePathConverter _customImageLoader = new ImagePathConverter();
        private static readonly FilePathToIconConverter _autoIconExtractor = new FilePathToIconConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string iconPath = values.Length > 0 ? values[0] as string : null;
            string folderPath = values.Length > 1 ? values[1] as string : null;
            string fileName = values.Length > 2 ? values[2] as string : null;

            // Ưu tiên 1: icon người dùng tự chọn
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                var custom = _customImageLoader.Convert(iconPath, targetType, null, culture);
                if (custom != null) return custom;
            }

            // Ưu tiên 2: icon tự động trích xuất từ file tài nguyên thật
            // (chỉ áp dụng được khi tài nguyên là 1 file đơn, không áp dụng cho dạng thư mục nhiều file)
            if (!string.IsNullOrWhiteSpace(folderPath) && !string.IsNullOrWhiteSpace(fileName))
            {
                string fullPath = Path.Combine(folderPath, fileName);
                if (File.Exists(fullPath))
                {
                    var auto = _autoIconExtractor.Convert(fullPath, targetType, null, culture);
                    if (auto != null) return auto;
                }
            }

            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
