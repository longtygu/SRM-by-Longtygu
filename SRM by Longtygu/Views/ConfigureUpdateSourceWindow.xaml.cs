using System.Windows;
using System.Windows.Input;

namespace SRM_by_Longtygu.Views
{
    public partial class ConfigureUpdateSourceWindow : Window
    {
        public ConfigureUpdateSourceWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
