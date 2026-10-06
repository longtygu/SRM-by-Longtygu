using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services;
using System.ComponentModel;
using System.Windows.Data;

namespace SRM_by_Longtygu.ViewModels
{
    public class LibraryViewModel : ViewModelBase
    {
        private readonly ISoftwareRepository _repository;
        private readonly IDialogService _dialogService;
        private readonly IFileProcessingService _fileService;
        private readonly ISoftwareVersionRepository _versionRepo;
        private readonly ISoftwareResourceRepository _resourceMappingRepo; // MỚI

        public ObservableCollection<Software> Softwares { get; } = new ObservableCollection<Software>();
        public ICollectionView SoftwareView { get; }

        private string _currentSortColumn = string.Empty;
        private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;
        private Software _selectedSoftware;

        public Software SelectedSoftware
        {
            get => _selectedSoftware;
            set => SetProperty(ref _selectedSoftware, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _ = PerformSearchAsync();
                }
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand AddDummyCommand { get; }
        public ICommand AddMultipleCommand { get; } // MỚI: mở cửa sổ thêm nhiều phần mềm cùng lúc
        public ICommand CleanUpCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand CloseDetailsCommand { get; }
        public ICommand AddVersionCommand { get; }
        public ICommand InstallVersionCommand { get; }
        public ICommand DeleteVersionCommand { get; }
        public ICommand AttachResourceCommand { get; } // MỚI: mở form đính kèm tài nguyên
        public ICommand DetachResourceCommand { get; } // MỚI: gỡ 1 tài nguyên đã đính kèm
        public ICommand OpenResourceCommand { get; } // MỚI: mở trực tiếp file tài nguyên đã đính kèm

        // ĐÃ SỬA: Thêm ISoftwareVersionRepository versionRepo và ISoftwareResourceRepository resourceMappingRepo vào tham số
        public LibraryViewModel(ISoftwareRepository repository, IDialogService dialogService, IFileProcessingService fileService,
                                 ISoftwareVersionRepository versionRepo, ISoftwareResourceRepository resourceMappingRepo)
        {
            _repository = repository;
            _dialogService = dialogService;
            _fileService = fileService;
            _versionRepo = versionRepo;
            _resourceMappingRepo = resourceMappingRepo;

            RefreshCommand = new RelayCommand(_ => _ = LoadDataAsync());
            DeleteCommand = new RelayCommand(ExecuteDelete, CanExecuteDelete);
            AddDummyCommand = new RelayCommand(_ => ExecuteAddSoftware());
            AddMultipleCommand = new RelayCommand(_ => ExecuteAddMultipleSoftware());
            CleanUpCommand = new RelayCommand(_ => _ = ExecuteCleanUpAsync());
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteDelete);
            CloseDetailsCommand = new RelayCommand(_ => SelectedSoftware = null);
            AddVersionCommand = new RelayCommand(ExecuteAddVersion, CanExecuteDelete);
            InstallVersionCommand = new RelayCommand(ExecuteInstallVersion);
            DeleteVersionCommand = new RelayCommand(ExecuteDeleteVersion);
            AttachResourceCommand = new RelayCommand(ExecuteAttachResource, CanExecuteDelete);
            DetachResourceCommand = new RelayCommand(ExecuteDetachResource);
            OpenResourceCommand = new RelayCommand(ExecuteOpenResource);

            _ = LoadDataAsync();
            SoftwareView = CollectionViewSource.GetDefaultView(Softwares);
        }

        // ĐÃ SỬA: Bọc try/catch quanh phần nạp chi tiết của TỪNG phần mềm.
        // Lớp phòng vệ cuối: nếu DB (ví dụ từ 1 backup cũ hiếm gặp) vẫn còn thiếu bảng/cột nào đó
        // mà bước đồng bộ schema (DatabaseSchemaMigrator) không lường tới, thì chỉ phần mềm đó bị
        // thiếu dữ liệu phụ (dung lượng/tài nguyên đính kèm), KHÔNG làm cả DataGrid trắng trơn.
        private async Task LoadDataAsync()
        {
            var data = await _repository.GetAllAsync();
            Application.Current.Dispatcher.Invoke(() => Softwares.Clear());

            foreach (var item in data)
            {
                await TryLoadSoftwareDetailsAsync(item);
                Application.Current.Dispatcher.Invoke(() => Softwares.Add(item));
            }
        }

        private async Task PerformSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                await LoadDataAsync();
                return;
            }

            var data = await _repository.SearchAsync(SearchText);
            Application.Current.Dispatcher.Invoke(() => Softwares.Clear());

            foreach (var item in data)
            {
                await TryLoadSoftwareDetailsAsync(item);
                Application.Current.Dispatcher.Invoke(() => Softwares.Add(item));
            }
        }

        // MỚI: gom logic nạp dung lượng + tài nguyên đính kèm cho 1 phần mềm vào 1 chỗ,
        // có try/catch để lỗi ở 1 phần mềm không làm gãy toàn bộ danh sách.
        private async Task TryLoadSoftwareDetailsAsync(Software item)
        {
            try
            {
                var versions = await _versionRepo.GetBySoftwareIdAsync(item.Id);
                long totalBytes = versions.Sum(v => v.FileSize);
                item.DisplaySize = FormatSize(totalBytes);
                item.Versions = new ObservableCollection<SoftwareVersion>(versions);
            }
            catch (Exception ex)
            {
                item.DisplaySize = "N/A";
                item.Versions = new ObservableCollection<SoftwareVersion>();
                System.Diagnostics.Debug.WriteLine($"[LibraryViewModel] Lỗi nạp phiên bản cho '{item.Name}': {ex.Message}");
            }

            try
            {
                var attachedResources = await _resourceMappingRepo.GetResourcesForSoftwareAsync(item.Id);
                item.AttachedResources = new ObservableCollection<ResourceFile>(attachedResources);
            }
            catch (Exception ex)
            {
                // Thường gặp khi DB là backup cũ chưa có bảng SoftwareResourceMapping/ResourceFiles.
                // DatabaseSchemaMigrator đã lo việc này ở bước khởi động/restore, nhưng vẫn giữ
                // lớp phòng vệ ở đây để không làm mất cả DataGrid nếu có trường hợp phát sinh khác.
                item.AttachedResources = new ObservableCollection<ResourceFile>();
                System.Diagnostics.Debug.WriteLine($"[LibraryViewModel] Lỗi nạp tài nguyên đính kèm cho '{item.Name}': {ex.Message}");
            }
        }

        // CHUYỂN ĐỔI BYTE SANG MB/GB
        private string FormatSize(long bytes)
        {
            if (bytes == 0) return "0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb >= 1024) return $"{(mb / 1024.0):0.##} GB";
            return $"{mb:0.##} MB";
        }

        private bool CanExecuteDelete(object parameter) => SelectedSoftware != null;

        private async void ExecuteDelete(object parameter)
        {
            if (SelectedSoftware != null)
            {
                var result = MessageBox.Show($"Bạn có chắc muốn xóa '{SelectedSoftware.Name}' và XÓA TOÀN BỘ file bộ cài khỏi ổ cứng không?",
                                             "Xác nhận xóa vĩnh viễn", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    await _fileService.DeleteSoftwareFolderAsync(SelectedSoftware.Name);
                    await _repository.DeleteAsync(SelectedSoftware.Id);
                    await LoadDataAsync();
                }
            }
        }

        private void ExecuteAddSoftware()
        {
            _dialogService.ShowAddSoftwareDialog();
            _ = LoadDataAsync();
        }

        // MỚI: mở cửa sổ Thêm nhiều phần mềm cùng lúc, sau đó làm mới danh sách
        private void ExecuteAddMultipleSoftware()
        {
            _dialogService.ShowAddMultipleSoftwareDialog();
            _ = LoadDataAsync();
        }

        private async Task ExecuteCleanUpAsync()
        {
            var result = MessageBox.Show("Hệ thống sẽ quét và xóa toàn bộ các file rác vật lý và dữ liệu rác trong hệ thống. Bạn có muốn tiếp tục?",
                                         "Dọn dẹp bộ nhớ", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                await _repository.CleanUpOrphanedDataAsync();
                var softwares = await _repository.GetAllAsync();
                var validNames = System.Linq.Enumerable.Select(softwares, s => s.Name);
                await _fileService.CleanUpOrphanedFilesAsync(validNames);
                _dialogService.ShowMessage("Dọn dẹp thành công! Dữ liệu và ổ cứng đã được đồng bộ hoàn toàn.", "Hoàn tất");
            }
        }

        public void GroupOrSortBy(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return;

            if (_currentSortColumn == propertyName)
                _currentSortDirection = _currentSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            else
            {
                _currentSortColumn = propertyName;
                _currentSortDirection = ListSortDirection.Ascending;
            }

            SoftwareView.GroupDescriptions.Clear();
            SoftwareView.SortDescriptions.Clear();
            if (propertyName == "Category" || propertyName == "Publisher")
                SoftwareView.GroupDescriptions.Add(new PropertyGroupDescription(propertyName));

            SoftwareView.SortDescriptions.Add(new SortDescription(propertyName, _currentSortDirection));
        }

        private void ExecuteEdit(object parameter)
        {
            if (SelectedSoftware != null)
            {
                _dialogService.ShowEditSoftwareDialog(SelectedSoftware);
                _ = LoadDataAsync();
            }
        }
        private void ExecuteAddVersion(object parameter)
        {
            if (SelectedSoftware != null)
            {
                _dialogService.ShowAddVersionDialog(SelectedSoftware);
                _ = LoadDataAsync(); // Cập nhật lại dung lượng sau khi thêm xong
            }
        }
        private void ExecuteInstallVersion(object parameter)
        {
            if (parameter is SoftwareVersion version)
            {
                try
                {
                    string fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, version.FilePath);
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = fullPath,
                            UseShellExecute = true // Tự động mở file .exe
                        });
                    }
                    else MessageBox.Show("Không tìm thấy file cài đặt trên ổ cứng!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex) { MessageBox.Show($"Lỗi chạy file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private async void ExecuteDeleteVersion(object parameter)
        {
            if (parameter is SoftwareVersion version)
            {
                if (MessageBox.Show($"Bạn có chắc muốn xóa vĩnh viễn phiên bản {version.Version}?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Xóa file vật lý
                        string fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, version.FilePath);
                        if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);

                        // Xóa database
                        await _versionRepo.DeleteAsync(version.Id);

                        // Nếu xóa hết phiên bản, cảnh báo (Tùy chọn)
                        _ = LoadDataAsync();
                    }
                    catch (Exception ex) { MessageBox.Show($"Lỗi xóa: {ex.Message}"); }
                }
            }
        }

        // MỚI: Mở form "Đính kèm tài nguyên" cho phần mềm đang được chọn, sau đó nạp lại danh sách tài nguyên
        private void ExecuteAttachResource(object parameter)
        {
            if (SelectedSoftware != null)
            {
                _dialogService.ShowAttachResourceDialog(SelectedSoftware);
                _ = LoadDataAsync();
            }
        }

        // MỚI: Gỡ 1 tài nguyên đã đính kèm khỏi phần mềm đang chọn (không xóa tài nguyên khỏi Thư viện Tài nguyên, chỉ gỡ liên kết)
        private async void ExecuteDetachResource(object parameter)
        {
            if (parameter is ResourceFile resource && SelectedSoftware != null)
            {
                var result = MessageBox.Show($"Gỡ liên kết tài nguyên '{resource.Name}' khỏi phần mềm '{SelectedSoftware.Name}'?\n(Tài nguyên vẫn được giữ nguyên trong Thư viện Tài nguyên)",
                                             "Xác nhận gỡ đính kèm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        await _resourceMappingRepo.DetachAsync(SelectedSoftware.Id, resource.Id);
                        _ = LoadDataAsync();
                    }
                    catch (Exception ex) { MessageBox.Show($"Lỗi gỡ đính kèm: {ex.Message}"); }
                }
            }
        }

        // MỚI: Mở trực tiếp file tài nguyên đã đính kèm (không chỉ mở thư mục chứa nó)
        // MỚI: Mở Explorer tới đúng vị trí (thư mục chứa) file tài nguyên, đồng thời bôi đen sẵn file đó
        // (không chạy/mở nội dung file, tương tự chức năng "Mở vị trí file" của Windows)
        private void ExecuteOpenResource(object parameter)
        {
            if (parameter is ResourceFile resource)
            {
                try
                {
                    string absoluteFolderPath = GetAbsoluteResourceFolderPath(resource.FolderPath);
                    string fullPath = System.IO.Path.Combine(absoluteFolderPath ?? string.Empty, resource.FileName ?? string.Empty);

                    if (System.IO.File.Exists(fullPath))
                    {
                        // /select, sẽ mở Explorer đúng thư mục và bôi đen sẵn file được chỉ định
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                    }
                    else if (!string.IsNullOrEmpty(absoluteFolderPath) && System.IO.Directory.Exists(absoluteFolderPath))
                    {
                        // Không tìm thấy đúng file nhưng thư mục vẫn còn -> mở thư mục để người dùng tự tìm
                        System.Diagnostics.Process.Start("explorer.exe", absoluteFolderPath);
                    }
                    else
                    {
                        MessageBox.Show($"Không tìm thấy vị trí tài nguyên trên ổ cứng!\nĐường dẫn: {fullPath}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex) { MessageBox.Show($"Lỗi mở vị trí tài nguyên: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        // MỚI: Tự phục hồi đường dẫn thư mục tài nguyên theo vị trí BaseDirectory hiện tại của máy đang chạy
        // (đồng bộ logic với GetAbsoluteFolderPath bên ResourceFileMainViewModel, để dữ liệu Portable luôn đúng khi đổi máy)
        private string GetAbsoluteResourceFolderPath(string savedPath)
        {
            if (string.IsNullOrWhiteSpace(savedPath)) return null;

            string folderName = new System.IO.DirectoryInfo(savedPath).Name;
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library", "Resources", folderName);
        }
    }
}
