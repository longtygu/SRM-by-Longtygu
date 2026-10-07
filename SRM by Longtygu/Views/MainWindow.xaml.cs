using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using SRM_by_Longtygu.ViewModels;

namespace SRM_by_Longtygu
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;

            // Gắn ViewModel vào View để hệ thống Data Binding hoạt động
            DataContext = viewModel;

            // Lắng nghe khi CurrentViewModel đổi (chuyển tab) để phát hiệu ứng fade
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentViewModel))
            {
                PlayContentTransition();
            }
        }

        private void PlayContentTransition()
        {
            var storyboard = (Storyboard)FindResource("ContentFadeInStoryboard");
            storyboard.Begin(MainContentControl);
        }

        // ============ CHẶN ĐÓNG APP KHI ĐANG CÓ TIẾN TRÌNH CHẠY ============
        // Áp dụng cho cả nút X tự vẽ, Alt+F4 và đóng từ thanh tác vụ.
        // Mặc định chọn "Không" và vẫn cho phép đóng nếu người dùng xác nhận, để app không bị kẹt khi tiến trình treo.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_viewModel.Busy.IsBusy)
            {
                var result = MessageBox.Show(
                    $"Đang thực hiện: {_viewModel.Busy.CurrentOperation}.\n\n" +
                    "Đóng ứng dụng lúc này có thể làm gián đoạn tiến trình và gây lỗi dữ liệu.\n" +
                    "Bạn vẫn muốn đóng ứng dụng?",
                    "Tiến trình đang chạy",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                }
            }

            base.OnClosing(e);
        }

        // ============ XỬ LÝ NÚT TITLE BAR TỰ VẼ ============
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized)
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}