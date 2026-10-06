using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services
{
    public interface IUninstallService
    {
        Task<List<InstalledApp>> GetAllInstalledAppsAsync();
        Task<bool> RunUninstallAsync(string uninstallString);
    }

    public class UninstallService : IUninstallService
    {
        public async Task<List<InstalledApp>> GetAllInstalledAppsAsync()
        {
            return await Task.Run(() =>
            {
                var apps = new List<InstalledApp>();
                // Quét 3 vùng Registry chứa thông tin cài đặt của Windows
                string[] registryKeys = new string[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" // Dành cho CurrentUser
                };

                foreach (var keyPath in registryKeys)
                {
                    using (RegistryKey baseKey = keyPath.Contains("WOW6432Node") || keyPath == @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
                           ? Registry.LocalMachine.OpenSubKey(keyPath)
                           : Registry.CurrentUser.OpenSubKey(keyPath))
                    {
                        if (baseKey == null) continue;

                        foreach (string subkeyName in baseKey.GetSubKeyNames())
                        {
                            using (RegistryKey subkey = baseKey.OpenSubKey(subkeyName))
                            {
                                if (subkey == null) continue;

                                string displayName = subkey.GetValue("DisplayName") as string;
                                string uninstallString = subkey.GetValue("UninstallString") as string ?? subkey.GetValue("QuietUninstallString") as string;
                                string displayIcon = subkey.GetValue("DisplayIcon") as string;

                                if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(uninstallString) || displayName.Contains("KB"))
                                    continue;

                                // === BỘ PHÂN LOẠI THÔNG MINH ===
                                string appType = "User"; // Mặc định là app người dùng cài
                                int isSystemComponent = subkey.GetValue("SystemComponent") as int? ?? 0;
                                string nameLower = displayName.ToLower();
                                string pubLower = (subkey.GetValue("Publisher") as string ?? "").ToLower();

                                // 1. Nhận diện App Hệ thống (Core Windows)
                                if (isSystemComponent == 1 || nameLower.Contains("microsoft windows") || nameLower.Contains("security update"))
                                {
                                    appType = "System";
                                }
                                // 2. Nhận diện App Thành phần phụ (Driver, C++, Framework, SDK)
                                else if (nameLower.Contains("redistributable") || nameLower.Contains("framework") ||
                                         nameLower.Contains("runtime") || pubLower.Contains("intel") ||
                                         pubLower.Contains("nvidia") || pubLower.Contains("amd"))
                                {
                                    appType = "Component";
                                }
                                // ===============================

                                string sizeStr = "N/A";
                                object sizeObj = subkey.GetValue("EstimatedSize");
                                if (sizeObj != null && int.TryParse(sizeObj.ToString(), out int sizeKb) && sizeKb > 0)
                                {
                                    sizeStr = $"{(sizeKb / 1024.0):0.##} MB";
                                }

                                string dateStr = subkey.GetValue("InstallDate") as string;
                                if (!string.IsNullOrEmpty(dateStr) && dateStr.Length == 8)
                                    dateStr = $"{dateStr.Substring(6, 2)}/{dateStr.Substring(4, 2)}/{dateStr.Substring(0, 4)}";
                                else dateStr = "Không rõ";

                                apps.Add(new InstalledApp
                                {
                                    Name = displayName,
                                    Publisher = subkey.GetValue("Publisher") as string ?? "Unknown",
                                    InstallDate = dateStr,
                                    EstimatedSize = sizeStr,
                                    UninstallString = uninstallString,
                                    AppIcon = SRM_by_Longtygu.Helpers.IconExtractor.GetIconFromPath(displayIcon),
                                    AppType = appType // Gán loại App vừa phân tích được
                                });
                            }
                        }
                    }
                }

                var uniqueApps = new Dictionary<string, InstalledApp>();
                foreach (var app in apps)
                {
                    if (!uniqueApps.ContainsKey(app.Name))
                        uniqueApps[app.Name] = app;
                }
                return new List<InstalledApp>(uniqueApps.Values);
            });
        }

        public async Task<bool> RunUninstallAsync(string uninstallString)
        {
            if (string.IsNullOrEmpty(uninstallString)) return false;

            return await Task.Run(() =>
            {
                try
                {
                    string fileName = "";
                    string arguments = "";

                    // ĐÃ SỬA LỖI HOẠT ĐỘNG ẢO: Bóc tách đường dẫn thông minh hơn
                    if (uninstallString.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = "msiexec.exe";
                        arguments = uninstallString.Substring(11).Trim();
                    }
                    else if (uninstallString.StartsWith("\""))
                    {
                        int endQuote = uninstallString.IndexOf("\"", 1);
                        if (endQuote > 0)
                        {
                            fileName = uninstallString.Substring(1, endQuote - 1);
                            arguments = uninstallString.Length > endQuote + 1 ? uninstallString.Substring(endQuote + 1).Trim() : "";
                        }
                        else fileName = uninstallString;
                    }
                    else
                    {
                        // Tìm vị trí chữ .exe để cắt chính xác thay vì cắt ở dấu cách đầu tiên
                        int exeIndex = uninstallString.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                        if (exeIndex > 0)
                        {
                            fileName = uninstallString.Substring(0, exeIndex + 4); // Cắt lấy đến chữ .exe
                            arguments = uninstallString.Substring(exeIndex + 4).Trim(); // Phần còn lại là tham số
                        }
                        else
                        {
                            fileName = uninstallString; // Nếu không có .exe thì giữ nguyên
                        }
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true,
                        Verb = "runas", // Gọi quyền Admin
                        WindowStyle = ProcessWindowStyle.Normal // Đảm bảo giao diện gỡ cài đặt hiện lên (nếu có)
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        // Chờ bộ gỡ cài đặt tắt đi
                        process?.WaitForExit();

                        // Nghỉ thêm 1.5 giây đề phòng các bộ gỡ (như Unins000) tự tách tiến trình con 
                        Task.Delay(1500).Wait();

                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}