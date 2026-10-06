using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;

namespace SRM_by_Longtygu.Views
{
    public partial class SettingsView : UserControl
    {
        // Đang đăng ký lắng nghe log của ViewModel nào (để hủy đăng ký, tránh bị đăng ký trùng khi View load nhiều lần)
        private ViewModels.SettingsViewModel? _subscribedViewModel;

        public SettingsView()
        {
            InitializeComponent();

            // Card "Cập nhật ứng dụng" dùng ViewModel riêng (Singleton) nên SettingsViewModel không phải thay đổi.
            // Kiểm tra null để không lỗi khi Visual Studio/VS Code mở trình thiết kế XAML (lúc đó chưa có ServiceProvider).
            if (App.ServiceProvider != null)
            {
                AppUpdateCard.DataContext = App.ServiceProvider.GetRequiredService<ViewModels.AppUpdateViewModel>();
            }

            // Đăng ký sự kiện khi View load xong / unload
            this.Loaded += SettingsView_Loaded;
            this.Unloaded += SettingsView_Unloaded;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is ViewModels.SettingsViewModel viewModel)
            {
                // Gỡ đăng ký cũ (nếu có) rồi mới đăng ký lại => không bao giờ bị trùng
                if (_subscribedViewModel != null)
                {
                    _subscribedViewModel.OperationLogs.CollectionChanged -= OperationLogs_CollectionChanged;
                }

                _subscribedViewModel = viewModel;
                // Lắng nghe sự kiện thêm log mới để tự động cuộn (Auto-scroll)
                viewModel.OperationLogs.CollectionChanged += OperationLogs_CollectionChanged;
            }
        }

        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.OperationLogs.CollectionChanged -= OperationLogs_CollectionChanged;
                _subscribedViewModel = null;
            }
        }

        private void OperationLogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null)
            {
                LogConsole.ScrollIntoView(args.NewItems[args.NewItems.Count - 1]);
            }
        }
    }
}