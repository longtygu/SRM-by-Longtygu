using System.Windows.Controls;

namespace SRM_by_Longtygu.Views
{
    public partial class DeploymentView : UserControl
    {
        public DeploymentView()
        {
            InitializeComponent();
        }

        private void DataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            string columnProperty = e.Column.SortMemberPath;
            if (DataContext is ViewModels.DeploymentViewModel vm)
            {
                vm.GroupOrSortBy(columnProperty);
            }
        }
    }
}