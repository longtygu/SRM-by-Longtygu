using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services.AppUpdate
{
    /// <summary>
    /// Các mục dữ liệu người dùng nằm cạnh file exe: KHÔNG BAO GIỜ được ghi đè hay xóa khi cập nhật,
    /// kể cả khi một gói zip nào đó lỡ chứa chúng.
    /// </summary>
    public static class AppUpdateProtection
    {
        public static readonly string[] ProtectedTopLevelNames =
        {
            "Database", "Data", "Library", "Backups", "Logs",
            "settings.json", "error_log.txt"
        };

        /// <summary>relativePath dùng dấu '/', ví dụ "Data/abc.png" hoặc "settings.json".</summary>
        public static bool IsProtected(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;
            int slash = relativePath.IndexOf('/');
            string top = slash < 0 ? relativePath : relativePath.Substring(0, slash);
            foreach (var name in ProtectedTopLevelNames)
            {
                if (string.Equals(top, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    public class AppUpdateDownloader : IAppUpdateDownloader
    {
        private const string MainExeName = "SRM by Longtygu.exe";
        private const string MainDllName = "SRM by Longtygu.dll";
        private const int BufferSize = 81920;
        // Chờ máy chủ phản hồi (gồm cả chuyển hướng sang CDN) tối đa 30 giây
        private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);

        // Đang tải mà KHÔNG nhận thêm dữ liệu quá 20 giây thì coi như mất kết nối.
        // Sau 5 giây không có dữ liệu thì bắt đầu báo "đang chờ" cho người dùng thấy.
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(20);
        private const int StallWarnSeconds = 5;

        // HttpClient riêng cho việc tải file lớn: không đặt Timeout tổng (file lớn tải lâu),
        // thay vào đó tự hủy khi KHÔNG nhận được dữ liệu trong 30 giây (xem StallTimeout).
        private static readonly HttpClient Http = CreateHttpClient();

        private readonly ILogService _logService;

        public AppUpdateDownloader(ILogService logService)
        {
            _logService = logService;
        }

        public static string StagingRoot => Path.Combine(Path.GetTempPath(), "SRM-by-Longtygu-update");

        public string StagingFolderPath => StagingRoot;

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SRM-by-Longtygu-UpdateChecker/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
            return client;
        }

        // ====================================================================
        // ĐIỂM VÀO
        // ====================================================================
        public async Task<AppUpdateStageResult> DownloadAndStageAsync(
            AppUpdateResult update,
            IProgress<AppUpdateProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            if (update == null || !update.CanDownloadAutomatically
                || !AppSelfUpdateChecker.IsTrustedDownloadUrl(update.AssetDownloadUrl))
            {
                return Fail("Bản phát hành này không hỗ trợ tự tải (thiếu file gói hoặc mã kiểm tra SHA256). Hãy dùng nút \"Xem trang tải về\".");
            }

            try
            {
                CleanupStaging(); // dọn rác của lần trước (nếu có)
                Directory.CreateDirectory(StagingRoot);
                EnsureFreeSpace(update.AssetSize);

                string zipPath = Path.Combine(StagingRoot, "update.zip");
                string extractDir = Path.Combine(StagingRoot, "extracted");

                await DownloadAndVerifyAsync(update, zipPath, progress, cancellationToken).ConfigureAwait(false);

                await Task.Run(
                    () => ExtractAndValidate(update, zipPath, extractDir, progress, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                TryDelete(zipPath); // đã giải nén xong: bỏ file zip để không giữ hai bản

                SafeLogInfo($"[Cập nhật SRM] Đã tải, xác minh SHA256 và giải nén gói {update.AssetName} vào {extractDir}");
                return new AppUpdateStageResult { Success = true, StagedFolder = extractDir };
            }
            catch (UpdateException ex)
            {
                CleanupStaging();
                SafeLogWarning("[Cập nhật SRM] " + ex.Message);
                return Fail(ex.Message);
            }
            catch (OperationCanceledException)
            {
                CleanupStaging();
                return new AppUpdateStageResult { Cancelled = true, ErrorMessage = "Đã hủy tải bản cập nhật." };
            }
            catch (HttpRequestException ex)
            {
                CleanupStaging();
                SafeLogError("[Cập nhật SRM] Lỗi mạng khi tải gói cập nhật.", ex);
                return Fail("Không kết nối được tới máy chủ tải file. Hãy kiểm tra mạng rồi thử lại.");
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                CleanupStaging();
                SafeLogError("[Cập nhật SRM] Hết dung lượng ổ đĩa khi tải gói cập nhật.", ex);
                return Fail("Ổ đĩa đã đầy khi ghi file tạm. Hãy giải phóng dung lượng rồi thử lại.");
            }
            catch (UnauthorizedAccessException ex)
            {
                CleanupStaging();
                SafeLogError("[Cập nhật SRM] Không có quyền ghi vào thư mục tạm.", ex);
                return Fail("Không có quyền ghi vào thư mục tạm của Windows.");
            }
            catch (InvalidDataException ex)
            {
                CleanupStaging();
                SafeLogError("[Cập nhật SRM] File tải về không phải zip hợp lệ.", ex);
                return Fail("File tải về không phải gói zip hợp lệ.");
            }
            catch (Exception ex)
            {
                CleanupStaging();
                SafeLogError("[Cập nhật SRM] Lỗi không xác định khi chuẩn bị gói cập nhật.", ex);
                return Fail("Đã xảy ra lỗi không mong muốn khi chuẩn bị gói cập nhật.");
            }
        }

        public void CleanupStaging()
        {
            try
            {
                if (Directory.Exists(StagingRoot))
                {
                    Directory.Delete(StagingRoot, true);
                }
            }
            catch (Exception ex)
            {
                SafeLogWarning("[Cập nhật SRM] Không dọn hết được thư mục tạm: " + ex.Message);
            }
        }

        // ====================================================================
        // BƯỚC 1: TẢI + KIỂM TRA SHA256 (tính băm ngay trong lúc tải, không đọc lại file)
        // ====================================================================
        private async Task DownloadAndVerifyAsync(
            AppUpdateResult update, string zipPath, IProgress<AppUpdateProgress>? progress, CancellationToken ct)
        {
            string partPath = zipPath + ".part";

            using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stallCts.CancelAfter(HeaderTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, update.AssetDownloadUrl);
            HttpResponseMessage response;
            try
            {
                response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stallCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new UpdateException("Hết thời gian chờ máy chủ tải file. Hãy kiểm tra mạng rồi thử lại.");
            }

            var state = new StallState();
            using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task monitor = Task.CompletedTask;

            try
            {
                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new UpdateException($"Máy chủ trả về mã lỗi {(int)response.StatusCode} khi tải file.");
                    }

                    long total = response.Content.Headers.ContentLength ?? update.AssetSize;
                    state.BytesTotal = total;

                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long done = 0;

                    // Từ đây mới theo dõi "đứng dữ liệu" (bộ hẹn giờ chờ phản hồi không còn cần nữa)
                    stallCts.CancelAfter(Timeout.InfiniteTimeSpan);
                    state.Touch();
                    monitor = MonitorStallAsync(state, response, stallCts, progress, monitorCts.Token);

                    using (var network = await response.Content.ReadAsStreamAsync(stallCts.Token).ConfigureAwait(false))
                    using (var file = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
                    {
                        byte[] buffer = new byte[BufferSize];
                        var clock = Stopwatch.StartNew();
                        long lastReportMs = -1000;
                        long lastSampleMs = 0;
                        long lastSampleBytes = 0;
                        double speed = 0;

                        while (true)
                        {
                            Task<int> readTask = network.ReadAsync(buffer, 0, buffer.Length, stallCts.Token);

                            if (!readTask.IsCompleted)
                            {
                                // Chờ dữ liệu HOẶC chờ bộ theo dõi kết luận đã mất kết nối
                                await Task.WhenAny(readTask, monitor).ConfigureAwait(false);

                                if (!readTask.IsCompleted)
                                {
                                    // Tác vụ đọc bị bỏ lại phía sau thì phải "nhận" lỗi của nó, nếu không sẽ thành lỗi Task không ai xử lý
                                    _ = readTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

                                    // Người dùng/hệ thống chủ động hủy: không phải mất kết nối
                                    ct.ThrowIfCancellationRequested();

                                    // Bộ theo dõi đã báo mất kết nối mà việc đọc vẫn treo: bỏ cuộc ngay, không chờ nữa
                                    if (state.NetworkLost || state.TimedOut)
                                    {
                                        throw StallFailure(state);
                                    }
                                }
                            }

                            int read;
                            try
                            {
                                read = await readTask.ConfigureAwait(false);
                            }
                            catch (Exception) when (state.NetworkLost || state.TimedOut)
                            {
                                throw StallFailure(state);
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                throw new UpdateException("Mất kết nối khi đang tải. Hãy kiểm tra mạng rồi thử lại.");
                            }
                            catch (IOException ex)
                            {
                                // Chỉ việc ĐỌC từ mạng nằm trong khối này (ghi đĩa nằm bên ngoài) nên đây là kết nối bị ngắt
                                SafeLogError("[Cập nhật SRM] Kết nối bị ngắt giữa chừng khi đang tải.", ex);
                                throw new UpdateException("Kết nối bị ngắt giữa chừng khi đang tải. Hãy kiểm tra mạng rồi thử lại. (Đã hủy và xóa file tạm.)");
                            }

                            if (read == 0) break;

                            state.Touch();
                            await file.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                            hasher.AppendData(buffer, 0, read);
                            done += read;
                            state.BytesDone = done;
                            state.Touch();

                            long nowMs = clock.ElapsedMilliseconds;
                            if (nowMs - lastReportMs >= 250)
                            {
                                long deltaMs = nowMs - lastSampleMs;
                                if (deltaMs > 0)
                                {
                                    double instant = (done - lastSampleBytes) * 1000.0 / deltaMs;
                                    speed = speed <= 0 ? instant : (speed * 0.6 + instant * 0.4); // làm mượt để số không nhảy loạn
                                }
                                lastSampleMs = nowMs;
                                lastSampleBytes = done;
                                lastReportMs = nowMs;

                                progress?.Report(new AppUpdateProgress
                                {
                                    Phase = AppUpdatePhase.Downloading,
                                    BytesDone = done,
                                    BytesTotal = total,
                                    BytesPerSecond = speed
                                });
                            }
                        }

                        await file.FlushAsync(ct).ConfigureAwait(false);

                        // Đã nhận hết dữ liệu: dừng theo dõi trước khi sang bước kiểm tra (băm/ghi file không phải "mất kết nối")
                        monitorCts.Cancel();

                        progress?.Report(new AppUpdateProgress
                        {
                            Phase = AppUpdatePhase.Downloading,
                            BytesDone = done,
                            BytesTotal = total > 0 ? total : done,
                            BytesPerSecond = speed
                        });
                    }

                    // ---- Kiểm tra toàn vẹn ----
                    progress?.Report(new AppUpdateProgress { Phase = AppUpdatePhase.Verifying, BytesDone = done, BytesTotal = done });

                    if (update.AssetSize > 0 && done != update.AssetSize)
                    {
                        throw new UpdateException("File tải về bị thiếu dữ liệu (kích thước không khớp với GitHub). Đã hủy.");
                    }

                    string actual = Convert.ToHexString(hasher.GetHashAndReset());
                    if (!string.Equals(actual, update.AssetSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        SafeLogError($"[Cập nhật SRM] SHA256 KHÔNG khớp. Mong đợi {update.AssetSha256}, thực tế {actual}", null);
                        throw new UpdateException("Mã SHA256 của file tải về KHÔNG khớp với mã GitHub công bố (file có thể bị hỏng hoặc bị can thiệp). Đã hủy và xóa file.");
                    }
                }
            }
            finally
            {
                monitorCts.Cancel();
                try { await monitor.ConfigureAwait(false); } catch { }
            }

            File.Move(partPath, zipPath, true);
        }

        /// <summary>
        /// Chạy song song với việc tải: mỗi giây kiểm tra xem đã bao lâu rồi chưa nhận được dữ liệu.
        /// Quá 5 giây thì báo "đang chờ"; mất mạng hoặc quá 20 giây thì hủy việc tải bằng cách đóng kết nối.
        /// </summary>
        private static async Task MonitorStallAsync(
            StallState state, HttpResponseMessage response, CancellationTokenSource stallCts,
            IProgress<AppUpdateProgress>? progress, CancellationToken token)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    double idle = state.IdleSeconds;

                    if (idle >= StallWarnSeconds)
                    {
                        progress?.Report(new AppUpdateProgress
                        {
                            Phase = AppUpdatePhase.Downloading,
                            BytesDone = state.BytesDone,
                            BytesTotal = state.BytesTotal,
                            StalledSeconds = (int)idle,
                            StallLimitSeconds = (int)StallTimeout.TotalSeconds
                        });
                    }

                    if (idle >= 3 && !NetworkInterface.GetIsNetworkAvailable())
                    {
                        state.NetworkLost = true;
                    }
                    else if (idle >= StallTimeout.TotalSeconds)
                    {
                        state.TimedOut = true;
                    }

                    if (state.NetworkLost || state.TimedOut)
                    {
                        try { stallCts.Cancel(); } catch { }
                        try { response.Dispose(); } catch { }
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Việc tải đã kết thúc bình thường: bộ theo dõi dừng theo
            }
        }

        private static UpdateException StallFailure(StallState state)
        {
            if (state.NetworkLost)
            {
                return new UpdateException("Máy tính đã mất kết nối mạng khi đang tải. Hãy kết nối lại rồi thử lại. (Đã hủy và xóa file tạm.)");
            }

            return new UpdateException(
                $"Mất kết nối khi đang tải: không nhận được dữ liệu trong {(int)StallTimeout.TotalSeconds} giây. Hãy kiểm tra mạng rồi thử lại. (Đã hủy và xóa file tạm.)");
        }

        // ====================================================================
        // BƯỚC 2: GIẢI NÉN AN TOÀN + KIỂM TRA GÓI
        // ====================================================================
        private void ExtractAndValidate(
            AppUpdateResult update, string zipPath, string extractDir,
            IProgress<AppUpdateProgress>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(extractDir);
            string extractFull = Path.GetFullPath(extractDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            using var archive = ZipFile.OpenRead(zipPath);

            // Chuẩn hóa tên entry: chấp nhận cả '\' lẫn '/', bỏ qua mục thư mục
            var files = new List<(ZipArchiveEntry Entry, string Name)>();
            foreach (var entry in archive.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith("/", StringComparison.Ordinal)) continue;
                files.Add((entry, name));
            }

            if (files.Count == 0)
            {
                throw new UpdateException("Gói cập nhật rỗng.");
            }

            // Bỏ thư mục gốc chung (ví dụ "SRM-by-Longtygu-v1.0.0/"), không gán cứng tên
            string? sharedRoot = null;
            bool hasSharedRoot = true;
            foreach (var (_, name) in files)
            {
                int slash = name.IndexOf('/');
                if (slash <= 0) { hasSharedRoot = false; break; }

                string top = name.Substring(0, slash);
                if (sharedRoot == null) sharedRoot = top;
                else if (!string.Equals(sharedRoot, top, StringComparison.OrdinalIgnoreCase)) { hasSharedRoot = false; break; }
            }
            int strip = (hasSharedRoot && sharedRoot != null) ? sharedRoot.Length + 1 : 0;

            // Tổng dung lượng cần giải nén (để tính %)
            long totalBytes = 0;
            foreach (var (entry, name) in files)
            {
                if (!AppUpdateProtection.IsProtected(name.Substring(strip))) totalBytes += entry.Length;
            }

            long doneBytes = 0;
            int skippedProtected = 0;

            foreach (var (entry, name) in files)
            {
                ct.ThrowIfCancellationRequested();

                string relative = name.Substring(strip);

                // Dữ liệu người dùng: bỏ qua, không bao giờ giải nén
                if (AppUpdateProtection.IsProtected(relative))
                {
                    skippedProtected++;
                    continue;
                }

                // Chống đường dẫn độc hại (zip-slip): cấm "..", ".", ổ đĩa, đường dẫn tuyệt đối
                foreach (var segment in relative.Split('/'))
                {
                    if (segment == ".." || segment == "." || segment.Contains(':'))
                    {
                        throw new UpdateException("Gói cập nhật chứa đường dẫn không an toàn. Đã hủy.");
                    }
                }

                string destination = Path.GetFullPath(Path.Combine(extractDir, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(extractFull, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdateException("Gói cập nhật chứa đường dẫn nằm ngoài thư mục giải nén. Đã hủy.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);

                doneBytes += entry.Length;
                progress?.Report(new AppUpdateProgress
                {
                    Phase = AppUpdatePhase.Extracting,
                    BytesDone = doneBytes,
                    BytesTotal = totalBytes
                });
            }

            if (skippedProtected > 0)
            {
                SafeLogWarning($"[Cập nhật SRM] Gói chứa {skippedProtected} file thuộc dữ liệu người dùng, đã bỏ qua (không giải nén).");
            }

            ValidateStagedPackage(update, extractDir);
        }

        private void ValidateStagedPackage(AppUpdateResult update, string extractDir)
        {
            string stagedExe = Path.Combine(extractDir, MainExeName);
            if (!File.Exists(stagedExe))
            {
                throw new UpdateException($"Gói cập nhật không chứa file chương trình \"{MainExeName}\".");
            }

            if (update.LatestVersion == null) return;

            // Đọc phiên bản từ metadata của file .dll chính (không nạp vào bộ nhớ)
            string stagedDll = Path.Combine(extractDir, MainDllName);
            if (!File.Exists(stagedDll))
            {
                SafeLogWarning($"[Cập nhật SRM] Không thấy {MainDllName} trong gói, bỏ qua bước đối chiếu phiên bản.");
                return;
            }

            try
            {
                Version? packageVersion = AssemblyName.GetAssemblyName(stagedDll).Version;
                if (packageVersion == null) return;

                var normalized = new Version(packageVersion.Major, packageVersion.Minor,
                                             Math.Max(packageVersion.Build, 0), Math.Max(packageVersion.Revision, 0));

                if (normalized.CompareTo(update.LatestVersion) != 0)
                {
                    throw new UpdateException(
                        $"Phiên bản trong gói ({normalized}) không khớp với bản phát hành ({update.LatestVersion}). Đã hủy để tránh cài nhầm.");
                }
            }
            catch (UpdateException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SafeLogWarning("[Cập nhật SRM] Không đọc được phiên bản trong gói, bỏ qua đối chiếu: " + ex.Message);
            }
        }

        // ====================================================================
        // HÀM HỖ TRỢ
        // ====================================================================
        private static void EnsureFreeSpace(long assetSize)
        {
            try
            {
                string? root = Path.GetPathRoot(StagingRoot);
                if (string.IsNullOrEmpty(root)) return;

                long free = new DriveInfo(root).AvailableFreeSpace;
                long need = Math.Max(assetSize, 1) * 5; // gói zip + bản giải nén
                if (free < need)
                {
                    throw new UpdateException(
                        $"Ổ đĩa chứa thư mục tạm không đủ dung lượng trống (cần khoảng {need / 1048576} MB, còn {free / 1048576} MB).");
                }
            }
            catch (UpdateException)
            {
                throw;
            }
            catch
            {
                // Không kiểm tra được thì bỏ qua; nếu thiếu thật thì lỗi khi ghi file sẽ được bắt ở trên.
            }
        }

        private static bool IsDiskFull(IOException ex)
        {
            // ERROR_HANDLE_DISK_FULL = 39, ERROR_DISK_FULL = 112
            return (ex.HResult & 0xFFFF) is 39 or 112;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static AppUpdateStageResult Fail(string message)
        {
            return new AppUpdateStageResult { Success = false, ErrorMessage = message };
        }

        private void SafeLogInfo(string message) { try { _logService?.LogInfo(message); } catch { } }
        private void SafeLogWarning(string message) { try { _logService?.LogWarning(message); } catch { } }
        private void SafeLogError(string message, Exception? ex)
        {
            try
            {
                if (ex == null) _logService?.LogError(message);
                else _logService?.LogError(message, ex);
            }
            catch { }
        }

        /// <summary>Trạng thái dùng chung giữa luồng tải và bộ theo dõi "đứng dữ liệu" (an toàn khi dùng từ nhiều luồng).</summary>
        private sealed class StallState
        {
            private long _lastDataTimestamp = Stopwatch.GetTimestamp();
            private long _bytesDone;
            private long _bytesTotal;

            public volatile bool NetworkLost;
            public volatile bool TimedOut;

            public long BytesDone
            {
                get => Interlocked.Read(ref _bytesDone);
                set => Interlocked.Exchange(ref _bytesDone, value);
            }

            public long BytesTotal
            {
                get => Interlocked.Read(ref _bytesTotal);
                set => Interlocked.Exchange(ref _bytesTotal, value);
            }

            public void Touch() => Interlocked.Exchange(ref _lastDataTimestamp, Stopwatch.GetTimestamp());

            public double IdleSeconds => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastDataTimestamp)).TotalSeconds;
        }

        /// <summary>Lỗi có thông báo thân thiện bằng tiếng Việt để hiển thị thẳng cho người dùng.</summary>
        private sealed class UpdateException : Exception
        {
            public UpdateException(string message) : base(message) { }
        }
    }
}
