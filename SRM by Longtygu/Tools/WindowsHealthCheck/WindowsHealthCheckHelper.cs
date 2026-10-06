using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SRM_by_Longtygu.Tools.WindowsHealthCheck
{
    public enum HealthCheckState { Good, Warning, Bad, Unknown }

    // Model 1 dòng trong khu vực "Tình trạng hệ thống". Các property State* tính sẵn
    // (màu/icon/nhãn) để bind thẳng ra XAML, không cần IValueConverter — theo đúng phong cách
    // đã dùng ở DiskHealthInfo (SizeDisplay, WearDisplay).
    public class HealthCheckItem
    {
        public string Name { get; set; }
        public string StatusText { get; set; }
        public string DetailText { get; set; }
        public string IconKind { get; set; } = "HelpCircleOutline";
        public HealthCheckState State { get; set; } = HealthCheckState.Unknown;

        public bool HasDetail => !string.IsNullOrWhiteSpace(DetailText);

        public string StateLabel
        {
            get
            {
                switch (State)
                {
                    case HealthCheckState.Good: return "Tốt";
                    case HealthCheckState.Warning: return "Cảnh báo";
                    case HealthCheckState.Bad: return "Lỗi";
                    default: return "Không rõ";
                }
            }
        }

        public string StateColor
        {
            get
            {
                switch (State)
                {
                    case HealthCheckState.Good: return "#6CCB5F";
                    case HealthCheckState.Warning: return "#E3B341";
                    case HealthCheckState.Bad: return "#E06C75";
                    default: return "#909090";
                }
            }
        }

        public string StateChipBackground
        {
            get
            {
                switch (State)
                {
                    case HealthCheckState.Good: return "#1A6CCB5F";
                    case HealthCheckState.Warning: return "#1AE3B341";
                    case HealthCheckState.Bad: return "#1AE06C75";
                    default: return "#1A909090";
                }
            }
        }

        public string StateIconKind
        {
            get
            {
                switch (State)
                {
                    case HealthCheckState.Good: return "CheckCircleOutline";
                    case HealthCheckState.Warning: return "AlertCircleOutline";
                    case HealthCheckState.Bad: return "CloseCircleOutline";
                    default: return "HelpCircleOutline";
                }
            }
        }
    }

    public static class WindowsHealthCheckHelper
    {
        // ============ KIỂM TRA QUYỀN ADMINISTRATOR ============
        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        // ============ TỔNG HỢP TẤT CẢ CÁC MỤC KIỂM TRA ============
        // Mỗi check được bọc try/catch RIÊNG (qua TryAdd) — bài học từ bug CPU trống dữ liệu
        // ở HardwareInfoView: 1 mục lỗi không được làm hỏng cả danh sách.
        public static List<HealthCheckItem> GetAllHealthChecks()
        {
            var items = new List<HealthCheckItem>();

            void TryAdd(Func<HealthCheckItem> check, string fallbackName)
            {
                try
                {
                    items.Add(check());
                }
                catch (Exception ex)
                {
                    items.Add(new HealthCheckItem
                    {
                        Name = fallbackName,
                        StatusText = "Lỗi khi kiểm tra",
                        DetailText = ex.Message,
                        State = HealthCheckState.Unknown,
                        IconKind = "AlertCircleOutline"
                    });
                }
            }

            TryAdd(CheckActivation, "Kích hoạt Windows");
            TryAdd(CheckBuild, "Phiên bản Windows");
            TryAdd(CheckUac, "UAC");
            TryAdd(CheckDefender, "Windows Defender");
            TryAdd(CheckFirewall, "Tường lửa");
            TryAdd(CheckSecureBoot, "Secure Boot");
            TryAdd(CheckTpm, "TPM");
            TryAdd(CheckBitLocker, "BitLocker");
            TryAdd(CheckPendingRestart, "Chờ khởi động lại");

            return items;
        }

        private static HealthCheckItem CheckActivation()
        {
            var item = new HealthCheckItem { Name = "Kích hoạt Windows", IconKind = "KeyOutline" };
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var status = Convert.ToUInt32(mo["LicenseStatus"]);
                        if (status == 1)
                        {
                            item.State = HealthCheckState.Good;
                            item.StatusText = "Đã kích hoạt";
                        }
                        else
                        {
                            item.State = HealthCheckState.Bad;
                            item.StatusText = "Chưa kích hoạt";
                        }
                        return item;
                    }
                }
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không xác định";
            }
            catch (Exception ex)
            {
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không đọc được";
                item.DetailText = ex.Message;
            }
            return item;
        }

        private static HealthCheckItem CheckBuild()
        {
            var item = new HealthCheckItem { Name = "Phiên bản Windows", IconKind = "MicrosoftWindows" };
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var productName = key?.GetValue("ProductName")?.ToString() ?? "Không rõ";
                    var displayVersion = key?.GetValue("DisplayVersion")?.ToString() ?? "";
                    var buildNumber = key?.GetValue("CurrentBuildNumber")?.ToString() ?? "";
                    var ubr = key?.GetValue("UBR")?.ToString();
                    var fullBuild = !string.IsNullOrEmpty(ubr) ? $"{buildNumber}.{ubr}" : buildNumber;

                    item.StatusText = $"{productName} {displayVersion}".Trim();
                    item.DetailText = string.IsNullOrEmpty(fullBuild) ? null : $"Build {fullBuild}";
                    item.State = HealthCheckState.Good;
                }
            }
            catch (Exception ex)
            {
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không đọc được";
                item.DetailText = ex.Message;
            }
            return item;
        }

        private static HealthCheckItem CheckUac()
        {
            var item = new HealthCheckItem { Name = "UAC (Kiểm soát tài khoản)", IconKind = "AccountLockOutline" };
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
                {
                    var value = key?.GetValue("EnableLUA");
                    var enabled = value != null && Convert.ToInt32(value) == 1;
                    item.State = enabled ? HealthCheckState.Good : HealthCheckState.Warning;
                    item.StatusText = enabled ? "Đang bật" : "Đang tắt";
                }
            }
            catch (Exception ex)
            {
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không đọc được";
                item.DetailText = ex.Message;
            }
            return item;
        }

        private static HealthCheckItem CheckDefender()
        {
            var item = new HealthCheckItem { Name = "Windows Defender", IconKind = "ShieldCheckOutline" };
            try
            {
                var output = RunProcessAndCaptureOutput("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"$s = Get-MpComputerStatus; Write-Output ($s.AntivirusEnabled.ToString() + '|' + $s.RealTimeProtectionEnabled.ToString())\"");

                var parts = output.Trim().Split('|');
                if (parts.Length == 2 && bool.TryParse(parts[0], out var avEnabled) && bool.TryParse(parts[1], out var rtEnabled))
                {
                    if (avEnabled && rtEnabled)
                    {
                        item.State = HealthCheckState.Good;
                        item.StatusText = "Đang bảo vệ (Real-time bật)";
                    }
                    else if (avEnabled)
                    {
                        item.State = HealthCheckState.Warning;
                        item.StatusText = "Bật nhưng Real-time đang tắt";
                    }
                    else
                    {
                        item.State = HealthCheckState.Bad;
                        item.StatusText = "Đang tắt";
                    }
                }
                else
                {
                    item.State = HealthCheckState.Unknown;
                    item.StatusText = "Không đọc được (có thể đang dùng AV khác)";
                }
            }
            catch (Exception ex)
            {
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không đọc được";
                item.DetailText = ex.Message;
            }
            return item;
        }

        private static HealthCheckItem CheckFirewall()
        {
            var item = new HealthCheckItem { Name = "Tường lửa (Firewall)", IconKind = "SecurityNetwork" };
            try
            {
                var output = RunProcessAndCaptureOutput("netsh.exe", "advfirewall show allprofiles state");
                var onCount = Regex.Matches(output, @"State\s+ON", RegexOptions.IgnoreCase).Count;

                if (onCount >= 3)
                {
                    item.State = HealthCheckState.Good;
                    item.StatusText = "Bật cả 3 hồ sơ mạng";
                }
                else if (onCount > 0)
                {
                    item.State = HealthCheckState.Warning;
                    item.StatusText = $"Chỉ bật {onCount}/3 hồ sơ mạng";
                }
                else
                {
                    item.State = HealthCheckState.Bad;
                    item.StatusText = "Đang tắt";
                }
            }
            catch (Exception ex)
            {
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không đọc được";
                item.DetailText = ex.Message;
            }
            return item;
        }

        private static HealthCheckItem CheckSecureBoot()
        {
            var item = new HealthCheckItem { Name = "Secure Boot", IconKind = "ShieldLockOutline" };
            try
            {
                var output = RunProcessAndCaptureOutput("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"try { Confirm-SecureBootUEFI } catch { 'NOTSUPPORTED' }\"");
                var trimmed = output.Trim();

                if (trimmed.Equals("True", StringComparison.OrdinalIgnoreCase))
                {
                    item.State = HealthCheckState.Good;
                    item.StatusText = "Đang bật";
                }
                else if (trimmed.Equals("False", StringComparison.OrdinalIgnoreCase))
                {
                    item.State = HealthCheckState.Warning;
                    item.StatusText = "Đang tắt";
                }
                else
                {
                    item.State = HealthCheckState.Unknown;
                    item.StatusText = "Không hỗ trợ (Legacy BIOS)";
                }
            }
            catch (Exception ex)
            {
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không đọc được";
                item.DetailText = ex.Message;
            }
            return item;
        }

        private static HealthCheckItem CheckTpm()
        {
            var item = new HealthCheckItem { Name = "TPM", IconKind = "Chip" };
            try
            {
                var output = RunProcessAndCaptureOutput("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"try { $t = Get-Tpm; Write-Output ($t.TpmPresent.ToString() + '|' + $t.TpmReady.ToString()) } catch { 'NOTSUPPORTED' }\"");
                var trimmed = output.Trim();
                var parts = trimmed.Split('|');

                if (parts.Length == 2 && bool.TryParse(parts[0], out var present) && bool.TryParse(parts[1], out var ready))
                {
                    if (present && ready)
                    {
                        item.State = HealthCheckState.Good;
                        item.StatusText = "Có TPM, sẵn sàng";
                    }
                    else if (present)
                    {
                        item.State = HealthCheckState.Warning;
                        item.StatusText = "Có TPM nhưng chưa sẵn sàng";
                    }
                    else
                    {
                        item.State = HealthCheckState.Bad;
                        item.StatusText = "Không có TPM";
                    }
                }
                else
                {
                    item.State = HealthCheckState.Unknown;
                    item.StatusText = "Không hỗ trợ / không đọc được";
                }
            }
            catch (Exception ex)
            {
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không đọc được";
                item.DetailText = ex.Message;
            }
            return item;
        }

        private static HealthCheckItem CheckBitLocker()
        {
            var item = new HealthCheckItem { Name = "BitLocker (ổ hệ thống)", IconKind = "FolderLockOutline" };
            try
            {
                var systemDrive = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var driveLetter = systemDrive.TrimEnd('\\');
                var output = RunProcessAndCaptureOutput("manage-bde.exe", $"-status {driveLetter}");

                if (output.IndexOf("Protection On", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    item.State = HealthCheckState.Good;
                    item.StatusText = "Đã mã hóa (Protection On)";
                }
                else if (output.IndexOf("Protection Off", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    item.State = HealthCheckState.Warning;
                    item.StatusText = "Chưa mã hóa (Protection Off)";
                }
                else
                {
                    item.State = HealthCheckState.Unknown;
                    item.StatusText = "Không hỗ trợ / không đọc được";
                }
            }
            catch (Exception ex)
            {
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không đọc được (cần quyền Administrator)";
                item.DetailText = ex.Message;
            }
            return item;
        }

        private static HealthCheckItem CheckPendingRestart()
        {
            var item = new HealthCheckItem { Name = "Chờ khởi động lại", IconKind = "Restart" };
            try
            {
                bool pending = false;

                using (var cbs = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                {
                    if (cbs != null) pending = true;
                }

                if (!pending)
                {
                    using (var wu = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\WindowsUpdate\Auto Update\RebootRequired"))
                    {
                        if (wu != null) pending = true;
                    }
                }

                if (!pending)
                {
                    using (var sm = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
                    {
                        var value = sm?.GetValue("PendingFileRenameOperations");
                        if (value != null) pending = true;
                    }
                }

                item.State = pending ? HealthCheckState.Warning : HealthCheckState.Good;
                item.StatusText = pending ? "Có tác vụ đang chờ khởi động lại" : "Không có tác vụ nào đang chờ";
            }
            catch (Exception ex)
            {
                item.State = HealthCheckState.Unknown;
                item.StatusText = "Không đọc được";
                item.DetailText = ex.Message;
            }
            return item;
        }

        // ============ CHẠY LỆNH ĐỌC NHANH, LẤY OUTPUT 1 LẦN (DÙNG CHO CÁC MỤC KIỂM TRA) ============
        private static string RunProcessAndCaptureOutput(string fileName, string arguments, int timeoutMs = 15000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(timeoutMs);
                return output;
            }
        }

        // ============ SCRIPT DỌN CACHE + SỬA LỖI WINDOWS UPDATE ============
        // Đổi tên (ren) thay vì xóa hẳn SoftwareDistribution/catroot2 để an toàn hơn —
        // Windows sẽ tự tạo lại thư mục mới, thư mục .bak cũ người dùng có thể xóa tay sau.
        public const string RepairWindowsUpdateScript =
            "net stop wuauserv & net stop bits & net stop cryptsvc & " +
            "ren %systemroot%\\SoftwareDistribution SoftwareDistribution.bak & " +
            "ren %systemroot%\\System32\\catroot2 catroot2.bak & " +
            "net start wuauserv & net start bits & net start cryptsvc & " +
            "wuauclt /resetauthorization /detectnow & " +
            "UsoClient StartScan";

        // ============ CHẠY LỆNH SỬA LỖI — STREAM LOG TRỰC TIẾP, HỖ TRỢ HỦY ============
        // Lưu ý: dùng process.Kill(true) và tính năng chờ qua TaskCompletionSource,
        // cần .NET (Core) 3.0+ / .NET 5+. Nếu project đang target .NET Framework cũ hơn,
        // đổi process.Kill(true) thành process.Kill() (không kill được tiến trình con).
        public static async Task<int> RunRepairCommandAsync(
            string fileName,
            string arguments,
            IProgress<string> logProgress,
            CancellationToken cancellationToken,
            bool autoConfirmYes = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = autoConfirmYes,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcs = new TaskCompletionSource<bool>();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) logProgress?.Report(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) logProgress?.Report(e.Data);
                };
                process.Exited += (s, e) => tcs.TrySetResult(true);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // CHKDSK /f trên ổ đang dùng (VD ổ hệ thống) sẽ hỏi xác nhận lên lịch kiểm tra
                // vào lần khởi động lại tiếp theo (Y/N) -> tự động xác nhận "Y" để không bị treo.
                if (autoConfirmYes)
                {
                    try
                    {
                        process.StandardInput.WriteLine("Y");
                        process.StandardInput.Close();
                    }
                    catch { /* bỏ qua nếu lệnh không cần input */ }
                }

                using (cancellationToken.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { /* bỏ qua */ }
                    tcs.TrySetCanceled();
                }))
                {
                    await tcs.Task.ConfigureAwait(false);
                }

                return process.HasExited ? process.ExitCode : -1;
            }
        }
    }
}
