using System;
using System.Globalization;
using System.Windows.Data;
using SRM_by_Longtygu.Services;

namespace SRM_by_Longtygu.Converters
{
    // Giá trị danh mục lưu trong DB -> chữ hiển thị theo ngôn ngữ (xem CategoryCatalog). Không ghi ngược lại DB.
    public class CategoryDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return CategoryCatalog.Display(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
