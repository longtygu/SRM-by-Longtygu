using Microsoft.Extensions.DependencyInjection;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services;
using System;
using System.Windows.Input;

namespace SRM_by_Longtygu.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private ViewModelBase _currentViewModel;

        // MỚI: trạng thái "đang bận" dùng chung toàn app; khi bận thì khóa mọi nút chuyển tab
        private readonly IBusyService _busy;
        public IBusyService Busy => _busy;

        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public ICommand NavigateDashboardCommand { get; }
        public ICommand NavigateLibraryCommand { get; }
        public ICommand NavigateDeploymentCommand { get; }
        public ICommand NavigatePresetsCommand { get; }
        public ICommand NavigateSettingsCommand { get; }
        public ICommand NavigateToolsCommand { get; }
        public ICommand ShowUninstallCommand { get; }
        public ICommand ShowAboutCommand { get; }

        // ĐÃ THÊM: Command điều hướng sang Thư viện Tài nguyên
        public ICommand NavigateResourceFileCommand { get; }

        // MỚI: Command điều hướng sang trang Trung tâm cập nhật (trang riêng, độc lập với Kho phần mềm)
        public ICommand NavigateUpdateCenterCommand { get; }

        private object _currentView;
        public object CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        private readonly ISoftwareVersionRepository _versionRepo;

        public MainViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _busy = _serviceProvider.GetRequiredService<IBusyService>();

            NavigateDashboardCommand = new RelayCommand(_ => CurrentViewModel = _serviceProvider.GetRequiredService<DashboardViewModel>(), CanNavigate);
            NavigateLibraryCommand = new RelayCommand(_ => CurrentViewModel = _serviceProvider.GetRequiredService<LibraryViewModel>(), CanNavigate);

            // ĐÃ THÊM: Khởi tạo Command gọi ResourceFileMainViewModel
            NavigateResourceFileCommand = new RelayCommand(_ => CurrentViewModel = _serviceProvider.GetRequiredService<ResourceFileMainViewModel>(), CanNavigate);

            NavigateDeploymentCommand = new RelayCommand(_ => CurrentViewModel = _serviceProvider.GetRequiredService<DeploymentViewModel>(), CanNavigate);
            NavigatePresetsCommand = new RelayCommand(_ => CurrentViewModel = _serviceProvider.GetRequiredService<PresetMainViewModel>(), CanNavigate);
            NavigateSettingsCommand = new RelayCommand(_ => CurrentViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>(), CanNavigate);
            NavigateToolsCommand = new RelayCommand(_ => CurrentViewModel = _serviceProvider.GetRequiredService<ToolsViewModel>(), CanNavigate);

            // MỚI: Điều hướng sang UpdateCenterViewModel (trang "Trung tâm cập nhật")
            NavigateUpdateCenterCommand = new RelayCommand(_ => CurrentViewModel = _serviceProvider.GetRequiredService<UpdateCenterViewModel>(), CanNavigate);

            CurrentViewModel = _serviceProvider.GetRequiredService<DashboardViewModel>();

            ShowUninstallCommand = new RelayCommand(_ =>
            {
                CurrentViewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                              .GetRequiredService<UninstallViewModel>(App.ServiceProvider);
            }, CanNavigate);
            ShowAboutCommand = new RelayCommand(_ =>
            {
                CurrentViewModel = _serviceProvider.GetRequiredService<AboutViewModel>();
            }, CanNavigate);
        }

        // MỚI: chỉ cho chuyển tab khi KHÔNG có tiến trình nào đang chạy
        private bool CanNavigate(object? parameter) => !_busy.IsBusy;
    }
}
