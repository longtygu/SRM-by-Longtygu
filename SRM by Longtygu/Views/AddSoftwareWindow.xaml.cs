using System.Windows;
using System.Windows.Input;
using SRM_by_Longtygu.ViewModels;

namespace SRM_by_Longtygu.Views
{
    public partial class AddSoftwareWindow : Window
    {
        public AddSoftwareWindow(AddSoftwareViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Lắng nghe sự kiện từ ViewModel để tự đóng cửa sổ khi load file xong
            viewModel.CloseWindow += () => this.Close();
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
