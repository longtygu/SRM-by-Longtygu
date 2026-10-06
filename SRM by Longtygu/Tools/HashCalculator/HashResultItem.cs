using System;
using System.Linq;

namespace SRM_by_Longtygu.Tools.HashCalculator
{
    // Model 1 file trong danh sách kết quả. Toàn bộ giá trị hiển thị (màu/nhãn khớp hash...)
    // được tính sẵn và lưu qua SetProperty (đã có ở ViewModelBase) để UI tự cập nhật khi
    // hash tính xong hoặc khi người dùng gõ vào ô "Hash cần đối chiếu" — không cần IValueConverter.
    public class HashResultItem : ViewModels.ViewModelBase
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string SizeDisplay { get; set; }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set { SetProperty(ref _isProcessing, value); RecomputeMatch(); }
        }

        private int _progressPercent;
        public int ProgressPercent
        {
            get => _progressPercent;
            set => SetProperty(ref _progressPercent, value);
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            private set => SetProperty(ref _hasError, value);
        }

        public void SetError(string message)
        {
            ErrorMessage = message;
            HasError = !string.IsNullOrEmpty(message);
        }

        private string _md5 = "";
        public string Md5 { get => _md5; private set => SetProperty(ref _md5, value); }

        private string _sha1 = "";
        public string Sha1 { get => _sha1; private set => SetProperty(ref _sha1, value); }

        private string _sha256 = "";
        public string Sha256 { get => _sha256; private set => SetProperty(ref _sha256, value); }

        private string _sha512 = "";
        public string Sha512 { get => _sha512; private set => SetProperty(ref _sha512, value); }

        private string _crc32 = "";
        public string Crc32 { get => _crc32; private set => SetProperty(ref _crc32, value); }

        public void SetHashResults(string md5, string sha1, string sha256, string sha512, string crc32)
        {
            Md5 = md5;
            Sha1 = sha1;
            Sha256 = sha256;
            Sha512 = sha512;
            Crc32 = crc32;
            RecomputeMatch();
        }

        // ============ ĐỐI CHIẾU VỚI HASH NGƯỜI DÙNG NHẬP (dùng chung cho cả danh sách qua ViewModel) ============
        private string _compareHash = "";
        public string CompareHash
        {
            get => _compareHash;
            set
            {
                _compareHash = value;
                RecomputeMatch();
            }
        }

        private bool _hasMatchResult;
        public bool HasMatchResult
        {
            get => _hasMatchResult;
            private set => SetProperty(ref _hasMatchResult, value);
        }

        private string _matchLabel = "";
        public string MatchLabel { get => _matchLabel; private set => SetProperty(ref _matchLabel, value); }

        private string _matchColor = "#909090";
        public string MatchColor { get => _matchColor; private set => SetProperty(ref _matchColor, value); }

        private string _matchChipBackground = "#1A909090";
        public string MatchChipBackground { get => _matchChipBackground; private set => SetProperty(ref _matchChipBackground, value); }

        private string _matchIconKind = "HelpCircleOutline";
        public string MatchIconKind { get => _matchIconKind; private set => SetProperty(ref _matchIconKind, value); }

        private void RecomputeMatch()
        {
            var target = (_compareHash ?? "").Trim();

            if (string.IsNullOrEmpty(target))
            {
                HasMatchResult = false;
                return;
            }

            if (IsProcessing)
            {
                HasMatchResult = true;
                MatchLabel = "Đang tính...";
                MatchColor = "#909090";
                MatchChipBackground = "#1A909090";
                MatchIconKind = "TimerSandEmpty";
                return;
            }

            var matched = new[] { Md5, Sha1, Sha256, Sha512, Crc32 }
                .Any(h => !string.IsNullOrWhiteSpace(h) && h.Equals(target, StringComparison.OrdinalIgnoreCase));

            HasMatchResult = true;
            if (matched)
            {
                MatchLabel = "Khớp";
                MatchColor = "#6CCB5F";
                MatchChipBackground = "#1A6CCB5F";
                MatchIconKind = "CheckCircleOutline";
            }
            else
            {
                MatchLabel = "Không khớp";
                MatchColor = "#E06C75";
                MatchChipBackground = "#1AE06C75";
                MatchIconKind = "CloseCircleOutline";
            }
        }
    }
}
