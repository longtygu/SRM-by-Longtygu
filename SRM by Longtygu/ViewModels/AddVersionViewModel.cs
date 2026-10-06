using System;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;

namespace SRM_by_Longtygu.ViewModels
{
    public class AddVersionViewModel : ViewModelBase
    {
        private readonly ISoftwareVersionRepository _versionRepo;
        private readonly Software _parentSoftware;
        public Action CloseWindow { get; set; }

        // Dữ liệu đọc từ phần mềm mẹ (Chỉ hiển thị, không sửa)
        public string SoftwareName => _parentSoftware.Name;
        public string Publisher => _parentSoftware.Publisher;

        // Các trường nhập liệu
        private string _version; public string Version { get => _version; set => SetProperty(ref _version, value); }
        private string _filePath; public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }
        private bool _isPortable; public bool IsPortable { get => _isPortable; set => SetProperty(ref _isPortable, value); }
        private bool _isSaving; public bool IsSaving { get => _isSaving; set => SetProperty(ref _isSaving, value); }

        public ICommand SelectFileCommand { get; }
        public ICommand SaveCommand { get; }

        // Nhận phần mềm mẹ từ ngoài truyền vào
        public AddVersionViewModel(ISoftwareVersionRepository versionRepo, Software parentSoftware)
        {
            _versionRepo = versionRepo;
            _parentSoftware = parentSoftware;

            SelectFileCommand = new RelayCommand(_ => SelectFile());
            SaveCommand = new RelayCommand(async _ => await ExecuteSaveAsync(), _ => !IsSaving);
        }

        private void SelectFile()
        {
            var dialog = new OpenFileDialog { Filter = "Tệp cài đặt (*.exe;*.msi;*.zip)|*.exe;*.msi;*.zip|Tất cả các tệp (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                FilePath = dialog.FileName;

                // Tự bóc tách version từ file
                string fileNameOnly = Path.GetFileNameWithoutExtension(FilePath);
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(FilePath);
                    string extractedVer = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
                    if (string.IsNullOrWhiteSpace(extractedVer) || extractedVer.Trim() == "1.0" || extractedVer.Trim() == "0.0.0.0")
                    {
                        var match = Regex.Match(fileNameOnly, @"\d+(\.\d+)+");
                        if (match.Success) extractedVer = match.Value;
                    }
                    Version = !string.IsNullOrWhiteSpace(extractedVer) ? extractedVer : "1.0";
                }
                catch
                {
                    var match = Regex.Match(fileNameOnly, @"\d+(\.\d+)+");
                    if (match.Success) Version = match.Value;
                }
            }
        }

        private async Task ExecuteSaveAsync()
        {
            if (string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(FilePath))
            {
                MessageBox.Show("Vui lòng kiểm tra lại Phiên bản và File cài đặt!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsSaving = true;

            try
            {
                string captureVersion = Version.Trim();
                string captureFile = FilePath;
                bool captureIsPortable = IsPortable;
                int parentId = _parentSoftware.Id;

                await Task.Run(async () =>
                {
                    string installersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Installers");
                    if (!Directory.Exists(installersDir)) Directory.CreateDirectory(installersDir);

                    string fileExt = Path.GetExtension(captureFile);
                    string newFileName = $"{Guid.NewGuid()}{fileExt}";
                    string destFilePath = Path.Combine(installersDir, newFileName);

                    File.Copy(captureFile, destFilePath, true);

                    var fileInfo = new FileInfo(destFilePath);
                    string hash = CalculateSHA256(destFilePath);

                    // Lưu thẳng vào bản con, không cần check DB mẹ
                    await _versionRepo.InsertAsync(new SoftwareVersion
                    {
                        SoftwareId = parentId,
                        Version = captureVersion,
                        FilePath = Path.Combine("Data", "Installers", newFileName),
                        FileSize = fileInfo.Length,
                        SHA256 = hash,
                        IsPortable = captureIsPortable
                    });
                });

                Application.Current.Dispatcher.Invoke(() => {
                    CloseWindow?.Invoke();
                    MessageBox.Show("Đã thêm phiên bản mới thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => {
                    IsSaving = false;
                    MessageBox.Show($"Lỗi xử lý: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private string CalculateSHA256(string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
            }
        }
    }
}