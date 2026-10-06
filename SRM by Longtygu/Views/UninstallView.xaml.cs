using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SRM_by_Longtygu.Views
{
    /// <summary>
    /// Interaction logic for UninstallView.xaml
    /// </summary>
    public partial class UninstallView : UserControl
    {
        public UninstallView()
        {
            InitializeComponent();
            // Kiểm tra dòng này: Nếu thiếu, giao diện sẽ không bao giờ hiện dữ liệu!
            // Cách tốt nhất là để cho DI Container của App tự gán DataContext từ App.xaml
        }
    }
}
