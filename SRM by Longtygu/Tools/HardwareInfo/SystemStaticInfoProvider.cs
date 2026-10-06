using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Management;

namespace SRM_by_Longtygu.Tools.HardwareInfo
{
    // 1 dòng thông tin hiển thị trong tab "Tổng quan" — dùng chung để render UI và xuất file TXT,
    // đảm bảo dữ liệu hiển thị và dữ liệu xuất file luôn khớp nhau tuyệt đối.
    public class SystemInfoRow
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }

    // Đọc thông tin phần cứng "tĩnh" (không đổi theo thời gian thực) qua WMI + Registry.
    // Dùng riêng biệt với LibreHardwareMonitorLib vì WMI/Registry đáng tin cậy hơn cho tên/model/định danh,
    // trong khi LibreHardwareMonitorLib đáng tin cậy hơn cho nhiệt độ/tải thời gian thực.
    internal static class SystemStaticInfoProvider
    {
        // Danh sách đầy đủ theo phong cách trang "About" của Windows Settings
        public static List<SystemInfoRow> GetOverviewRows()
        {
            return new List<SystemInfoRow>
            {
                new SystemInfoRow { Label = "Edition", Value = GetWindowsEdition() },
                new SystemInfoRow { Label = "Version", Value = GetWindowsDisplayVersion() },
                new SystemInfoRow { Label = "Installed on", Value = GetInstallDate() },
                new SystemInfoRow { Label = "OS Build", Value = GetOsBuild() },
                new SystemInfoRow { Label = "Device Name", Value = Environment.MachineName },
                new SystemInfoRow { Label = "Processor", Value = GetCpuFullText() },
                new SystemInfoRow { Label = "Installed RAM", Value = GetTotalRamText() },
                new SystemInfoRow { Label = "Storage", Value = GetStorageSummary() },
                new SystemInfoRow { Label = "Graphics Card", Value = GetGpuFullText() },
                new SystemInfoRow { Label = "Device ID", Value = GetDeviceId() },
                new SystemInfoRow { Label = "Product ID", Value = GetProductId() },
                new SystemInfoRow { Label = "System Type", Value = GetSystemType() },
                new SystemInfoRow { Label = "Pen and touch", Value = GetPenTouchInfo() },
            };
        }

        // ---------- Windows / OS ----------

        private static string GetWindowsEdition()
        {
            return ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Không xác định";
        }

        private static string GetWindowsDisplayVersion()
        {
            return ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion")
                ?? ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId")
                ?? "Không xác định";
        }

        private static string GetInstallDate()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT InstallDate FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var dmtf = mo["InstallDate"]?.ToString();
                        if (!string.IsNullOrEmpty(dmtf))
                        {
                            var dt = ManagementDateTimeConverter.ToDateTime(dmtf);
                            return dt.ToString("dd-MM-yyyy");
                        }
                    }
                }
            }
            catch { }

            return "Không xác định";
        }

        private static string GetOsBuild()
        {
            var buildNumber = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
            var ubr = ReadRegistryDword(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");

            if (string.IsNullOrEmpty(buildNumber)) return "Không xác định";
            return ubr.HasValue ? $"{buildNumber}.{ubr.Value}" : buildNumber;
        }

        public static string GetOperatingSystem()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Caption, OSArchitecture, Version FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var caption = mo["Caption"]?.ToString()?.Trim();
                        var arch = mo["OSArchitecture"]?.ToString();
                        var version = mo["Version"]?.ToString();
                        return $"{caption} ({arch}) — build {version}";
                    }
                }
            }
            catch { }

            return "Không xác định";
        }

        public static string GetMotherboardInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        return $"{mo["Manufacturer"]} {mo["Product"]}".Trim();
                    }
                }
            }
            catch { }

            return "Không xác định";
        }

        // ---------- CPU ----------

        public static string GetCpuName() => QueryFirstString("SELECT Name FROM Win32_Processor", "Name") ?? "Không xác định";

        public static string GetCpuCoresInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        return $"{mo["NumberOfCores"]} nhân / {mo["NumberOfLogicalProcessors"]} luồng";
                    }
                }
            }
            catch { }

            return string.Empty;
        }

        private static string GetCpuFullText()
        {
            var name = GetCpuName();
            var cores = GetCpuCoresInfo();
            return string.IsNullOrEmpty(cores) ? name : $"{name} ({cores})";
        }

        // ---------- RAM ----------

        public static string GetTotalRamText()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        if (ulong.TryParse(mo["TotalPhysicalMemory"]?.ToString(), out var bytes))
                        {
                            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.#") + " GB";
                        }
                    }
                }
            }
            catch { }

            return "Không xác định";
        }

        // ---------- Storage (liệt kê toàn bộ ổ đĩa, giống định dạng Windows About) ----------

        private static string GetStorageSummary()
        {
            try
            {
                var parts = new List<string>();
                using (var searcher = new ManagementObjectSearcher("SELECT Model, Size, MediaType FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var model = mo["Model"]?.ToString()?.Trim() ?? "Ổ đĩa";
                        var mediaType = mo["MediaType"]?.ToString() ?? "";

                        string sizeText = "";
                        if (ulong.TryParse(mo["Size"]?.ToString(), out var bytes))
                        {
                            sizeText = (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0") + " GB";
                        }

                        string kind = mediaType.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0 ? "SSD"
                                    : mediaType.IndexOf("Fixed", StringComparison.OrdinalIgnoreCase) >= 0 ? "HDD"
                                    : "";

                        parts.Add(string.IsNullOrEmpty(kind) ? $"{sizeText} {model}".Trim() : $"{sizeText} {kind} {model}".Trim());
                    }
                }
                return parts.Count > 0 ? string.Join(", ", parts) : "Không xác định";
            }
            catch { }

            return "Không xác định";
        }

        // ---------- GPU ----------

        private static string GetGpuFullText()
        {
            try
            {
                var parts = new List<string>();
                using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var name = mo["Name"]?.ToString();
                        if (string.IsNullOrEmpty(name)) continue;

                        string ramText = "";
                        if (uint.TryParse(mo["AdapterRAM"]?.ToString(), out var ramBytes) && ramBytes > 0)
                        {
                            ramText = ramBytes >= 1073741824
                                ? $" ({ramBytes / 1073741824.0:0.#} GB)"
                                : $" ({ramBytes / 1048576.0:0} MB)";
                        }

                        parts.Add(name + ramText);
                    }
                }
                return parts.Count > 0 ? string.Join(", ", parts) : "Không xác định";
            }
            catch { }

            return "Không xác định";
        }

        // ---------- Định danh máy ----------

        private static string GetDeviceId()
        {
            return ReadRegistryString(@"SOFTWARE\Microsoft\SQMClient", "MachineId") ?? "Không xác định";
        }

        private static string GetProductId()
        {
            return QueryFirstString("SELECT SerialNumber FROM Win32_OperatingSystem", "SerialNumber") ?? "Không xác định";
        }

        private static string GetSystemType()
        {
            bool is64Os = Environment.Is64BitOperatingSystem;
            bool is64Proc = Environment.Is64BitProcess;
            string osArch = is64Os ? "64-bit operating system" : "32-bit operating system";
            string procArch = is64Proc ? "x64-based processor" : "x86-based processor";
            return $"{osArch}, {procArch}";
        }

        private static string GetPenTouchInfo()
        {
            try
            {
                int count = System.Windows.Input.Tablet.TabletDevices.Count;
                return count > 0
                    ? $"Phát hiện {count} thiết bị bút/cảm ứng"
                    : "No pen or touch input is available for this display";
            }
            catch
            {
                return "Không xác định";
            }
        }

        // ---------- Helpers ----------

        private static string QueryFirstString(string query, string field)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        return mo[field]?.ToString()?.Trim();
                    }
                }
            }
            catch { }

            return null;
        }

        private static string ReadRegistryString(string subKey, string valueName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(subKey))
                {
                    return key?.GetValue(valueName)?.ToString();
                }
            }
            catch { }

            return null;
        }

        private static int? ReadRegistryDword(string subKey, string valueName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(subKey))
                {
                    var value = key?.GetValue(valueName);
                    if (value != null && int.TryParse(value.ToString(), out var result))
                    {
                        return result;
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
