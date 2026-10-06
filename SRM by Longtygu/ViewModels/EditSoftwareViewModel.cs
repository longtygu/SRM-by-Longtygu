using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services;

namespace SRM_by_Longtygu.ViewModels
{
    public class EditSoftwareViewModel : ViewModelBase
    {
        private readonly ISoftwareRepository _softwareRepo;
        private readonly IDialogService _dialogService;
        private readonly Software _originalSoftware;

        public ObservableCollection<string> Categories { get; } = new ObservableCollection<string>
        {
            "Trình duyệt Web",
            "Tiện ích hệ thống",
            "Văn phòng & Công việc",
            "Thiết kế & Đồ họa",
            "Đa phương tiện (Media)",
            "Lập trình & IT",
            "Bảo mật & Diệt Virus",
            "Trò chơi (Games)",
            "Khác"
        };
        
        private string _name; public string Name { get => _name; set => SetProperty(ref _name, value); }
        private string _publisher; public string Publisher { get => _publisher; set => SetProperty(ref _publisher, value); }
        private string _category; public string Category { get => _category; set => SetProperty(ref _category, value); }
        private string _description; public string Description { get => _description; set => SetProperty(ref _description, value); }
        private string _licenseKey; public string LicenseKey { get => _licenseKey; set => SetProperty(ref _licenseKey, value); }
        private string _readme; public string Readme { get => _readme; set => SetProperty(ref _readme, value); }
        private string _silentInstallCommand;
        public string SilentInstallCommand { get => _silentInstallCommand; set => SetProperty(ref _silentInstallCommand, value); }

        private string _silentUninstallCommand;
        public string SilentUninstallCommand { get => _silentUninstallCommand; set => SetProperty(ref _silentUninstallCommand, value); }
        public ICommand SaveCommand { get; }
        public event Action RequestClose;

        public EditSoftwareViewModel(ISoftwareRepository softwareRepo, IDialogService dialogService, Software softwareToEdit)
        {
            _softwareRepo = softwareRepo;
            _dialogService = dialogService;
            _originalSoftware = softwareToEdit;
            SilentInstallCommand = softwareToEdit.SilentInstallCommand;
            SilentUninstallCommand = softwareToEdit.SilentUninstallCommand;

            // Nạp dữ liệu cũ vào form
            Name = softwareToEdit.Name; Publisher = softwareToEdit.Publisher; Category = softwareToEdit.Category;
            Description = softwareToEdit.Description; LicenseKey = softwareToEdit.LicenseKey; Readme = softwareToEdit.Readme;

            SaveCommand = new RelayCommand(_ => _ = ExecuteSaveAsync());
        }

        private async Task ExecuteSaveAsync()
        {
            _originalSoftware.Name = Name; _originalSoftware.Publisher = Publisher; _originalSoftware.Category = Category;
            _originalSoftware.Description = Description; _originalSoftware.LicenseKey = LicenseKey; _originalSoftware.Readme = Readme;
            _originalSoftware.SilentInstallCommand = SilentInstallCommand?.Trim();
            _originalSoftware.SilentUninstallCommand = SilentUninstallCommand?.Trim();
            await _softwareRepo.UpdateAsync(_originalSoftware);
            _dialogService.ShowMessage("Cập nhật thông tin phần mềm thành công!", "Thành công");
            RequestClose?.Invoke();
        }
    }
}