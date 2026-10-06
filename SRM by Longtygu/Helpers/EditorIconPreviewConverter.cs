using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace SRM_by_Longtygu.Helpers
{
    /// <summary>
    /// Dùng trong ResourceFileEditorWindow để xem trước icon NGAY khi đang chỉnh sửa,
    /// trước khi người dùng bấm Lưu (lúc này file có thể chưa được copy vào thư viện).
    ///
    /// Thứ tự ưu tiên:
    /// 1) Icon do người dùng tự chọn thủ công (IconPath)
    /// 2) File vừa chọn ở "Chọn File" nhưng CHƯA lưu (SourceFilePath)
    /// 3) File đã lưu từ trước, khi đang sửa 1 tài nguyên có sẵn mà chưa đổi file (ExistingFilePath)
    ///
    /// values[0] = IconPath, values[1] = SourceFilePath, values[2] = ExistingFilePath
    /// </summary>
    public class EditorIconPreviewConverter : IMultiValueConverter
    {
        private static readonly ImagePathConverter _customImageLoader = new ImagePathConverter();
        private static readonly FilePathToIconConverter _autoIconExtractor = new FilePathToIconConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string iconPath = values.Length > 0 ? values[0] as string : null;
            string sourceFilePath = values.Length > 1 ? values[1] as string : null;
            string existingFilePath = values.Length > 2 ? values[2] as string : null;

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                var custom = _customImageLoader.Convert(iconPath, targetType, null, culture);
                if (custom != null) return custom;
            }

            if (!string.IsNullOrWhiteSpace(sourceFilePath) && File.Exists(sourceFilePath))
            {
                var auto = _autoIconExtractor.Convert(sourceFilePath, targetType, null, culture);
                if (auto != null) return auto;
            }

            if (!string.IsNullOrWhiteSpace(existingFilePath) && File.Exists(existingFilePath))
            {
                var auto = _autoIconExtractor.Convert(existingFilePath, targetType, null, culture);
                if (auto != null) return auto;
            }

            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
