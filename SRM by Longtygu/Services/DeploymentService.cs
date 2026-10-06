using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SRM_by_Longtygu.Services
{
    public interface IDeploymentService
    {
        Task<bool> CreateRestorePointAsync(string description);
        // Thêm một Action callback để báo % tiến độ
        Task<bool> RunSilentInstallAsync(string relativeFilePath, string arguments, Action<int> progressCallback = null);
    }

    public class DeploymentService : IDeploymentService
    {
        public async Task<bool> CreateRestorePointAsync(string description)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-ExecutionPolicy Bypass -NoProfile -Command \"Enable-ComputerRestore -Drive 'C:\'; Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS'\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (var process = Process.Start(startInfo))
                    {
                        process?.WaitForExit();
                        return process?.ExitCode == 0;
                    }
                }
                catch { return false; }
            });
        }

        public async Task<bool> RunSilentInstallAsync(string relativeFilePath, string arguments, Action<int> progressCallback = null)
        {
            if (string.IsNullOrEmpty(relativeFilePath)) return false;
            string absolutePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeFilePath);
            if (!File.Exists(absolutePath)) return false;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = absolutePath,
                    Arguments = arguments ?? "",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return false;

                    int fakeProgress = 0;
                    var random = new Random();

                    // Vòng lặp kiểm tra: Chừng nào file cài còn đang chạy ngầm, % sẽ tự động tăng dần
                    while (!process.HasExited)
                    {
                        await Task.Delay(400); // Cứ 0.4s cập nhật 1 lần
                        if (fakeProgress < 95)
                        {
                            fakeProgress += random.Next(3, 12); // Tăng ngẫu nhiên tạo cảm giác chân thực
                            if (fakeProgress > 95) fakeProgress = 95; // Kẹt ở 95% đợi cài xong hẳn
                        }
                        progressCallback?.Invoke(fakeProgress);
                    }

                    // Khi file đóng, báo 100%
                    progressCallback?.Invoke(100);
                    return process.ExitCode == 0;
                }
            }
            catch { return false; }
        }
    }
}