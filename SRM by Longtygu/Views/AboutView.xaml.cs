using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Views
{
    public partial class AboutView : UserControl
    {
        private const string FacebookUrl = "https://www.facebook.com/longtygu/";

        public AboutView()
        {
            InitializeComponent();
        }

        private void FacebookButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // .NET 8: bắt buộc UseShellExecute = true để mở URL bằng trình duyệt mặc định
                Process.Start(new ProcessStartInfo(FacebookUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không mở được trình duyệt.\nHãy truy cập thủ công: {FacebookUrl}\n\n{ex.Message}",
                                "SRM by Longtygu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}