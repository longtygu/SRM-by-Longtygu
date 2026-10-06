using System;
using System.Linq;
using System.Windows;
using SRM_by_Longtygu.Helpers;

namespace SRM_by_Longtygu.Services
{
    public interface ILocalizationService
    {
        void SetLanguage(string cultureCode);
        void ToggleLanguage();
    }

    public class LocalizationService : ILocalizationService
    {
        public void SetLanguage(string cultureCode)
        {
            // Tải file từ điển tương ứng
            var dictionary = new ResourceDictionary
            {
                Source = new Uri($"Resources/Langs/{cultureCode}.xaml", UriKind.Relative)
            };

            // Tìm và xóa từ điển ngôn ngữ cũ đang nạp trong App.xaml (nếu có)
            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Resources/Langs/"));

            if (oldDict != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(oldDict);
            }

            // Nạp từ điển mới vào
            Application.Current.Resources.MergedDictionaries.Add(dictionary);

            // Lưu lại vào file settings.json
            var config = SettingsManager.LoadSettings();
            config.Language = cultureCode;
            SettingsManager.SaveSettings(config);
        }

        // Nút bấm chuyển qua lại nhanh giữa Anh - Việt
        public void ToggleLanguage()
        {
            var config = SettingsManager.LoadSettings();
            string newLang = config.Language == "en-US" ? "vi-VN" : "en-US";
            SetLanguage(newLang);
        }
    }
}