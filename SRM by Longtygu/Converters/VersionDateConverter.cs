using System;
using System.Globalization;
using System.Windows.Data;

namespace SRM_by_Longtygu.Converters
{
    // Chuỗi ngày giờ lưu trong DB ("yyyy-MM-dd HH:mm:ss") -> chuỗi hiển thị theo ngôn ngữ đang chọn.
    // Rỗng / NULL -> "-". Không đọc được -> hiển thị nguyên văn. KHÔNG sửa dữ liệu trong DB.
    public class VersionDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            if (string.IsNullOrWhiteSpace(text)) return "-";

            if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
            {
                return dt.ToString("g", Loc.Culture);
            }
            return text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
