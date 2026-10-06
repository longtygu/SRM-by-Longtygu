using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SRM_by_Longtygu.Commands;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services.UpdateChecking;

namespace SRM_by_Longtygu.ViewModels
{
    // Lựa chọn hiển thị trong ComboBox loại nguồn
    public class UpdateSourceOption
    {
        public string Key { get; set; }      // giá trị thật lưu vào DB ("None" / "GitHubReleases" / "RegexPage" / "Winget")
        public string DisplayName { get; set; }
        public override string ToString() => DisplayName;
    }

    public class ConfigureUpdateSourceViewModel : ViewModelBase
    {
        private readonly ISoftwareRepository _repository;
        private readonly Software _software;

        public event Action CloseWindow;

        public string SoftwareName => _software.Name;

        public ObservableCollection<UpdateSourceOption> SourceOptions { get; } = new ObservableCollection<UpdateSourceOption>
        {
            new UpdateSourceOption { Key = "None", DisplayName = "Không kiểm tra cập nhật" },
            new UpdateSourceOption { Key = "Winget", DisplayName = "Winget (khuyên dùng - phủ hầu hết app phổ biến, không cần tự tra URL)" },
            new UpdateSourceOption { Key = "GitHubReleases", DisplayName = "GitHub Releases (dành cho phần mềm mã nguồn mở)" },
            new UpdateSourceOption { Key = "RegexPage", DisplayName = "Quét trang web + Regex (dùng khi 2 cách trên không có)" },
        };

        private UpdateSourceOption _selectedSourceOption;
        public UpdateSourceOption SelectedSourceOption
        {
            get => _selectedSourceOption;
            set
            {
                if (SetProperty(ref _selectedSourceOption, value))
                {
                    OnPropertyChanged(nameof(IsGitHubMode));
                    OnPropertyChanged(nameof(IsRegexMode));
                    OnPropertyChanged(nameof(IsWingetMode));
                }
            }
        }

        public bool IsGitHubMode => SelectedSourceOption?.Key == "GitHubReleases";
        public bool IsRegexMode => SelectedSourceOption?.Key == "RegexPage";
        public bool IsWingetMode => SelectedSourceOption?.Key == "Winget";

        private string _updateSourceUrl;
        public string UpdateSourceUrl { get => _updateSourceUrl; set => SetProperty(ref _updateSourceUrl, value); }

        private string _updateVersionPattern;
        public string UpdateVersionPattern { get => _updateVersionPattern; set => SetProperty(ref _updateVersionPattern, value); }

        private string _updateDownloadPattern;
        public string UpdateDownloadPattern { get => _updateDownloadPattern; set => SetProperty(ref _updateDownloadPattern, value); }

        private bool _isSaving;
        public bool IsSaving { get => _isSaving; set => SetProperty(ref _isSaving, value); }

        // MỚI: Thông báo kết quả tự động gợi ý từ catalog, hiển thị cho người dùng biết đã tự điền hay chưa
        private string _suggestionMessage;
        public string SuggestionMessage { get => _suggestionMessage; set => SetProperty(ref _suggestionMessage, value); }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AutoSuggestCommand { get; } // MỚI: dò lại catalog theo yêu cầu (nút bấm tay)

        public ConfigureUpdateSourceViewModel(ISoftwareRepository repository, Software software)
        {
            _repository = repository;
            _software = software;

            UpdateSourceUrl = software.UpdateSourceUrl;
            UpdateVersionPattern = software.UpdateVersionPattern;
            UpdateDownloadPattern = software.UpdateDownloadPattern;

            string currentKey = string.IsNullOrWhiteSpace(software.UpdateSourceType) ? "None" : software.UpdateSourceType;
            SelectedSourceOption = SourceOptions.Count > 0
                ? System.Linq.Enumerable.FirstOrDefault(SourceOptions, o => o.Key == currentKey) ?? SourceOptions[0]
                : null;

            SaveCommand = new RelayCommand(_ => _ = ExecuteSaveAsync());
            CancelCommand = new RelayCommand(_ => CloseWindow?.Invoke());
            AutoSuggestCommand = new RelayCommand(_ => ExecuteAutoSuggest());

            // MỚI: Nếu phần mềm CHƯA từng được cấu hình (còn "None"), tự động dò catalog ngay khi mở dialog,
            // giúp người dùng không phải tự bấm nếu trùng tên với app phổ biến đã có sẵn cấu hình.
            if (currentKey == "None")
            {
                ExecuteAutoSuggest();
            }
        }

        // MỚI: Dò tên phần mềm trong KnownAppsCatalog, tự điền loại nguồn + URL/ID nếu tìm thấy,
        // và TỰ ĐỘNG LƯU NGAY (không bắt người dùng phải bấm Lưu) - dialog vẫn giữ mở để xem lại/chỉnh.
        private async void ExecuteAutoSuggest()
        {
            if (KnownAppsCatalog.TryFindMatch(_software.Name, out var match))
            {
                var matchedOption = System.Linq.Enumerable.FirstOrDefault(SourceOptions, o => o.Key == match.UpdateSourceType);
                if (matchedOption != null)
                {
                    SelectedSourceOption = matchedOption;
                    UpdateSourceUrl = match.UpdateSourceUrl;

                    bool saved = await TrySaveSilentlyAsync();
                    SuggestionMessage = saved
                        ? $"✓ Đã tự động tìm và LƯU cấu hình gợi ý cho '{_software.Name}'. Có thể chỉnh lại rồi bấm Lưu nếu muốn đổi."
                        : $"Đã tìm thấy gợi ý cho '{_software.Name}' nhưng lưu tự động thất bại — hãy tự bấm Lưu bên dưới.";
                    return;
                }
            }

            SuggestionMessage = $"Không tìm thấy cấu hình gợi ý sẵn cho '{_software.Name}'. Hãy tự chọn nguồn và cấu hình bên dưới.";
        }

        // Lưu ngầm (không đóng dialog, không hiện thông báo lỗi popup) - dùng riêng cho luồng tự động gợi ý
        private async Task<bool> TrySaveSilentlyAsync()
        {
            try
            {
                _software.UpdateSourceType = SelectedSourceOption.Key;
                _software.UpdateSourceUrl = UpdateSourceUrl;
                _software.UpdateVersionPattern = null;
                _software.UpdateDownloadPattern = null;
                await _repository.UpdateUpdateSourceConfigAsync(_software);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task ExecuteSaveAsync()
        {
            if (SelectedSourceOption == null) return;

            if (IsGitHubMode && string.IsNullOrWhiteSpace(UpdateSourceUrl))
            {
                MessageBox.Show("Vui lòng nhập 'owner/repo' của GitHub (VD: videolan/vlc).", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (IsWingetMode && string.IsNullOrWhiteSpace(UpdateSourceUrl))
            {
                MessageBox.Show("Vui lòng nhập ID gói Winget (VD: VideoLAN.VLC). Chạy 'winget search <tên>' trong CMD để tra đúng ID.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (IsRegexMode && (string.IsNullOrWhiteSpace(UpdateSourceUrl) || string.IsNullOrWhiteSpace(UpdateVersionPattern)))
            {
                MessageBox.Show("Vui lòng nhập URL trang cần quét và mẫu regex lấy phiên bản.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsSaving = true;
            try
            {
                _software.UpdateSourceType = SelectedSourceOption.Key;
                _software.UpdateSourceUrl = (IsGitHubMode || IsRegexMode || IsWingetMode) ? UpdateSourceUrl : null;
                _software.UpdateVersionPattern = IsRegexMode ? UpdateVersionPattern : null;
                _software.UpdateDownloadPattern = IsRegexMode ? UpdateDownloadPattern : null;

                await _repository.UpdateUpdateSourceConfigAsync(_software);
                CloseWindow?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi lưu cấu hình: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
            }
        }
    }
}
