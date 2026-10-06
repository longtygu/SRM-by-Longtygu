using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace SRM_by_Longtygu.ViewModels
{
    public class ResourceFileMainViewModel : ViewModelBase
    {
        private readonly IResourceFileRepository _repository;

        public ObservableCollection<ResourceFile> ResourceFiles { get; set; } = new ObservableCollection<ResourceFile>();

        public ICollectionView ResourceFilesView { get; }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ResourceFilesView.Refresh();
                }
            }
        }

        private ResourceFile _selectedResource;
        public ResourceFile SelectedResource { get => _selectedResource; set => SetProperty(ref _selectedResource, value); }

        public ICommand OpenFolderCommand { get; }
        public ICommand AddResourceCommand { get; }
        public ICommand EditResourceCommand { get; }
        public ICommand DeleteResourceCommand { get; }

        public ResourceFileMainViewModel(IResourceFileRepository repository)
        {
            _repository = repository;

            ResourceFilesView = CollectionViewSource.GetDefaultView(ResourceFiles);
            ResourceFilesView.Filter = FilterResource;

            OpenFolderCommand = new RelayCommand(folderPath =>
            {
                if (folderPath is string path)
                {
                    // Lớp bảo vệ thứ 2: Đảm bảo đường dẫn mở luôn khớp với vị trí app hiện tại
                    string validPath = GetAbsoluteFolderPath(path);

                    if (!string.IsNullOrEmpty(validPath) && Directory.Exists(validPath))
                        Process.Start("explorer.exe", validPath);
                    else
                        MessageBox.Show($"Không tìm thấy thư mục cục bộ của file này!\nĐường dẫn tìm kiếm: {validPath}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });

            DeleteResourceCommand = new RelayCommand(param =>
            {
                if (param is ResourceFile file)
                {
                    var result = MessageBox.Show($"Bạn có chắc chắn muốn xóa '{file.Name}'?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                    {
                        _repository.Delete(file.Id);
                        ResourceFiles.Remove(file);

                        if (SelectedResource == file)
                            SelectedResource = null;
                    }
                }
            });

            AddResourceCommand = new RelayCommand(async _ =>
            {
                var editorVM = new ResourceFileEditorViewModel(null);
                var modal = new Views.ResourceFileEditorWindow { DataContext = editorVM };

                var mainWin = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (mainWin != null)
                {
                    modal.Owner = mainWin;
                }

                if (modal.ShowDialog() == true)
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    try
                    {
                        string localFolderPath = await CopySourceToLibraryAsync(editorVM, existingFolderPath: null);

                        var newFile = new ResourceFile
                        {
                            Name = editorVM.Name,
                            Category = editorVM.Category,
                            FileName = editorVM.FileName,
                            Size = editorVM.Size,
                            Description = editorVM.Description,
                            IconPath = editorVM.IconPath,
                            FolderPath = localFolderPath,
                            AddedDate = DateTime.Now
                        };

                        _repository.Add(newFile);
                        LoadData();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi: " + ex.Message, "Lỗi Copy", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                    }
                }
            });

            EditResourceCommand = new RelayCommand(async param =>
            {
                if (param is ResourceFile file)
                {
                    var editorVM = new ResourceFileEditorViewModel(file);
                    var modal = new Views.ResourceFileEditorWindow { DataContext = editorVM };

                    var mainWin = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                    if (mainWin != null)
                    {
                        modal.Owner = mainWin;
                    }

                    if (modal.ShowDialog() == true)
                    {
                        Mouse.OverrideCursor = Cursors.Wait;
                        try
                        {
                            string folderPath = await CopySourceToLibraryAsync(editorVM, existingFolderPath: file.FolderPath);

                            file.Name = editorVM.Name;
                            file.Category = editorVM.Category;
                            file.FileName = editorVM.FileName;
                            file.Size = editorVM.Size;
                            file.Description = editorVM.Description;
                            file.IconPath = editorVM.IconPath;
                            file.FolderPath = folderPath;

                            _repository.Update(file);
                            LoadData();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Lỗi: " + ex.Message, "Lỗi Copy", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            Mouse.OverrideCursor = null;
                        }
                    }
                }
            });

            LoadData();
        }

        private async Task<string> CopySourceToLibraryAsync(ResourceFileEditorViewModel editorVM, string existingFolderPath)
        {
            if (string.IsNullOrEmpty(editorVM.SourceFilePath))
                return existingFolderPath ?? "";

            string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library", "Resources");
            string baseNameForFolder = editorVM.IsSourceFolder
                ? new DirectoryInfo(editorVM.SourceFilePath).Name
                : Path.GetFileNameWithoutExtension(editorVM.FileName);

            string uniqueFolder = Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + baseNameForFolder;
            foreach (char c in Path.GetInvalidFileNameChars()) uniqueFolder = uniqueFolder.Replace(c, '_');

            string localFolderPath = Path.Combine(baseDir, uniqueFolder);
            Directory.CreateDirectory(localFolderPath);

            if (editorVM.IsSourceFolder)
            {
                if (!Directory.Exists(editorVM.SourceFilePath))
                    throw new DirectoryNotFoundException("Không tìm thấy thư mục nguồn: " + editorVM.SourceFilePath);

                await Task.Run(() => CopyDirectoryRecursive(editorVM.SourceFilePath, localFolderPath));
            }
            else
            {
                if (!File.Exists(editorVM.SourceFilePath))
                    throw new FileNotFoundException("Không tìm thấy file nguồn: " + editorVM.SourceFilePath);

                string destFile = Path.Combine(localFolderPath, editorVM.FileName);
                await Task.Run(() => File.Copy(editorVM.SourceFilePath, destFile, true));
            }

            return localFolderPath;
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(filePath));
                File.Copy(filePath, destFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSubDir);
            }
        }

        private bool FilterResource(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (!(obj is ResourceFile file)) return false;

            string keyword = SearchText.Trim();

            return Contains(file.Name, keyword)
                || Contains(file.FileName, keyword)
                || Contains(file.Category, keyword)
                || Contains(file.Description, keyword);
        }

        private static bool Contains(string source, string keyword)
        {
            if (string.IsNullOrEmpty(source)) return false;
            return source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Hàm helper để chuẩn hóa lại đường dẫn động theo máy tính hiện tại
        /// </summary>
        private string GetAbsoluteFolderPath(string savedPath)
        {
            if (string.IsNullOrWhiteSpace(savedPath)) return null;

            string folderName = new DirectoryInfo(savedPath).Name;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library", "Resources", folderName);
        }

        private void LoadData()
        {
            int? previousSelectedId = SelectedResource?.Id;

            ResourceFiles.Clear();
            var dataFromDb = _repository.GetAll();

            foreach (var item in dataFromDb)
            {
                // [Self-Healing] Tự động cập nhật đường dẫn chính xác cho phần mềm Portable.
                // Khi đem sang máy tính khác, BaseDirectory thay đổi nhưng DataGrid vẫn lấy dc đường dẫn mới nhất.
                if (!string.IsNullOrWhiteSpace(item.FolderPath))
                {
                    item.FolderPath = GetAbsoluteFolderPath(item.FolderPath);
                }

                ResourceFiles.Add(item);
            }

            SelectedResource = previousSelectedId.HasValue
                ? ResourceFiles.FirstOrDefault(f => f.Id == previousSelectedId.Value)
                : null;
        }
    }
}