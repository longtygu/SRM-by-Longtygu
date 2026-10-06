using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Tools.HashCalculator
{
    public partial class HashCalculatorView : UserControl
    {
        private readonly HashCalculatorViewModel _viewModel;

        public HashCalculatorView()
        {
            InitializeComponent();
            _viewModel = new HashCalculatorViewModel();
            DataContext = _viewModel;
        }

        // Cho phép kéo thả file vào bất kỳ đâu trong khung tool (RootGrid AllowDrop="True")
        private void RootGrid_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void RootGrid_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                // Chỉ lấy file, bỏ qua nếu người dùng kéo nhầm thư mục
                var files = paths.Where(File.Exists);
                _viewModel.AddFiles(files);
            }

            e.Handled = true;
        }
    }
}
