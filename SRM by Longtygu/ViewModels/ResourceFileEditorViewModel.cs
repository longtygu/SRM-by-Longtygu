using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.ViewModels
{
    public class ResourceFileEditorViewModel : ViewModelBase
    {
        public int FileId { get; set; }

        private string _name;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _category;
        public string Category { get => _category; set => SetProperty(ref _category, value); }

        private string _fileName;
        public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }

        private string _size;
        public string Size { get => _size; set => SetProperty(ref _size, value); }

        private string _description;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private string _iconPath;
        public string IconPath { get => _iconPath; set => SetProperty(ref _iconPath, value); }

        public string FolderPath { get; set; }

        // Lưu đường dẫn file/thư mục gốc khi người dùng ấn "Chọn File" hoặc "Chọn Thư Mục" để chuẩn bị Copy
        private string _sourceFilePath;
        public string SourceFilePath { get => _sourceFilePath; set => SetProperty(ref _sourceFilePath, value); }

        // ĐÃ THÊM: true nếu SourceFilePath đang trỏ tới 1 THƯ MỤC (nhiều file nhỏ) thay vì 1 file đơn
        private bool _isSourceFolder;
        public bool IsSourceFolder { get => _isSourceFolder; set => SetProperty(ref _isSourceFolder, value); }

        // ĐÃ THÊM: đường dẫn đầy đủ tới file đã lưu từ trước (chỉ có giá trị khi đang Sửa 1 tài nguyên có sẵn),
        // dùng để xem trước icon tự động khi mở form Sửa mà chưa chọn file mới
        public string ExistingFilePath { get; private set; }

        public ObservableCollection<string> Categories { get; set; }
        public ICommand SelectIconCommand { get; }
        public ICommand ResetIconCommand { get; } // ĐÃ THÊM: khôi phục về icon tự động (bỏ icon tự chọn)
        public ICommand SelectFileCommand { get; }
        public ICommand SelectFolderCommand { get; } // ĐÃ THÊM: chọn cả thư mục nhiều file nhỏ

        public ResourceFileEditorViewModel(ResourceFile existingFile = null)
        {
            Categories = new ObservableCollection<string>
            {
                "Windows ISO",
                "Office ISO",
                "System Components (DLL, C++)",
                "Drivers",
                "Scripts & Configs",
                "Công cụ (Tools)",
                "Khác"
            };

            if (existingFile != null)
            {
                FileId = existingFile.Id;
                Name = existingFile.Name;
                Category = existingFile.Category;
                FileName = existingFile.FileName;
                Size = existingFile.Size;
                Description = existingFile.Description;
                IconPath = existingFile.IconPath;
                FolderPath = existingFile.FolderPath;

                ExistingFilePath = !string.IsNullOrEmpty(existingFile.FolderPath) && !string.IsNullOrEmpty(existingFile.FileName)
                    ? Path.Combine(existingFile.FolderPath, existingFile.FileName)
                    : null;
            }
            else
            {
                Category = Categories[0];
            }

            SelectIconCommand = new RelayCommand(_ =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.ico" };
                if (dialog.ShowDialog() == true) IconPath = dialog.FileName;
            });

            ResetIconCommand = new RelayCommand(_ =>
            {
                // Bỏ icon tự chọn -> icon sẽ tự động quay lại dùng icon trích xuất từ file thật
                IconPath = null;
            });

            SelectFileCommand = new RelayCommand(_ =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Chọn file tài nguyên (ISO, DLL, exe...)",
                    Filter = "All Files (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    SourceFilePath = dialog.FileName; // Lưu đường dẫn gốc
                    IsSourceFolder = false;

                    var fileInfo = new FileInfo(dialog.FileName);

                    FileName = fileInfo.Name;
                    Name = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    Size = FormatSize(fileInfo.Length);
                }
            });

            // ĐÃ THÊM: cho phép chọn cả 1 thư mục chứa nhiều file nhỏ (thay vì chỉ 1 file)
            // Dùng Microsoft.Win32.OpenFolderDialog (có sẵn từ .NET 8, native trong WPF) thay vì
            // System.Windows.Forms.FolderBrowserDialog để KHÔNG cần bật UseWindowsForms cho cả project
            // (bật UseWindowsForms sẽ gây xung đột tên lớp - CS0104 - ở toàn bộ solution).
            SelectFolderCommand = new RelayCommand(_ =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Chọn thư mục chứa nhiều file tài nguyên",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    SourceFilePath = dialog.FolderName; // Lưu đường dẫn thư mục gốc
                    IsSourceFolder = true;

                    var dirInfo = new DirectoryInfo(dialog.FolderName);
                    FileName = dirInfo.Name; // Với thư mục, "FileName" đóng vai trò tên thư mục chứa
                    Name = dirInfo.Name;

                    int fileCount = 0;
                    long totalBytes = 0;
                    try
                    {
                        var files = Directory.EnumerateFiles(dialog.FolderName, "*", SearchOption.AllDirectories).ToList();
                        fileCount = files.Count;
                        totalBytes = files.Sum(f => new FileInfo(f).Length);
                    }
                    catch
                    {
                        // Bỏ qua các file/thư mục không truy cập được (VD: bị khóa quyền)
                    }

                    Size = $"{FormatSize(totalBytes)} ({fileCount} file)";
                }
            });
        }

        private static string FormatSize(double bytes)
        {
            if (bytes >= 1073741824.0) return (bytes / 1073741824.0).ToString("0.##") + " GB";
            if (bytes >= 1048576.0) return (bytes / 1048576.0).ToString("0.##") + " MB";
            return (bytes / 1024.0).ToString("0.##") + " KB";
        }
    }
}
