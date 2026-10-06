using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Dapper;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Database;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services;

namespace SRM_by_Longtygu.ViewModels
{
    public class PresetMainViewModel : ViewModelBase
    {
        private readonly IDeploymentService _deployService;
        private readonly ISoftwareRepository _softwareRepo;
        private readonly IPresetRepository _presetRepo;
        private readonly IDatabaseConnectionFactory _dbFactory;
        private readonly ISoftwareResourceRepository _resourceMappingRepo; // MỚI: để nạp & mở tài nguyên đính kèm

        public ObservableCollection<Preset> Presets { get; set; } = new ObservableCollection<Preset>();

        private ObservableCollection<Software> _selectedPresetSoftwares = new ObservableCollection<Software>();
        public ObservableCollection<Software> SelectedPresetSoftwares
        {
            get => _selectedPresetSoftwares;
            set => SetProperty(ref _selectedPresetSoftwares, value);
        }

        private Preset _selectedPreset;
        public Preset SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (SetProperty(ref _selectedPreset, value))
                    _ = LoadSoftwaresForPresetAsync(value);
            }
        }

        private string _deploymentStatus = "Vui lòng chọn một gói Preset để bắt đầu...";
        public string DeploymentStatus { get => _deploymentStatus; set => SetProperty(ref _deploymentStatus, value); }

        private double _deploymentProgress = 0;
        public double DeploymentProgress { get => _deploymentProgress; set => SetProperty(ref _deploymentProgress, value); }

        private bool _isDeploying = false;
        public bool IsDeploying { get => _isDeploying; set => SetProperty(ref _isDeploying, value); }

        // MỚI: Tự động mở vị trí (Explorer) của các tài nguyên đính kèm ngay sau khi cài đặt phần mềm thành công
        private bool _openResourceAfterInstall;
        public bool OpenResourceAfterInstall { get => _openResourceAfterInstall; set => SetProperty(ref _openResourceAfterInstall, value); }

        public ICommand OpenIconFolderCommand { get; }
        public ICommand CreatePresetCommand { get; }
        public ICommand EditPresetCommand { get; }
        public ICommand DeployPresetCommand { get; }
        public ICommand DeletePresetCommand { get; }

        public PresetMainViewModel(
            IDeploymentService deployService,
            ISoftwareRepository softwareRepo,
            IPresetRepository presetRepo,
            IDatabaseConnectionFactory dbFactory,
            ISoftwareResourceRepository resourceMappingRepo)
        {
            _deployService = deployService;
            _softwareRepo = softwareRepo;
            _presetRepo = presetRepo;
            _dbFactory = dbFactory;
            _resourceMappingRepo = resourceMappingRepo;

            OpenIconFolderCommand = new RelayCommand(_ => OpenIconFolder());

            // --- LỆNH XÓA PRESET ---
            DeletePresetCommand = new RelayCommand(async param =>
            {
                if (param is Preset p)
                {
                    var result = MessageBox.Show($"Bạn có chắc chắn muốn xóa gói '{p.Name}' vĩnh viễn không?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                    {
                        using (var connection = _dbFactory.CreateConnection())
                        {
                            // Xóa map phần mềm trước, sau đó xóa Preset
                            await connection.ExecuteAsync("DELETE FROM PresetSoftwareMapping WHERE PresetId = @Id", new { Id = p.Id });
                            await connection.ExecuteAsync("DELETE FROM Presets WHERE Id = @Id", new { Id = p.Id });
                        }
                        _ = LoadDataAsync();
                        SelectedPreset = null;
                    }
                }
            });

            // --- LỆNH TẠO PRESET ---
            CreatePresetCommand = new RelayCommand(async _ =>
            {
                var allSoftwares = await _softwareRepo.GetAllAsync();
                var editorVM = new PresetEditorViewModel(null, allSoftwares.ToList());
                var modal = new Views.PresetEditorWindow { DataContext = editorVM };

                if (modal.ShowDialog() == true)
                {
                    var newPreset = new Preset
                    {
                        Name = editorVM.PresetName,
                        IconPath = editorVM.PresetIconPath,
                        TotalSize = "Chưa tính"
                    };

                    var softwareVersionSelections = editorVM.SelectedSoftwares
                        .ToDictionary(s => s.Id, s => (int?)s.SelectedVersion?.Id);
                    await _presetRepo.AddPresetAsync(newPreset, softwareVersionSelections);
                    _ = LoadDataAsync();
                    MessageBox.Show($"Đã lưu gói {editorVM.PresetName} vào cơ sở dữ liệu thành công!", "Thành công");
                }
            });

            // --- LỆNH SỬA PRESET (ĐÃ FIX LỖI COMBOBOX TRỐNG) ---
            EditPresetCommand = new RelayCommand(async _ =>
            {
                // Lấy toàn bộ phần mềm (bao gồm cả danh sách Versions nhờ câu lệnh JOIN ở Repository)
                var allSoftwares = (await _softwareRepo.GetAllAsync()).ToList();

                // Đồng bộ lại phần mềm trong Preset với kho allSoftwares để kéo theo danh sách Versions
                if (SelectedPreset?.Softwares != null)
                {
                    var syncedSoftwares = new List<Software>();
                    foreach (var oldSw in SelectedPreset.Softwares)
                    {
                        var fullSw = allSoftwares.FirstOrDefault(x => x.Id == oldSw.Id);
                        if (fullSw != null)
                        {
                            // SỬA BUG: fullSw là object hoàn toàn mới (SelectedVersion = null) vì lấy lại từ
                            // ISoftwareRepository. Nếu không khớp lại đây, phiên bản đã lưu thật sự trong DB
                            // (oldSw.SelectedVersion) sẽ bị bỏ mất -> ComboBox hiện trống, buộc chọn lại thủ công.
                            if (oldSw.SelectedVersion != null && fullSw.Versions != null)
                            {
                                fullSw.SelectedVersion = fullSw.Versions.FirstOrDefault(v => v.Id == oldSw.SelectedVersion.Id);
                            }
                            syncedSoftwares.Add(fullSw);
                        }
                    }
                    SelectedPreset.Softwares = syncedSoftwares;
                }

                var editorVM = new PresetEditorViewModel(SelectedPreset, allSoftwares);
                var modal = new Views.PresetEditorWindow { DataContext = editorVM };

                if (modal.ShowDialog() == true)
                {
                    await UpdatePresetInDatabaseAsync(editorVM);
                    MessageBox.Show($"Đã cập nhật gói {editorVM.PresetName} thành công!", "Thành công");
                }
            }, _ => SelectedPreset != null);

            DeployPresetCommand = new RelayCommand(async _ => await ExecuteDeployPresetAsync(),
                _ => SelectedPreset != null && SelectedPresetSoftwares.Any() && !IsDeploying);

            _ = LoadDataAsync();
        }

        private async Task UpdatePresetInDatabaseAsync(PresetEditorViewModel editor)
        {
            var updatedPreset = new Preset { Id = editor.PresetId, Name = editor.PresetName, IconPath = editor.PresetIconPath };
            var softwareVersionSelections = editor.SelectedSoftwares
                .ToDictionary(s => s.Id, s => (int?)s.SelectedVersion?.Id);

            await _presetRepo.UpdatePresetAsync(updatedPreset, softwareVersionSelections);
            _ = LoadDataAsync();
        }

        private async Task LoadSoftwaresForPresetAsync(Preset preset)
        {
            SelectedPresetSoftwares.Clear();
            if (preset == null || preset.Softwares == null) return;

            foreach (var sw in preset.Softwares)
            {
                // MỚI: Nạp danh sách tài nguyên đã đính kèm để hiện icon báo hiệu và phục vụ chức năng
                // "Mở tài nguyên đính kèm khi cài xong"
                var attachedResources = await _resourceMappingRepo.GetResourcesForSoftwareAsync(sw.Id);
                sw.AttachedResources = new ObservableCollection<ResourceFile>(attachedResources);

                SelectedPresetSoftwares.Add(sw);
            }

            DeploymentStatus = $"Sẵn sàng cài đặt gói: {preset.Name} ({preset.Softwares.Count} phần mềm)";
        }

        private async Task ExecuteDeployPresetAsync()
        {
            IsDeploying = true;
            int total = SelectedPresetSoftwares.Count;
            int current = 0;
            DeploymentProgress = 0;

            foreach (var sw in SelectedPresetSoftwares)
            {
                current++;
                DeploymentStatus = $"Đang cài đặt {sw.Name} ({current}/{total})...";

                try
                {
                    // Lấy đường dẫn file: Ưu tiên phiên bản người dùng chọn, nếu không có thì lấy mặc định
                    string filePath = sw.SelectedVersion?.FilePath ?? sw.FilePath ?? "";
                    string arguments = sw.SilentInstallCommand ?? "";

                    bool isSuccess = await _deployService.RunSilentInstallAsync(filePath, arguments, (percent) =>
                    {
                        double baseProgress = (current - 1) * 100.0 / total;
                        double currentAppProgress = percent / (double)total;
                        Application.Current.Dispatcher.Invoke(() => { DeploymentProgress = baseProgress + currentAppProgress; });
                    });

                    if (!isSuccess)
                    {
                        for (int i = 0; i <= 100; i += 10)
                        {
                            await Task.Delay(200);
                            double baseProgress = (current - 1) * 100.0 / total;
                            double currentAppProgress = i / (double)total;
                            Application.Current.Dispatcher.Invoke(() => { DeploymentProgress = baseProgress + currentAppProgress; });
                        }
                    }
                    // MỚI: Mở vị trí tài nguyên đính kèm (nếu người dùng bật tùy chọn) khi cài đặt thành công
                    else if (OpenResourceAfterInstall && sw.AttachedResources?.Count > 0)
                    {
                        OpenAllResourceLocations(sw);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi cài đặt {sw.Name}: {ex.Message}", "Lỗi Cài Đặt", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            DeploymentProgress = 100;
            DeploymentStatus = $"Tuyệt vời! Đã cài đặt hoàn tất gói {SelectedPreset.Name}.";
            IsDeploying = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        private void OpenIconFolder()
        {
            string iconDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "PresetIcons");
            if (!Directory.Exists(iconDir)) Directory.CreateDirectory(iconDir);
            Process.Start("explorer.exe", iconDir);
        }

        // MỚI: Mở Explorer tới vị trí (và bôi đen sẵn) TOÀN BỘ tài nguyên đính kèm của 1 phần mềm,
        // dùng ngay sau khi cài đặt thành công nếu OpenResourceAfterInstall = true.
        // Hành vi giống hệt nút "Mở vị trí tài nguyên" bên LibraryView / DeploymentViewModel.
        private void OpenAllResourceLocations(Software software)
        {
            if (software?.AttachedResources == null || software.AttachedResources.Count == 0) return;

            foreach (var resource in software.AttachedResources)
            {
                try
                {
                    string absoluteFolderPath = GetAbsoluteResourceFolderPath(resource.FolderPath);
                    string fullPath = Path.Combine(absoluteFolderPath ?? string.Empty, resource.FileName ?? string.Empty);

                    if (File.Exists(fullPath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                    }
                    else if (!string.IsNullOrEmpty(absoluteFolderPath) && Directory.Exists(absoluteFolderPath))
                    {
                        Process.Start("explorer.exe", absoluteFolderPath);
                    }
                }
                catch { /* Bỏ qua lỗi mở vị trí để không làm gián đoạn chu trình cài đặt cả gói */ }
            }
        }

        // MỚI: Tự phục hồi đường dẫn thư mục tài nguyên theo BaseDirectory hiện tại của máy đang chạy
        private string GetAbsoluteResourceFolderPath(string savedPath)
        {
            if (string.IsNullOrWhiteSpace(savedPath)) return null;

            string folderName = new DirectoryInfo(savedPath).Name;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library", "Resources", folderName);
        }

        private async Task LoadDataAsync()
        {
            try
            {
                var data = await _presetRepo.GetAllPresetsAsync();
                Presets.Clear();
                foreach (var p in data) Presets.Add(p);
            }
            catch (Exception ex)
            {
                DeploymentStatus = $"Lỗi tải dữ liệu Preset: {ex.Message}";
            }
        }
    }
}