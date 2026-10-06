using System.Windows;
using System.Windows.Input;

namespace SRM_by_Longtygu.Views
{
    // Lưu ý: giống EditSoftwareWindow / AddVersionWindow, cửa sổ này được DialogService khởi tạo
    // thủ công (new AttachResourceWindow { DataContext = vm }) chứ không qua DI container,
    // nên dùng constructor rỗng và tự bắt sự kiện CloseWindow từ ViewModel sau khi gán DataContext.
    public partial class AttachResourceWindow : Window
    {
        public AttachResourceWindow()
        {
            InitializeComponent();

            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is ViewModels.AttachResourceViewModel vm)
                {
                    vm.CloseWindow += () => this.Close();
                }
            };
        }

        // Cho phép kéo di chuyển cửa sổ bằng thanh titlebar tự chế
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