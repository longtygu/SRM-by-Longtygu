using System;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SRM_by_Longtygu.Views
{
    // BỘ CHUYỂN ĐỔI ĐƯỜNG DẪN ẢNH TỪ DB RA GIAO DIỆN
    

    public partial class LibraryView : UserControl
    {
        public LibraryView()
        {
            InitializeComponent();
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            string columnProperty = e.Column.SortMemberPath;

            if (DataContext is ViewModels.LibraryViewModel vm)
            {
                vm.GroupOrSortBy(columnProperty);
            }
        }
    }
}