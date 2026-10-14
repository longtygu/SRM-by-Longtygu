using System;
using System.ComponentModel;

namespace SRM_by_Longtygu.Services
{
    /// <summary>
    /// Cầu nối để XAML liên kết (Binding) chuỗi đa ngôn ngữ vào những thuộc tính KHÔNG thuộc FrameworkElement,
    /// ví dụ tiêu đề cột của DataGrid (DynamicResource không tự cập nhật ở đó).
    /// Cách dùng trong XAML:
    ///   xmlns:srmloc="clr-namespace:SRM_by_Longtygu.Services"
    ///   Header="{Binding [Khoa], Source={x:Static srmloc:LocProxy.Instance}}"
    /// Khi đổi ngôn ngữ, chỉ mục này phát "Item[]" để mọi liên kết tự lấy lại chuỗi mới.
    /// </summary>
    public sealed class LocProxy : INotifyPropertyChanged
    {
        public static LocProxy Instance { get; } = new LocProxy();

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _hooked;

        private LocProxy()
        {
        }

        public string this[string key]
        {
            get
            {
                Hook();
                return Loc.T(key);
            }
        }

        // Đăng ký sự kiện đổi ngôn ngữ ở lần đọc đầu tiên (lúc này dịch vụ ngôn ngữ chắc chắn đã được tạo)
        private void Hook()
        {
            if (_hooked) return;
            var localization = LocalizationService.Instance;
            if (localization == null) return;

            _hooked = true;
            localization.LanguageChanged += (s, e) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
}
