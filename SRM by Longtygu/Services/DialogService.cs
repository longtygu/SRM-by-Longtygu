using Microsoft.Win32;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SRM_by_Longtygu.Models; // Dòng này chính là chìa khóa để fix lỗi CS0246

namespace SRM_by_Longtygu.Services
{
    public interface IDialogService
    {
        string OpenFileDialog(string filter);
        void ShowMessage(string message, string title);
        void ShowAddSoftwareDialog();
        void ShowAddMultipleSoftwareDialog(); // MỚI: mở cửa sổ thêm nhiều phần mềm cùng lúc
        void ShowEditSoftwareDialog(Software software); // Bây giờ nó đã hiểu Software là gì
        void ShowAddVersionDialog(Software parentSoftware);
        void ShowAttachResourceDialog(Software parentSoftware); // MỚI: mở cửa sổ đính kèm tài nguyên cho 1 phần mềm

        // MỚI: mở cửa sổ cấu hình nguồn kiểm tra cập nhật cho 1 phần mềm
        void ShowConfigureUpdateSourceDialog(Software software);

        // MỚI: mở cửa sổ hướng dẫn sử dụng Trung tâm cập nhật
        void ShowUpdateCenterGuideDialog();
    }

    public class DialogService : IDialogService
    {
        public string OpenFileDialog(string filter)
        {
            var dialog = new OpenFileDialog { Filter = filter, Title = "Chọn file bộ cài đặt" };
            return dialog.ShowDialog() == true ? dialog.FileName : string.Empty;
        }

        public void ShowMessage(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowAddSoftwareDialog()
        {
            // Chỉ cần lấy Window ra (DI Container sẽ tự động bơm ViewModel vào cho nó)
            var window = App.ServiceProvider.GetRequiredService<SRM_by_Longtygu.Views.AddSoftwareWindow>();

            // Xóa các dòng gán ViewModel và CloseWindow cũ đi
            window.ShowDialog();
        }

        // MỚI: mở cửa sổ riêng biệt cho phép chọn & thêm nhiều phần mềm cùng lúc
        public void ShowAddMultipleSoftwareDialog()
        {
            var window = App.ServiceProvider.GetRequiredService<SRM_by_Longtygu.Views.AddMultipleSoftwareWindow>();
            window.ShowDialog();
        }

        public void ShowEditSoftwareDialog(Software software)
        {
            var repo = App.ServiceProvider.GetRequiredService<Repositories.ISoftwareRepository>();
            var dialogService = App.ServiceProvider.GetRequiredService<IDialogService>();

            var vm = new ViewModels.EditSoftwareViewModel(repo, dialogService, software);
            var window = new Views.EditSoftwareWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };

            vm.RequestClose += () => window.Close();
            window.ShowDialog();
        }
        public void ShowAddVersionDialog(Software parentSoftware)
        {
            var versionRepo = App.ServiceProvider.GetRequiredService<Repositories.ISoftwareVersionRepository>();
            var vm = new ViewModels.AddVersionViewModel(versionRepo, parentSoftware);
            var window = new Views.AddVersionWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };

            // Lệnh tự đóng cửa sổ
            vm.CloseWindow += () => window.Close();
            window.ShowDialog();
        }

        // MỚI: mở cửa sổ "Đính kèm tài nguyên" cho 1 phần mềm cụ thể (tìm kiếm + chọn nhiều bằng checkbox)
        public void ShowAttachResourceDialog(Software parentSoftware)
        {
            var resourceRepo = App.ServiceProvider.GetRequiredService<Repositories.IResourceFileRepository>();
            var mappingRepo = App.ServiceProvider.GetRequiredService<Repositories.ISoftwareResourceRepository>();

            var vm = new ViewModels.AttachResourceViewModel(resourceRepo, mappingRepo, parentSoftware);
            var window = new Views.AttachResourceWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };

            window.ShowDialog();
        }

        // MỚI: mở cửa sổ cấu hình nguồn kiểm tra cập nhật (GitHub Releases / Regex trang web) cho 1 phần mềm
        public void ShowConfigureUpdateSourceDialog(Software software)
        {
            var softwareRepo = App.ServiceProvider.GetRequiredService<Repositories.ISoftwareRepository>();

            var vm = new ViewModels.ConfigureUpdateSourceViewModel(softwareRepo, software);
            var window = new Views.ConfigureUpdateSourceWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };

            vm.CloseWindow += () => window.Close();
            window.ShowDialog();
        }

        // MỚI: mở cửa sổ hướng dẫn sử dụng Trung tâm cập nhật
        public void ShowUpdateCenterGuideDialog()
        {
            var window = new Views.UpdateCenterGuideWindow
            {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
        }
    }
}
