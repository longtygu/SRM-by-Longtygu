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

namespace SRM_by_Longtygu.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();

            // Đăng ký sự kiện khi View load xong
            this.Loaded += SettingsView_Loaded;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is ViewModels.SettingsViewModel viewModel)
            {
                // Lắng nghe sự kiện thêm log mới để tự động cuộn (Auto-scroll)
                viewModel.OperationLogs.CollectionChanged += (s, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null)
                    {
                        LogConsole.ScrollIntoView(args.NewItems[args.NewItems.Count - 1]);
                    }
                };
            }
        }
    }
}