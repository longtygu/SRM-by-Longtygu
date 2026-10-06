using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading.Tasks;

namespace SRM_by_Longtygu.Services
{
    public interface ISystemService
    {
        bool IsAdministrator();
        Task<bool> CreateRestorePointAsync(string description);
    }

    public class SystemService : ISystemService
    {
        private readonly ILogService _logService;

        public SystemService(ILogService logService)
        {
            _logService = logService;
        }

        public bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public async Task<bool> CreateRestorePointAsync(string description)
        {
            if (!IsAdministrator())
            {
                _logService.LogWarning("Không thể tạo Restore Point: Cần quyền Administrator.");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logService.LogInfo("Đang khởi tạo System Restore Point...");
                    string script = $"Checkpoint-Computer -Description '{description}' -RestorePointType APPLICATION_INSTALL";

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };

                    using (Process process = Process.Start(psi))
                    {
                        process.WaitForExit();
                        if (process.ExitCode == 0)
                        {
                            _logService.LogInfo("Tạo Restore Point thành công.");
                            return true;
                        }
                        else
                        {
                            _logService.LogWarning($"PowerShell trả về ExitCode: {process.ExitCode}");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logService.LogError("Lỗi Exception khi tạo Restore Point", ex);
                    return false;
                }
            });
        }
    }
}