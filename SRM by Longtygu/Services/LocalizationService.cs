using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using SRM_by_Longtygu.Helpers;

namespace SRM_by_Longtygu.Services
{
    /// <summary>
    /// Một ngôn ngữ giao diện được hỗ trợ (hiện trong ComboBox ở trang Cài đặt).
    /// NativeName là tên viết bằng chính ngôn ngữ đó và KHÔNG được dịch (ví dụ "Tiếng Việt", "English").
    /// </summary>
    public sealed class LanguageOption
    {
        public LanguageOption(string code, string nativeName)
        {
            Code = code;
            NativeName = nativeName;
            try
            {
                Culture = CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
                Culture = CultureInfo.InvariantCulture;
            }
        }

        /// <summary>Mã ngôn ngữ, trùng tên file trong Resources/Langs (ví dụ "vi-VN" -> Resources/Langs/vi-VN.xaml).</summary>
        public string Code { get; }

        /// <summary>Tên hiển thị bằng chính ngôn ngữ đó.</summary>
        public string NativeName { get; }

        /// <summary>Culture dùng để ĐỊNH DẠNG số/ngày khi hiển thị (không đổi culture của thread).</summary>
        public CultureInfo Culture { get; }

        public override string ToString() => NativeName;
    }

    public interface ILocalizationService
    {
        /// <summary>Danh sách ngôn ngữ app hỗ trợ (để đổ vào ComboBox).</summary>
        IReadOnlyList<LanguageOption> SupportedLanguages { get; }

        /// <summary>Mã ngôn ngữ đang dùng (ví dụ "vi-VN").</summary>
        string CurrentLanguage { get; }

        /// <summary>Culture của ngôn ngữ đang dùng, dùng để định dạng số/ngày hiển thị.</summary>
        CultureInfo Culture { get; }

        /// <summary>Phát (trên UI thread) sau khi đổi ngôn ngữ xong, để ViewModel làm mới các chuỗi đã tính sẵn.</summary>
        event EventHandler? LanguageChanged;

        /// <summary>Đổi ngôn ngữ ngay (không cần khởi động lại). persist = true thì ghi vào settings.json.</summary>
        void SetLanguage(string cultureCode, bool persist = true);

        /// <summary>Chuyển sang ngôn ngữ kế tiếp trong danh sách.</summary>
        void ToggleLanguage();

        /// <summary>Lấy chuỗi theo khóa. Thiếu khóa ở ngôn ngữ đang chọn thì dùng bản en-US; thiếu cả hai thì trả về chính tên khóa.</summary>
        string GetString(string key);

        /// <summary>Lấy chuỗi theo khóa rồi chèn tham số {0}, {1}... theo Culture của ngôn ngữ đang chọn.</summary>
        string Format(string key, params object[] args);
    }

    public class LocalizationService : ILocalizationService
    {
        /// <summary>Ngôn ngữ nền: luôn được nạp trước, ngôn ngữ đang chọn phủ lên trên (khóa thiếu sẽ rơi về bản này).</summary>
        public const string BaseLanguage = "en-US";

        /// <summary>Ngôn ngữ mặc định khi settings.json trống hoặc có mã không hỗ trợ.</summary>
        public const string DefaultLanguage = "vi-VN";

        private const string LangFolderMarker = "Resources/Langs/";

        // THÊM NGÔN NGỮ MỚI: (1) tạo file Resources/Langs/<mã>.xaml (sao chép en-US.xaml rồi dịch),
        // (2) thêm một dòng vào danh sách này, (3) chạy Check-LangKeys.ps1 để kiểm tra thiếu khóa.
        private static readonly IReadOnlyList<LanguageOption> Supported = new List<LanguageOption>
        {
            new LanguageOption("vi-VN", "Tiếng Việt"),
            new LanguageOption("en-US", "English"),
        };

        private static bool _windowHandlerRegistered;

        /// <summary>Instance đang dùng của ứng dụng (đặt khi khởi tạo); cho lớp Loc ở dưới truy cập nhanh.</summary>
        public static ILocalizationService? Instance { get; private set; }

        // Bản sao chuỗi (gộp en-US + ngôn ngữ đang chọn) để đọc an toàn từ mọi luồng, kể cả luồng nền.
        private volatile Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);
        private volatile LanguageOption _current;

        public event EventHandler? LanguageChanged;

        public LocalizationService()
        {
            _current = Resolve(DefaultLanguage);
            Instance = this;
        }

        public IReadOnlyList<LanguageOption> SupportedLanguages => Supported;

        public string CurrentLanguage => _current.Code;

        public CultureInfo Culture => _current.Culture;

        public void SetLanguage(string cultureCode, bool persist = true)
        {
            var app = Application.Current;
            if (app == null) return;

            // Từ điển tài nguyên chỉ được đổi trên UI thread
            if (!app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.Invoke(() => SetLanguage(cultureCode, persist));
                return;
            }

            var target = Resolve(cultureCode);
            bool isBase = string.Equals(target.Code, BaseLanguage, StringComparison.OrdinalIgnoreCase);

            var baseDict = LoadDictionary(BaseLanguage);
            ResourceDictionary? selectedDict = null;
            if (!isBase)
            {
                selectedDict = LoadDictionary(target.Code);
                if (selectedDict == null)
                {
                    // Thiếu file ngôn ngữ => rơi về ngôn ngữ nền để giao diện vẫn có chữ
                    target = Resolve(BaseLanguage);
                    isBase = true;
                }
            }

            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            AddStrings(snapshot, baseDict);
            AddStrings(snapshot, selectedDict); // phủ lên bản nền

            // Cập nhật bản sao chuỗi TRƯỚC khi đổi từ điển để mọi handler chạy sau đó đều thấy chuỗi mới
            _strings = snapshot;
            _current = target;

            // Thêm từ điển mới TRƯỚC rồi mới gỡ từ điển cũ => không có lúc nào thiếu khóa (tránh nháy chữ rỗng)
            var merged = app.Resources.MergedDictionaries;
            var oldDicts = merged.Where(IsLanguageDictionary).ToList();
            if (baseDict != null) merged.Add(baseDict);
            if (selectedDict != null) merged.Add(selectedDict);
            foreach (var old in oldDicts) merged.Remove(old);

            EnsureWindowLanguageHandler();
            ApplyXmlLanguage(app);

            if (persist) SavePreference(target.Code);

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleLanguage()
        {
            int index = 0;
            for (int i = 0; i < Supported.Count; i++)
            {
                if (string.Equals(Supported[i].Code, _current.Code, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            SetLanguage(Supported[(index + 1) % Supported.Count].Code);
        }

        public string GetString(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_strings.TryGetValue(key, out var value)) return value;

            Debug.WriteLine($"[Localization] Thiếu khóa: {key}");
            return key;
        }

        public string Format(string key, params object[] args)
        {
            string template = GetString(key);
            if (args == null || args.Length == 0) return template;

            try
            {
                return string.Format(Culture, template, args);
            }
            catch (FormatException)
            {
                // Bản dịch viết sai {0}/{1}: trả nguyên mẫu thay vì làm app lỗi
                return template;
            }
        }

        // ================= HÀM HỖ TRỢ =================

        private static LanguageOption Resolve(string? code)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                var hit = Supported.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            return Supported.First(l => l.Code == DefaultLanguage);
        }

        private static ResourceDictionary? LoadDictionary(string code)
        {
            try
            {
                return new ResourceDictionary
                {
                    Source = new Uri($"{LangFolderMarker}{code}.xaml", UriKind.Relative)
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Localization] Không nạp được từ điển '{code}': {ex.Message}");
                return null;
            }
        }

        private static void AddStrings(Dictionary<string, string> target, ResourceDictionary? source)
        {
            if (source == null) return;
            foreach (DictionaryEntry entry in source)
            {
                if (entry.Key is string key && entry.Value is string value)
                {
                    target[key] = value;
                }
            }
        }

        private static bool IsLanguageDictionary(ResourceDictionary dictionary)
        {
            var source = dictionary.Source;
            return source != null && source.OriginalString.Replace('\\', '/').Contains(LangFolderMarker);
        }

        private void SavePreference(string code)
        {
            try
            {
                var config = SettingsManager.LoadSettings();
                if (!string.Equals(config.Language, code, StringComparison.OrdinalIgnoreCase))
                {
                    config.Language = code;
                    SettingsManager.SaveSettings(config);
                }
            }
            catch (Exception ex)
            {
                // Không lưu được settings.json thì vẫn giữ ngôn ngữ cho phiên làm việc này
                Debug.WriteLine($"[Localization] Không lưu được settings.json: {ex.Message}");
            }
        }

        // Đặt thuộc tính Language (XmlLanguage) cho mọi cửa sổ: giúp WPF chọn đúng font dự phòng (Trung/Nga...)
        // và định dạng số/ngày trong Binding XAML theo ngôn ngữ đang chọn.
        private void EnsureWindowLanguageHandler()
        {
            if (_windowHandlerRegistered) return;
            _windowHandlerRegistered = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                window.Language = CurrentXmlLanguage();
            }
        }

        private void ApplyXmlLanguage(Application app)
        {
            var language = CurrentXmlLanguage();
            foreach (Window window in app.Windows)
            {
                window.Language = language;
            }
        }

        private XmlLanguage CurrentXmlLanguage()
        {
            string tag = _current.Culture.IetfLanguageTag;
            return XmlLanguage.GetLanguage(string.IsNullOrEmpty(tag) ? BaseLanguage : tag);
        }
    }

    /// <summary>
    /// Cách gọi ngắn để lấy chuỗi đa ngôn ngữ từ BẤT KỲ lớp nào (không cần tiêm DI):
    /// Loc.T("Khoa") và Loc.F("Khoa", thamSo1, thamSo2). Thiếu khóa thì trả về chính tên khóa.
    /// </summary>
    public static class Loc
    {
        public static string T(string key) => LocalizationService.Instance?.GetString(key) ?? key;

        public static string F(string key, params object[] args) => LocalizationService.Instance?.Format(key, args) ?? key;
    }
}
