using System;
using System.IO;

namespace SRM_by_Longtygu.Helpers
{
    // Kiểm tra TRƯỚC khi chép file lớn vào thư viện (SRM chạy portable nên thư viện nằm cạnh file exe):
    //  - Ổ định dạng FAT/FAT32 không lưu được file lớn hơn 4 GB (Windows chỉ báo "The parameter is incorrect").
    //  - Ổ không đủ dung lượng trống.
    // Lỗi được ném dưới dạng IOException với thông báo song ngữ dễ hiểu. Không ảnh hưởng dữ liệu đã lưu.
    public static class DiskSpaceGuard
    {
        private const long Fat32MaxFileBytes = 4294967295L; // 4 GiB - 1 byte

        public static void EnsureCanStore(string destDir, long fileBytes)
        {
            Check(destDir, fileBytes, fileBytes);
        }

        public static void EnsureCanStoreFolder(string sourceDir, string destDir)
        {
            long total = 0, largest = 0;
            foreach (var f in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                long len = new FileInfo(f).Length;
                total += len;
                if (len > largest) largest = len;
            }
            Check(destDir, total, largest);
        }

        private static void Check(string destDir, long totalBytes, long largestFileBytes)
        {
            DriveInfo drive;
            string format = string.Empty;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(destDir));
                if (string.IsNullOrEmpty(root)) return;
                drive = new DriveInfo(root);
                if (!drive.IsReady) return;
                format = drive.DriveFormat ?? string.Empty;
            }
            catch
            {
                return; // đường dẫn mạng / không xác định được ổ: bỏ qua kiểm tra, để Windows tự báo lỗi
            }

            bool isFat = format.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
                      || format.Equals("FAT", StringComparison.OrdinalIgnoreCase);
            if (isFat && largestFileBytes > Fat32MaxFileBytes)
            {
                throw new IOException(Loc.F("ResourceFile_Fat32TooLarge", drive.Name, format, FormatSize(largestFileBytes)));
            }

            long free;
            try { free = drive.AvailableFreeSpace; }
            catch { return; }
            if (totalBytes > free)
            {
                throw new IOException(Loc.F("ResourceFile_NotEnoughSpace", drive.Name, FormatSize(totalBytes), FormatSize(free)));
            }
        }

        private static string FormatSize(long bytes)
        {
            double gb = bytes / 1073741824.0;
            if (gb >= 1) return gb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " GB";
            return (bytes / 1048576.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }
    }
}
