using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input; // Bắt buộc phải có để sử dụng MouseButtonEventArgs
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SRM_by_Longtygu.Views
{
    /// <summary>
    /// Interaction logic for AddVersionWindow.xaml
    /// </summary>
    public partial class AddVersionWindow : Window
    {
        public AddVersionWindow()
        {
            InitializeComponent();
        }

        // Cho phép kéo di chuyển cửa sổ bằng cách nhấn giữ vào thanh titlebar tự chế
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        // Nút đóng (X) ở góc phải titlebar
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}