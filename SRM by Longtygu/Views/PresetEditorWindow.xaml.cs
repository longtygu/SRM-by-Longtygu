using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Views
{
    public partial class PresetEditorWindow : Window
    {
        public PresetEditorWindow()
        {
            InitializeComponent();
        }

        // Sự kiện khi bấm nút HỦY BỎ
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // Sự kiện khi bấm nút LƯU GÓI
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }
        // Hàm giúp người dùng click giữ chuột vào thanh tiêu đề mới để di chuyển cửa sổ
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        // Hàm cho nút X (Đóng cửa sổ)
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // MỚI: Ép ComboBox "Phiên bản" trong giỏ hàng tự chọn đúng phiên bản mặc định ngay khi vừa render,
        // KHÔNG phụ thuộc vào thứ tự áp dụng ItemsSource/SelectedItem/SelectedIndex của WPF (vốn không
        // đáng tin cậy trong DataGridTemplateColumn.CellTemplate kết hợp DataGridCell custom template).
        // - Nếu ViewModel đã có sẵn SelectedVersion hợp lệ (nằm trong Versions) -> tôn trọng lựa chọn đó.
        // - Nếu chưa có -> tự chọn phiên bản CUỐI CÙNG trong danh sách Versions (giả định là bản mới nhất).
        private void VersionComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is ComboBox comboBox) || !(comboBox.DataContext is Software software)) return;
            if (software.Versions == null || software.Versions.Count == 0) return;

            if (software.SelectedVersion != null && software.Versions.Contains(software.SelectedVersion))
            {
                comboBox.SelectedItem = software.SelectedVersion;
                return;
            }

            var defaultVersion = software.Versions.Last();
            comboBox.SelectedItem = defaultVersion;
            software.SelectedVersion = defaultVersion;
        }
    }
}