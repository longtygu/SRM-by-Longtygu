using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Tools.WindowsHealthCheck
{
    public partial class WindowsHealthCheckView : UserControl
    {
        private readonly WindowsHealthCheckViewModel _viewModel;

        public WindowsHealthCheckView()
        {
            InitializeComponent();
            _viewModel = new WindowsHealthCheckViewModel();
            DataContext = _viewModel;

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WindowsHealthCheckViewModel.LogText))
            {
                // Tự cuộn xuống cuối nhật ký mỗi khi có dòng log mới, giống cảm giác console thật
                Dispatcher.InvokeAsync(() => LogScrollViewer.ScrollToEnd());
            }
        }

        // Khởi động lại toàn bộ app với quyền Admin — theo đúng pattern đã dùng ở
        // RestartAsAdminButton_Click trong HardwareInfoView (app chạy thường, tool tự xin quyền).
        private void RestartAsAdminButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                var psi = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                Application.Current.Shutdown();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Người dùng bấm "Không" ở hộp thoại UAC -> bỏ qua, không làm gì
            }
        }
    }
}
