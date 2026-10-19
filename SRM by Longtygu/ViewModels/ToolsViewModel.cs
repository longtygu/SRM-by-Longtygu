using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Plugins;
using SRM_by_Longtygu.Services;
using SRM_by_Longtygu.Tools.DiskHealth;
using SRM_by_Longtygu.Tools.HardwareInfo;
using SRM_by_Longtygu.Tools.HashCalculator;
using SRM_by_Longtygu.Tools.RamTest;
using SRM_by_Longtygu.Tools.WindowsHealthCheck;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SRM_by_Longtygu.ViewModels
{
    public class ToolsViewModel : ViewModelBase
    {
        private readonly IPluginService _pluginService;
        private readonly IServiceProvider _serviceProvider;

        public ObservableCollection<IToolModule> AvailableTools { get; } = new ObservableCollection<IToolModule>();

        public bool HasNoTools => AvailableTools.Count == 0;

        private IToolModule _selectedTool;
        public IToolModule SelectedTool
        {
            get => _selectedTool;
            set
            {
                if (SetProperty(ref _selectedTool, value) && value != null)
                {
                    ActiveToolView = value.CreateView(_serviceProvider);
                }
            }
        }

        private UserControl _activeToolView;
        public UserControl ActiveToolView
        {
            get => _activeToolView;
            set
            {
                if (SetProperty(ref _activeToolView, value))
                {
                    OnPropertyChanged(nameof(IsToolOpen));
                }
            }
        }

        public bool IsToolOpen => ActiveToolView != null;

        // Tiền tố quy ước cho Id của các công cụ tích hợp sẵn (không cho phép xóa)
        private const string BuiltInIdPrefix = "builtin.";

        private bool _isManageMode;
        public bool IsManageMode
        {
            get => _isManageMode;
            set
            {
                if (SetProperty(ref _isManageMode, value))
                {
                    OnPropertyChanged(nameof(ManageButtonLabel));
                }
            }
        }

        // Nhãn nút "Quản lý công cụ" đổi thành "Xong" khi đang ở chế độ quản lý
        public string ManageButtonLabel => IsManageMode ? Loc.T("Tools_Done") : Loc.T("Tools_ManageTools");

        public ICommand OpenToolCommand { get; }
        public ICommand BackToListCommand { get; }
        public ICommand AddToolCommand { get; }
        public ICommand ToggleManageModeCommand { get; }
        public ICommand DeleteToolCommand { get; }
        public ICommand ExportToolCommand { get; }

        public ToolsViewModel(IPluginService pluginService, IServiceProvider serviceProvider)
        {
            _pluginService = pluginService;
            _serviceProvider = serviceProvider;

            LoadAllTools();

            // Khu vực duyệt (card) và khu vực quản lý (bảng) hiển thị tách biệt theo IsManageMode,
            // nên card duyệt luôn mở tool bình thường khi được click.
            OpenToolCommand = new RelayCommand(t => SelectedTool = t as IToolModule);
            BackToListCommand = new RelayCommand(_ =>
            {
                SelectedTool = null;
                ActiveToolView = null;
            });
            AddToolCommand = new RelayCommand(_ => AddToolFromFile());
            ToggleManageModeCommand = new RelayCommand(_ => IsManageMode = !IsManageMode);
            DeleteToolCommand = new RelayCommand(t => DeleteTool(t as IToolModule));
            ExportToolCommand = new RelayCommand(t => ExportTool(t as IToolModule));
        }

        // Nạp lại danh sách: công cụ tích hợp sẵn (built-in) + công cụ nạp động từ thư mục Plugins
        private void LoadAllTools()
        {
            AvailableTools.Clear();

            // Công cụ tích hợp sẵn, không cần thả file .dll
            AvailableTools.Add(new DiskHealthToolModule());
            AvailableTools.Add(new RamTestToolModule());
            AvailableTools.Add(new HardwareInfoToolModule());
            AvailableTools.Add(new WindowsHealthCheckToolModule());
            AvailableTools.Add(new HashCalculatorToolModule());

            // Công cụ nạp động (thả tay hoặc cài qua nút "Thêm công cụ")
            foreach (var tool in _pluginService.LoadTools())
            {
                AvailableTools.Add(tool);
            }

            OnPropertyChanged(nameof(HasNoTools));
        }

        // Xóa 1 công cụ (chỉ áp dụng cho công cụ cài từ file .dll, không áp dụng cho tool tích hợp sẵn)
        private void DeleteTool(IToolModule tool)
        {
            if (tool == null) return;

            if (tool.Id != null && tool.Id.StartsWith(BuiltInIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(Loc.T("Tools_BuiltInToolsOf"), Loc.T("Tools_CannotDelete"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                Loc.F("Tools_AreYouSureYou", tool.Name) + "\n" + Loc.T("Tools_ThisActionCannotBe"),
                Loc.T("Tools_ConfirmToolDeletion"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            if (_pluginService.RemoveTool(tool.Id, out var error))
            {
                if (ReferenceEquals(SelectedTool, tool))
                {
                    SelectedTool = null;
                    ActiveToolView = null;
                }

                AvailableTools.Remove(tool);
                OnPropertyChanged(nameof(HasNoTools));

                if (error == "PENDING_RESTART")
                {
                    MessageBox.Show(
                        Loc.T("Tools_TheToolWasRemoved"),
                        Loc.T("Tools_ToolRemoved"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show(Loc.T("Tools_CouldNotDeleteThe") + "\n" + error, Loc.T("Common_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Xuất (copy) file .dll của công cụ ra vị trí người dùng chọn — dùng để backup/chia sẻ giữa các máy
        private void ExportTool(IToolModule tool)
        {
            if (tool == null) return;

            if (tool.Id != null && tool.Id.StartsWith(BuiltInIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(Loc.T("Tools_BuiltInToolsAre"),
                    Loc.T("Tools_CannotExport"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _pluginService.TryGetSourceFileName(tool.Id, out var suggestedFileName);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.F("Tools_ExportTool0", tool.Name),
                Filter = Loc.T("Tools_ToolLibrariesDllDll"),
                FileName = suggestedFileName ?? (tool.Name + ".dll")
            };

            if (dialog.ShowDialog() != true) return;

            if (_pluginService.ExportTool(tool.Id, dialog.FileName, out var error))
            {
                MessageBox.Show(Loc.F("Tools_Tool0ExportedSuccessfully", tool.Name), Loc.T("Tools_ExportTool"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(Loc.T("Tools_CouldNotExportThe") + "\n" + error, Loc.T("Common_Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Mở hộp thoại chọn file .dll, copy vào Plugins/ rồi nạp lại danh sách ngay
        private void AddToolFromFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Tools_ChooseAToolFile"),
                Filter = Loc.T("Tools_ToolLibrariesDllDll")
            };

            if (dialog.ShowDialog() == true)
            {
                if (_pluginService.InstallToolFromFile(dialog.FileName, out var error))
                {
                    LoadAllTools();
                    MessageBox.Show(Loc.T("Tools_ToolInstalledSuccessfully"), Loc.T("PresetMain_Success"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(Loc.T("Tools_CouldNotInstallThe") + "\n" + error, Loc.T("Common_Error"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}