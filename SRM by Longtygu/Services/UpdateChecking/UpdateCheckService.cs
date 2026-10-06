using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SRM_by_Longtygu.Models;
using SRM_by_Longtygu.Repositories;

namespace SRM_by_Longtygu.Services.UpdateChecking
{
    public class UpdateCheckService : IUpdateCheckService
    {
        private readonly ISoftwareRepository _softwareRepo;
        private readonly ISoftwareVersionRepository _versionRepo;
        private readonly IFileProcessingService _fileService;
        private readonly ILogService _logService;
        private readonly Dictionary<string, IUpdateSource> _sources;

        // MỚI: HttpClient dùng CHUNG cho toàn bộ lượt tải, KHÔNG tạo mới mỗi lần gọi.
        // Trước đây mỗi lần tải tạo 1 HttpClient mới rồi Dispose ngay khi xong - việc này đóng
        // socket đột ngột và buộc phải bắt tay TLS lại từ đầu cho lượt tải kế tiếp, gây tốn thời
        // gian/CPU không cần thiết, đặc biệt khi tải nhiều phần mềm liên tiếp hoặc song song.
        private static readonly HttpClient _downloadHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

        // Giới hạn số request quét song song, tránh spam quá nhiều kết nối cùng lúc
        // khi bấm "Kiểm tra cập nhật" cho cả thư viện lớn
        private readonly SemaphoreSlim _throttle = new SemaphoreSlim(4);

        public UpdateCheckService(
            ISoftwareRepository softwareRepo,
            ISoftwareVersionRepository versionRepo,
            IFileProcessingService fileService,
            ILogService logService)
        {
            _softwareRepo = softwareRepo;
            _versionRepo = versionRepo;
            _fileService = fileService;
            _logService = logService;

            // Đăng ký các nguồn kiểm tra hỗ trợ sẵn. Muốn thêm nguồn mới (Winget, Chocolatey...)
            // chỉ cần implement thêm 1 class IUpdateSource và thêm 1 dòng ở đây.
            var github = new GitHubReleaseSource();
            var regexPage = new RegexPageSource();
            var winget = new WingetSource();
            _sources = new Dictionary<string, IUpdateSource>(StringComparer.OrdinalIgnoreCase)
            {
                [github.SourceTypeKey] = github,
                [regexPage.SourceTypeKey] = regexPage,
                [winget.SourceTypeKey] = winget
            };
        }

        public async Task<UpdateCheckResult> CheckOneAsync(Software software, CancellationToken ct = default)
        {
            if (software == null)
                return new UpdateCheckResult { ErrorMessage = "Phần mềm không hợp lệ." };

            if (string.IsNullOrWhiteSpace(software.UpdateSourceType) ||
                software.UpdateSourceType == "None" ||
                !_sources.TryGetValue(software.UpdateSourceType, out var source))
            {
                return new UpdateCheckResult { ErrorMessage = "Phần mềm chưa cấu hình nguồn kiểm tra cập nhật." };
            }

            await _throttle.WaitAsync(ct);
            try
            {
                var result = await source.GetLatestAsync(software, ct);

                if (result.Success)
                {
                    await _softwareRepo.UpdateUpdateCheckInfoAsync(software.Id, result.LatestVersion, DateTime.Now);
                    _logService.LogInfo($"Kiểm tra cập nhật '{software.Name}': tìm thấy phiên bản {result.LatestVersion}.");
                }
                else
                {
                    _logService.LogWarning($"Kiểm tra cập nhật '{software.Name}' thất bại: {result.ErrorMessage}");
                }

                return result;
            }
            finally
            {
                _throttle.Release();
            }
        }

        public async Task CheckAllAsync(IEnumerable<Software> softwares, Action<Software, UpdateCheckResult> onEachChecked, CancellationToken ct = default)
        {
            var targets = softwares?.Where(s => s.HasUpdateSourceConfigured).ToList() ?? new List<Software>();

            var tasks = targets.Select(async software =>
            {
                var result = await CheckOneAsync(software, ct);
                onEachChecked?.Invoke(software, result);
            });

            await Task.WhenAll(tasks);
        }

        public async Task<bool> DownloadAndAddVersionAsync(Software software, string downloadUrl, string version, IProgress<DownloadProgressInfo> progress = null, CancellationToken ct = default)
        {
            if (software == null || string.IsNullOrWhiteSpace(downloadUrl))
            {
                _logService.LogWarning($"Không có link tải hợp lệ cho '{software?.Name}', bỏ qua tự động tải về.");
                return false;
            }

            string fileName;
            try { fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath); }
            catch { fileName = null; }
            if (string.IsNullOrWhiteSpace(fileName)) fileName = $"{software.Name}_{version}.exe";

            // MỚI: Tải THẲNG vào đúng vị trí cuối cùng Data/[Software]/[Version]/, KHÔNG qua file tạm
            // trung gian nữa. Trước đây: ghi ra Temp -> đọc lại để copy sang Data -> đọc lại LẦN NỮA để
            // băm SHA256 = tổng cộng ~4 lượt I/O trên toàn bộ dung lượng file, và quan trọng hơn là
            // Windows Defender/antivirus phải quét file những 2 lần (1 lần ở Temp, 1 lần ở Data) vì đây
            // là 2 file vật lý khác nhau -> đây nhiều khả năng chính là nguyên nhân khiến mạng "ì" lúc
            // tải trước đó, không phải do băng thông. Giờ chỉ còn 1 lượt ghi + băm ngay trong lúc tải
            // (không đọc lại file sau khi ghi xong) + chỉ bị quét virus 1 lần duy nhất.
            string absoluteFilePath;
            try
            {
                absoluteFilePath = await _fileService.PrepareVersionFilePathAsync(software.Name, version, fileName);
            }
            catch (Exception ex)
            {
                _logService.LogError($"Không tạo được thư mục đích cho '{software.Name}'", ex);
                return false;
            }

            try
            {
                using var response = await _downloadHttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                long totalRead = 0;

                using var sha256 = SHA256.Create();

                // Buffer lớn hơn trước (256KB thay vì 80KB) -> ít lượt gọi syscall đọc/ghi hơn cho cùng
                // 1 dung lượng file, giảm overhead CPU khi tải file lớn.
                var buffer = new byte[262144];

                using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
                using (var fileStream = new FileStream(absoluteFilePath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
                {
                    int read;

                    // Đo tốc độ tải: báo cáo định kỳ (~200ms/lần) thay vì mỗi chunk, tránh spam UI
                    // hàng trăm lần/giây. Tốc độ tính theo khoảng thời gian kể từ lần báo cáo trước
                    // (tốc độ tức thời), không phải trung bình từ đầu, nên phản ánh đúng thực tế hơn.
                    var stopwatch = Stopwatch.StartNew();
                    long lastReportedBytes = 0;
                    var lastReportedElapsed = TimeSpan.Zero;

                    while ((read = await httpStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);

                        // MỚI: Băm SHA256 NGAY TRONG LÚC TẢI (từng chunk một), không cần đọc lại file
                        // sau khi tải xong nữa - tiết kiệm hẳn 1 lượt đọc toàn bộ file.
                        sha256.TransformBlock(buffer, 0, read, null, 0);

                        totalRead += read;

                        var sinceLastReport = stopwatch.Elapsed - lastReportedElapsed;
                        if (sinceLastReport.TotalMilliseconds >= 200)
                        {
                            double speed = sinceLastReport.TotalSeconds > 0
                                ? (totalRead - lastReportedBytes) / sinceLastReport.TotalSeconds
                                : 0;

                            progress?.Report(new DownloadProgressInfo
                            {
                                PercentComplete = (totalBytes.HasValue && totalBytes.Value > 0)
                                    ? (int)(totalRead * 100 / totalBytes.Value)
                                    : -1,
                                BytesDownloaded = totalRead,
                                TotalBytes = totalBytes ?? 0,
                                SpeedBytesPerSecond = speed
                            });

                            lastReportedBytes = totalRead;
                            lastReportedElapsed = stopwatch.Elapsed;
                        }
                    }

                    // Báo cáo cuối cùng khi tải xong - luôn 100%, tốc độ 0 (đã tải xong, không còn "đang chạy")
                    progress?.Report(new DownloadProgressInfo
                    {
                        PercentComplete = 100,
                        BytesDownloaded = totalRead,
                        TotalBytes = totalBytes ?? totalRead,
                        SpeedBytesPerSecond = 0
                    });
                }

                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                string sha256Hash = BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();

                string relativePath = Path.Combine("Data", software.Name, version, fileName);

                await _versionRepo.InsertAsync(new SoftwareVersion
                {
                    SoftwareId = software.Id,
                    Version = version,
                    FilePath = relativePath,
                    FileSize = totalRead,
                    SHA256 = sha256Hash,
                    IsPortable = false
                });

                _logService.LogInfo($"Đã tự động thêm phiên bản {version} cho '{software.Name}' vào thư viện.");
                return true;
            }
            catch (Exception ex)
            {
                _logService.LogError($"Lỗi khi tải và thêm phiên bản mới cho '{software.Name}'", ex);

                // Dọn file dở dang nếu tải lỗi/bị huỷ giữa chừng, tránh để lại file rác/hỏng trong thư viện
                try { if (File.Exists(absoluteFilePath)) File.Delete(absoluteFilePath); } catch { /* bỏ qua */ }

                return false;
            }
        }
    }
}
