using System;
using System.Globalization;
using System.Windows.Data;

namespace SRM_by_Longtygu.Helpers
{
    public class StringContainsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text && parameter is string searchKeyword)
            {
                // Kiểm tra xem nội dung Log có chứa từ khóa (như "WARN" hoặc "ERROR") không
                return text.Contains(searchKeyword, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}