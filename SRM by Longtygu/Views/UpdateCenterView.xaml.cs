using System.Windows;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Views
{
    public partial class UpdateCenterView : UserControl
    {
        public UpdateCenterView()
        {
            InitializeComponent();
            Loaded += UpdateCenterView_Loaded;
        }

        // MỚI: Tự động hiện hướng dẫn lần đầu tiên người dùng mở trang này (ViewModel tự kiểm tra
        // đã xem chưa qua settings.json, chỉ hiện đúng 1 lần trong suốt vòng đời sử dụng app)
        private void UpdateCenterView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.UpdateCenterViewModel vm)
            {
                vm.ShowGuideIfFirstTime();
            }
        }
    }
}
