using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SRM_by_Longtygu.Services.AppUpdate
{
    public class AppUpdateInstaller : IAppUpdateInstaller
    {
        /// <summary>File cũ bị thay được đổi tên thêm đuôi này (không xóa ngay vì Windows không cho xóa file đang chạy).</summary>
        public const string OldFileSuffix = ".srm-old";

        private const string MainExeName = "SRM by Longtygu.exe";
        private const string MainFilePrefix = "SRM by Longtygu.";

        private readonly ILogService _logService;
        private readonly string _installFolder;

        public AppUpdateInstaller(ILogService logService)
            : this(logService, AppDomain.CurrentDomain.BaseDirectory)
        {
        }

        // Dành cho kiểm thử: chỉ định thư mục cài đặt khác. (internal nên bộ nạp DI không dùng đến)
        internal AppUpdateInstaller(ILogService logService, string installFolder)
        {
            _logService = logService;
            _installFolder = installFolder.TrimEnd('\\', '/');
        }

        public string InstallFolderPath => _installFolder;

        public string MainExePath => Path.Combine(_installFolder, MainExeName);

        // ====================================================================
        // KIỂM TRA QUYỀN GHI
        // ====================================================================
        public string? CheckInstallFolderWritable()
        {
            try
            {
                string probe = Path.Combine(_installFolder, ".srm-write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return null;
            }
            catch (Exception ex)
            {
                SafeLogWarning("[Cập nhật SRM] Không ghi được vào thư mục cài đặt: " + ex.Message);
                return "Không có quyền ghi vào thư mục cài đặt của ứng dụng:\n" + _installFolder +
                       "\n\nHãy đặt ứng dụng ở thư mục thường (ví dụ Desktop hoặc D:\\Apps), không đặt trong C:\\Program Files, " +
                       "hoặc dùng nút \"Xem trang tải về\" để cập nhật thủ công.";
            }
        }

        // ====================================================================
        // ĐIỂM VÀO
        // ====================================================================
        public Task<AppUpdateInstallResult> InstallAsync(
            string stagedFolder, IProgress<AppUpdateProgress>? progress, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => InstallCore(stagedFolder, progress, cancellationToken));
        }

        private AppUpdateInstallResult InstallCore(string stagedFolder, IProgress<AppUpdateProgress>? progress, CancellationToken ct)
        {
            var journal = new List<JournalEntry>();
            int replaced = 0, added = 0, unchanged = 0, skippedProtected = 0;
            string? dbBackup = null;

            try
            {
                if (string.IsNullOrWhiteSpace(stagedFolder) || !Directory.Exists(stagedFolder))
                {
                    return Fail("Không tìm thấy gói cập nhật đã giải nén.", false);
                }

                char sep = Path.DirectorySeparatorChar;
                string stagedFull = Path.GetFullPath(stagedFolder).TrimEnd(sep) + sep;
                string installFull = Path.GetFullPath(_installFolder).TrimEnd(sep) + sep;

                // ---------- 1) Lập kế hoạch: chỉ liệt kê file thật sự cần thay hoặc thêm ----------
                var plan = new List<PlanItem>();
                foreach (string file in Directory.EnumerateFiles(stagedFull, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();

                    string rel = file.Substring(stagedFull.Length).Replace('\\', '/');

                    // Dữ liệu người dùng: không bao giờ ghi, kể cả khi gói lỡ chứa
                    if (AppUpdateProtection.IsProtected(rel)) { skippedProtected++; continue; }
                    if (rel.Contains(OldFileSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                    string dest = Path.GetFullPath(Path.Combine(_installFolder, rel.Replace('/', sep)));
                    if (!dest.StartsWith(installFull, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InstallException("Gói cập nhật chứa đường dẫn nằm ngoài thư mục cài đặt. Đã hủy.");
                    }

                    long size = new FileInfo(file).Length;
                    bool exists = File.Exists(dest);

                    // File không đổi thì bỏ qua (cùng kích thước và cùng mã băm)
                    if (exists && new FileInfo(dest).Length == size && SameContent(file, dest))
                    {
                        unchanged++;
                        continue;
                    }

                    plan.Add(new PlanItem(file, dest, rel, size, exists));
                }

                // File chính của ứng dụng (SRM by Longtygu.*) thay SAU CÙNG: nếu lỗi giữa chừng thì chưa đụng tới nó
                plan = plan
                    .OrderBy(p => IsMainFile(p.Relative) ? 1 : 0)
                    .ThenBy(p => p.Relative, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (plan.Count == 0)
                {
                    SafeLogInfo("[Cập nhật SRM] Mọi file trong gói đều giống bản đang cài, không có gì để thay.");
                    return new AppUpdateInstallResult
                    {
                        Success = true,
                        FilesUnchanged = unchanged,
                        ProtectedSkipped = skippedProtected
                    };
                }

                // ---------- 2) Kiểm tra dung lượng ổ đĩa ----------
                long need = plan.Sum(p => p.Size) + 64L * 1024 * 1024;
                EnsureFreeSpace(installFull, need);

                // ---------- 3) Sao lưu file cơ sở dữ liệu (nhỏ, rẻ) trước khi đổi bất cứ file nào ----------
                progress?.Report(new AppUpdateProgress { Phase = AppUpdatePhase.BackingUp });
                dbBackup = BackupDatabase();

                // ---------- 4) Áp dụng từng file ----------
                progress?.Report(new AppUpdateProgress { Phase = AppUpdatePhase.Installing, BytesDone = 0, BytesTotal = plan.Count });

                for (int i = 0; i < plan.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    PlanItem item = plan[i];
                    ApplyFile(item, journal);
                    if (item.DestExists) replaced++; else added++;

                    progress?.Report(new AppUpdateProgress { Phase = AppUpdatePhase.Installing, BytesDone = i + 1, BytesTotal = plan.Count });
                }

                SafeLogInfo($"[Cập nhật SRM] Cài đặt xong: thay {replaced}, thêm {added}, giữ nguyên {unchanged}, bỏ qua dữ liệu người dùng {skippedProtected}. " +
                            $"Bản sao lưu DB: {(dbBackup ?? "không có")}");

                return new AppUpdateInstallResult
                {
                    Success = true,
                    FilesReplaced = replaced,
                    FilesAdded = added,
                    FilesUnchanged = unchanged,
                    ProtectedSkipped = skippedProtected,
                    DatabaseBackupPath = dbBackup
                };
            }
            catch (OperationCanceledException)
            {
                return FailWithRollback("Đã hủy cài đặt.", journal, null);
            }
            catch (InstallException ex)
            {
                return FailWithRollback(ex.Message, journal, ex);
            }
            catch (Exception ex)
            {
                return FailWithRollback("Không cài đặt được bản cập nhật: " + ex.Message, journal, ex);
            }
        }

        // ====================================================================
        // ÁP DỤNG MỘT FILE: đổi tên file cũ rồi chép file mới vào
        // ====================================================================
        private void ApplyFile(PlanItem item, List<JournalEntry> journal)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.Dest)!);

            string? oldPath = null;
            if (item.DestExists)
            {
                oldPath = UniqueOldPath(item.Dest);
                string dest = item.Dest;
                string old = oldPath;
                Retry(() => { ClearReadOnly(dest); File.Move(dest, old); }, "đổi tên " + item.Relative);
            }

            // Ghi nhật ký TRƯỚC khi chép: nếu chép dở thì vẫn hoàn tác được
            journal.Add(new JournalEntry(item.Dest, oldPath));

            string source = item.Source;
            string target = item.Dest;
            Retry(() => File.Copy(source, target, true), "chép " + item.Relative);
        }

        /// <summary>Thử lại vài lần khi file đang bị phần mềm diệt virus hoặc tiến trình khác giữ.</summary>
        private void Retry(Action action, string what)
        {
            const int attempts = 5;
            for (int i = 1; ; i++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException ex) when (i < attempts)
                {
                    SafeLogWarning($"[Cập nhật SRM] Thử lại {i}/{attempts} khi {what}: {ex.Message}");
                    Thread.Sleep(400);
                }
                catch (UnauthorizedAccessException ex) when (i < attempts)
                {
                    SafeLogWarning($"[Cập nhật SRM] Thử lại {i}/{attempts} khi {what}: {ex.Message}");
                    Thread.Sleep(400);
                }
            }
        }

        // ====================================================================
        // HOÀN TÁC
        // ====================================================================
        private AppUpdateInstallResult FailWithRollback(string message, List<JournalEntry> journal, Exception? ex)
        {
            if (ex != null && ex is not InstallException)
            {
                SafeLogError("[Cập nhật SRM] Cài đặt thất bại: " + message, ex);
            }
            else
            {
                SafeLogWarning("[Cập nhật SRM] Cài đặt thất bại: " + message);
            }

            if (journal.Count == 0)
            {
                return Fail(message + " (Chưa có file nào bị thay đổi.)", false);
            }

            var problems = Rollback(journal);
            if (problems.Count == 0)
            {
                return Fail(message + " Đã hoàn tác: ứng dụng vẫn ở bản cũ, dữ liệu không bị ảnh hưởng.", true);
            }

            SafeLogError("[Cập nhật SRM] Hoàn tác KHÔNG trọn vẹn: " + string.Join("; ", problems), null);
            return Fail(message + " Hoàn tác chưa trọn vẹn với " + problems.Count +
                        " file (xem nhật ký). Dữ liệu của bạn vẫn an toàn; nếu ứng dụng không mở được, hãy giải nén lại bản phát hành mới đè lên thư mục cũ.", false);
        }

        private List<string> Rollback(List<JournalEntry> journal)
        {
            var problems = new List<string>();

            for (int i = journal.Count - 1; i >= 0; i--)
            {
                JournalEntry entry = journal[i];
                try
                {
                    Retry(() =>
                    {
                        if (File.Exists(entry.Dest))
                        {
                            ClearReadOnly(entry.Dest);
                            File.Delete(entry.Dest);
                        }
                        if (entry.OldPath != null && File.Exists(entry.OldPath))
                        {
                            File.Move(entry.OldPath, entry.Dest);
                        }
                    }, "hoàn tác " + Path.GetFileName(entry.Dest));
                }
                catch (Exception ex)
                {
                    problems.Add(Path.GetFileName(entry.Dest) + ": " + ex.Message);
                }
            }

            return problems;
        }

        // ====================================================================
        // HÀM HỖ TRỢ
        // ====================================================================
        private string? BackupDatabase()
        {
            string db = Path.Combine(_installFolder, "Database", "library.db");
            if (!File.Exists(db))
            {
                SafeLogInfo("[Cập nhật SRM] Không có Database\\library.db để sao lưu, bỏ qua bước sao lưu.");
                return null;
            }

            try
            {
                string folder = Path.Combine(_installFolder, "Backups");
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, $"library_PreUpdate_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                File.Copy(db, target, false);
                return target;
            }
            catch (Exception ex)
            {
                // Chưa đổi file nào nên dừng ở đây là an toàn tuyệt đối
                throw new InstallException("Không sao lưu được file cơ sở dữ liệu (library.db) trước khi cập nhật: " + ex.Message + " Đã hủy để đảm bảo an toàn.");
            }
        }

        private static void EnsureFreeSpace(string installFull, long need)
        {
            try
            {
                string? root = Path.GetPathRoot(installFull);
                if (string.IsNullOrEmpty(root)) return;

                long free = new DriveInfo(root).AvailableFreeSpace;
                if (free < need)
                {
                    throw new InstallException($"Ổ đĩa chứa ứng dụng không đủ dung lượng trống để cài (cần khoảng {need / 1048576} MB, còn {free / 1048576} MB). Chưa có file nào bị thay đổi.");
                }
            }
            catch (InstallException)
            {
                throw;
            }
            catch
            {
                // Không kiểm tra được thì bỏ qua; thiếu thật thì lỗi khi chép sẽ được bắt và hoàn tác.
            }
        }

        private static bool IsMainFile(string relative)
        {
            return relative.IndexOf('/') < 0 && relative.StartsWith(MainFilePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string UniqueOldPath(string dest)
        {
            string path = dest + OldFileSuffix;
            if (!File.Exists(path)) return path;
            return path + "." + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static bool SameContent(string a, string b)
        {
            try
            {
                using var fa = File.OpenRead(a);
                using var fb = File.OpenRead(b);
                return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
            }
            catch
            {
                return false; // không đọc được thì coi như khác nhau để thay cho chắc
            }
        }

        private static void ClearReadOnly(string path)
        {
            try
            {
                var attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
            }
            catch { }
        }

        private static AppUpdateInstallResult Fail(string message, bool rolledBack)
        {
            return new AppUpdateInstallResult { Success = false, RolledBack = rolledBack, ErrorMessage = message };
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

        private sealed record PlanItem(string Source, string Dest, string Relative, long Size, bool DestExists);

        private sealed record JournalEntry(string Dest, string? OldPath);

        private sealed class InstallException : Exception
        {
            public InstallException(string message) : base(message) { }
        }
    }

    /// <summary>
    /// Dọn các file cũ (.srm-old) và thư mục tạm còn sót sau lần cập nhật trước.
    /// Chạy nền sau khi ứng dụng mở: file cũ có thể còn bị tiến trình cũ giữ trong vài giây đầu nên sẽ thử lại.
    /// </summary>
    public static class AppUpdateCleanup
    {
        public static void StartBackgroundCleanup(ILogService? logService)
        {
            Task.Run(async () =>
            {
                try
                {
                    string folder = AppDomain.CurrentDomain.BaseDirectory;
                    int[] delaysMs = { 5000, 20000, 60000 };

                    foreach (int delay in delaysMs)
                    {
                        await Task.Delay(delay).ConfigureAwait(false);

                        int remaining = DeleteOldFiles(folder, out int deleted);
                        if (deleted > 0)
                        {
                            try { logService?.LogInfo($"[Cập nhật SRM] Đã dọn {deleted} file cũ (.srm-old) sau lần cập nhật trước."); } catch { }
                        }
                        if (remaining == 0) break;
                    }

                    try
                    {
                        if (Directory.Exists(AppUpdateDownloader.StagingRoot))
                        {
                            Directory.Delete(AppUpdateDownloader.StagingRoot, true);
                        }
                    }
                    catch { }
                }
                catch
                {
                    // Dọn dẹp thất bại không được ảnh hưởng ứng dụng
                }
            });
        }

        /// <summary>Xóa file *.srm-old* ở thư mục gốc và các thư mục chương trình (bỏ qua thư mục dữ liệu). Trả về số file còn sót lại.</summary>
        public static int DeleteOldFiles(string folder, out int deleted)
        {
            deleted = 0;
            int remaining = 0;

            var targets = new List<string>();
            try { targets.AddRange(Directory.EnumerateFiles(folder, "*" + AppUpdateInstaller.OldFileSuffix + "*", SearchOption.TopDirectoryOnly)); } catch { }

            try
            {
                foreach (string sub in Directory.EnumerateDirectories(folder))
                {
                    string name = Path.GetFileName(sub);
                    if (AppUpdateProtection.IsProtected(name)) continue; // không bao giờ động vào thư mục dữ liệu

                    try { targets.AddRange(Directory.EnumerateFiles(sub, "*" + AppUpdateInstaller.OldFileSuffix + "*", SearchOption.AllDirectories)); } catch { }
                }
            }
            catch { }

            foreach (string file in targets)
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    deleted++;
                }
                catch
                {
                    remaining++; // đang bị giữ, lần sau sẽ thử lại
                }
            }

            return remaining;
        }
    }
}
