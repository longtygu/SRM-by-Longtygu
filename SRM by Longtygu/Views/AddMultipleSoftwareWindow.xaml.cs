using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SRM_by_Longtygu.ViewModels;

namespace SRM_by_Longtygu.Views
{
    public partial class AddMultipleSoftwareWindow : Window
    {
        public AddMultipleSoftwareWindow(AddMultipleSoftwareViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Lắng nghe sự kiện từ ViewModel để tự đóng cửa sổ khi xử lý xong toàn bộ hàng đợi
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

        // Cảnh báo nếu người dùng đóng cửa sổ khi còn phần mềm chưa được lưu trong hàng đợi
        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is AddMultipleSoftwareViewModel vm && vm.Queue.Count > 0 && !vm.IsSaving)
            {
                var result = MessageBox.Show(
                    $"Bạn còn {vm.Queue.Count} phần mềm chưa được lưu trong hàng đợi.\nBạn có chắc muốn đóng và bỏ qua không?",
                    "Còn phần mềm chưa lưu",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }
    }
}
