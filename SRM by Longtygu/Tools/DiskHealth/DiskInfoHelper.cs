using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading.Tasks;
using System.Threading;


namespace SRM_by_Longtygu.Tools.DiskHealth
{
    public class DiskHealthInfo
    {
        public string Model { get; set; }
        public long SizeBytes { get; set; }
        public string SizeDisplay => SizeBytes > 0 ? $"{SizeBytes / 1024.0 / 1024.0 / 1024.0:F0} GB" : "Không rõ";
        public string HealthStatus { get; set; } = "Không xác định";
        public string MediaType { get; set; } = "Không rõ";
        public int? WearPercent { get; set; }
        public string WearDisplay => WearPercent.HasValue
            ? $"{WearPercent}% đã sử dụng"
            : "Không đọc được (cần quyền Administrator)";
    }
    public class BenchmarkProgress
    {
        public string StatusText { get; set; }
        public int PercentComplete { get; set; }
    }
    public class BenchmarkResult
    {
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public double WriteSpeedMBps { get; set; }
        public double ReadSpeedMBps { get; set; }
        public string ErrorMessage { get; set; }
    }

    public static class DiskInfoHelper
    {
        // ============ ĐỌC THÔNG TIN SỨC KHỎE Ổ ĐĨA (SMART) ============
        public static List<DiskHealthInfo> GetPhysicalDisksHealth()
        {
            var result = new List<DiskHealthInfo>();

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                {
                    foreach (ManagementObject disk in searcher.Get())
                    {
                        var info = new DiskHealthInfo
                        {
                            Model = disk["FriendlyName"]?.ToString() ?? "Không rõ",
                            SizeBytes = disk["Size"] != null ? Convert.ToInt64(disk["Size"]) : 0,
                            HealthStatus = TranslateHealthStatus(disk["HealthStatus"]),
                            MediaType = TranslateMediaType(disk["MediaType"])
                        };

                        var deviceId = disk["DeviceId"]?.ToString();
                        if (!string.IsNullOrEmpty(deviceId))
                        {
                            TryReadWear(deviceId, info);
                        }

                        result.Add(info);
                    }
                }
            }
            catch (Exception ex)
            {
                // Namespace root\Microsoft\Windows\Storage có thể không truy cập được
                // (thiếu quyền Admin, phiên bản Windows cũ...) -> fallback dữ liệu cơ bản
                Debug.WriteLine($"[DiskInfoHelper] Không đọc được MSFT_PhysicalDisk: {ex.Message}");
                return GetBasicDiskInfoFallback();
            }

            return result;
        }

        // Đọc % hao mòn (Wear) qua bảng liên kết MSFT_StorageReliabilityCounter, thường cần quyền Admin
        private static void TryReadWear(string deviceId, DiskHealthInfo info)
        {
            try
            {
                var query = $"ASSOCIATORS OF {{MSFT_PhysicalDisk.DeviceId='{deviceId}'}} " +
                            "WHERE ResultClass=MSFT_StorageReliabilityCounter";

                using (var relSearcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", query))
                {
                    foreach (ManagementObject counter in relSearcher.Get())
                    {
                        if (counter["Wear"] != null)
                        {
                            info.WearPercent = Convert.ToInt32(counter["Wear"]);
                        }
                    }
                }
            }
            catch
            {
                // Không đủ quyền hoặc ổ không hỗ trợ -> bỏ qua, WearDisplay tự hiện thông báo phù hợp
            }
        }

        // Dữ liệu tối thiểu nếu namespace Storage mới không truy cập được (model + dung lượng qua Win32_DiskDrive)
        private static List<DiskHealthInfo> GetBasicDiskInfoFallback()
        {
            var result = new List<DiskHealthInfo>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject disk in searcher.Get())
                    {
                        result.Add(new DiskHealthInfo
                        {
                            Model = disk["Model"]?.ToString() ?? "Không rõ",
                            SizeBytes = disk["Size"] != null ? Convert.ToInt64(disk["Size"]) : 0,
                            HealthStatus = "Cần quyền Administrator để đọc",
                            MediaType = "Không rõ"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DiskInfoHelper] Không đọc được Win32_DiskDrive: {ex.Message}");
            }
            return result;
        }

        private static string TranslateHealthStatus(object raw)
        {
            if (raw == null) return "Không xác định";
            switch (Convert.ToUInt16(raw))
            {
                case 0: return "Tốt (Healthy)";
                case 1: return "Cảnh báo (Warning)";
                case 2: return "Kém (Unhealthy)";
                default: return "Không xác định";
            }
        }

        private static string TranslateMediaType(object raw)
        {
            if (raw == null) return "Không rõ";
            switch (Convert.ToUInt16(raw))
            {
                case 3: return "HDD (Ổ cứng cơ)";
                case 4: return "SSD (Ổ cứng thể rắn)";
                case 5: return "SCM";
                default: return "Không rõ";
            }
        }

        // ============ BENCHMARK TỐC ĐỘ ĐỌC / GHI ĐƠN GIẢN ============
        public static Task<BenchmarkResult> RunBenchmarkAsync(
            string driveLetter,
            IProgress<BenchmarkProgress> progress,
            CancellationToken cancellationToken = default)
        {
            // Bọc trong Task.Run để ép chạy hẳn trên background thread,
            // tránh trường hợp FileStream không async thật làm treo UI thread.
            return Task.Run(() =>
            {
                const int testSizeMb = 100;
                const int chunkSizeMb = 4;
                var totalChunks = testSizeMb / chunkSizeMb;

                var result = new BenchmarkResult();

                // Không ghi thẳng vào gốc ổ đĩa (VD: C:\) vì tài khoản người dùng thường
                // không có quyền ghi ở đó -> tạo 1 thư mục con riêng để test.
                var testFolder = Path.Combine(driveLetter, "SRM_BenchmarkTemp");
                string testFilePath;

                try
                {
                    Directory.CreateDirectory(testFolder);
                }
                catch (UnauthorizedAccessException)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Không có quyền ghi vào ổ {driveLetter}. Hãy chọn ổ đĩa khác (VD: ổ chứa Windows thường bị giới hạn quyền ghi ở thư mục gốc).";
                    return result;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Không thể tạo thư mục kiểm tra: {ex.Message}";
                    return result;
                }

                testFilePath = Path.Combine(testFolder, $"srm_benchmark_{Guid.NewGuid():N}.tmp");
                var buffer = new byte[chunkSizeMb * 1024 * 1024];
                new Random().NextBytes(buffer);

                try
                {
                    // ---- GHI (0% -> 50%) ----
                    progress?.Report(new BenchmarkProgress { StatusText = "Đang kiểm tra tốc độ ghi...", PercentComplete = 0 });

                    var writeStopwatch = Stopwatch.StartNew();
                    using (var fs = new FileStream(testFilePath, FileMode.Create, FileAccess.Write,
                                                    FileShare.None, buffer.Length, FileOptions.WriteThrough))
                    {
                        for (int i = 0; i < totalChunks; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            fs.Write(buffer, 0, buffer.Length);

                            var percent = (int)((i + 1) / (double)totalChunks * 50);
                            progress?.Report(new BenchmarkProgress
                            {
                                StatusText = $"Đang kiểm tra tốc độ ghi... ({(i + 1) * chunkSizeMb}MB / {testSizeMb}MB)",
                                PercentComplete = percent
                            });
                        }
                    }
                    writeStopwatch.Stop();
                    result.WriteSpeedMBps = testSizeMb / writeStopwatch.Elapsed.TotalSeconds;

                    // ---- ĐỌC (50% -> 100%) ----
                    progress?.Report(new BenchmarkProgress { StatusText = "Đang kiểm tra tốc độ đọc...", PercentComplete = 50 });

                    var readStopwatch = Stopwatch.StartNew();
                    using (var fs = new FileStream(testFilePath, FileMode.Open, FileAccess.Read,
                                                    FileShare.None, buffer.Length, FileOptions.SequentialScan))
                    {
                        int readChunkIndex = 0;
                        int bytesRead;
                        while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            readChunkIndex++;
                            var percent = 50 + (int)(readChunkIndex / (double)totalChunks * 50);
                            progress?.Report(new BenchmarkProgress
                            {
                                StatusText = $"Đang kiểm tra tốc độ đọc... ({readChunkIndex * chunkSizeMb}MB / {testSizeMb}MB)",
                                PercentComplete = Math.Min(percent, 100)
                            });
                        }
                    }
                    readStopwatch.Stop();
                    result.ReadSpeedMBps = testSizeMb / readStopwatch.Elapsed.TotalSeconds;

                    result.Success = true;
                }
                catch (OperationCanceledException)
                {
                    result.Cancelled = true;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }
                finally
                {
                    if (!string.IsNullOrEmpty(testFilePath) && File.Exists(testFilePath))
                    {
                        try { File.Delete(testFilePath); } catch { /* bỏ qua */ }
                    }

                    // Dọn luôn thư mục test nếu đã rỗng, để không để lại rác trên ổ đĩa người dùng
                    try
                    {
                        if (Directory.Exists(testFolder) && Directory.GetFiles(testFolder).Length == 0)
                        {
                            Directory.Delete(testFolder);
                        }
                    }
                    catch { /* bỏ qua */ }
                }

                return result;
            }, cancellationToken);
        }
    }
}