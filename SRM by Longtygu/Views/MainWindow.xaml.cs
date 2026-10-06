using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using SRM_by_Longtygu.ViewModels;

namespace SRM_by_Longtygu
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();

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