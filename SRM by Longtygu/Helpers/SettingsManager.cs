using System;
using System.IO;
using System.Text.Json;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Helpers
{
    public static class SettingsManager
    {
        private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppConfig LoadSettings()
        {
            if (!File.Exists(SettingsFile))
            {
                var defaultConfig = new AppConfig();
                SaveSettings(defaultConfig);
                return defaultConfig;
            }

            try
            {
                string json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig(); // Trả về mặc định nếu file lỗi
            }
        }

        public static void SaveSettings(AppConfig config)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(SettingsFile, json);
        }
    }
}