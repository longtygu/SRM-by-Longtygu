using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using SRM_by_Longtygu.Services;
using SRM_by_Longtygu.ViewModels;

namespace SRM_by_Longtygu
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ILocalizationService _loc;

        public MainWindow(MainViewModel viewModel, ILocalizationService localization)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _loc = localization;

            // Gắn ViewModel vào View để hệ thống Data Binding hoạt động
            DataContext = viewModel;

            // Lắng nghe khi CurrentViewModel đổi (chuyển tab) để phát hiệu ứng fade
            viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // MỚI: dòng mô tả tiến trình ở dải vàng - làm mới khi tên tiến trình đổi hoặc khi đổi ngôn ngữ
            _viewModel.Busy.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(IBusyService.CurrentOperation)) RefreshBusyDetail();
            };
            _loc.LanguageChanged += (s, e) => RefreshBusyDetail();
            RefreshBusyDetail();
        }

        private void RefreshBusyDetail()
        {
            BusyDetailTextBlock.Text = _loc.Format("Busy_Detail", _viewModel.Busy.CurrentOperation);
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
                    _loc.Format("Busy_CloseInProgress", _viewModel.Busy.CurrentOperation) + "\n\n" +
                    _loc.GetString("Busy_CloseWarning") + "\n" +
                    _loc.GetString("Busy_CloseQuestion"),
                    _loc.GetString("Busy_CloseTitle"),
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