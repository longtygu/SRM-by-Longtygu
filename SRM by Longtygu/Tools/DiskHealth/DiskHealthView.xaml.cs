using System.Windows.Controls;

namespace SRM_by_Longtygu.Tools.DiskHealth
{
    public partial class DiskHealthView : UserControl
    {
        public DiskHealthView()
        {
            InitializeComponent();
            DataContext = new DiskHealthViewModel();
        }
    }
}