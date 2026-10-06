using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SRM_by_Longtygu.Tools.HashCalculator
{
    public static class HashCalculatorHelper
    {
        // ============ BẢNG TRA CRC32 (CHUẨN IEEE 802.3, polynomial 0xEDB88320) ============
        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            const uint polynomial = 0xEDB88320;
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? polynomial ^ (c >> 1) : c >> 1;
                }
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32Update(uint crc, byte[] buffer, int length)
        {
            for (int i = 0; i < length; i++)
            {
                crc = Crc32Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            }
            return crc;
        }

        // ============ ĐỊNH DẠNG DUNG LƯỢNG FILE ============
        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:F2} {units[unitIndex]}";
        }

        // ============ TÍNH ĐỒNG THỜI 5 LOẠI HASH — ĐỌC FILE 1 LẦN DUY NHẤT THEO KHỐI ============
        // Đọc trực tiếp từ FilePath gốc (FileShare.Read, không khóa file người khác đang dùng),
        // không copy/không lưu tạm ra đâu cả — chỉ đọc byte để tính hash rồi bỏ qua.
        public static Task ComputeHashesAsync(HashResultItem item, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                item.IsProcessing = true;
                item.ProgressPercent = 0;
                item.SetError(null);

                try
                {
                    using (var md5 = MD5.Create())
                    using (var sha1 = SHA1.Create())
                    using (var sha256 = SHA256.Create())
                    using (var sha512 = SHA512.Create())
                    {
                        uint crc = 0xFFFFFFFF;
                        const int bufferSize = 1024 * 1024; // đọc theo khối 1MB
                        var buffer = new byte[bufferSize];

                        using (var stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read,
                            FileShare.Read, bufferSize, FileOptions.SequentialScan))
                        {
                            long totalBytes = stream.Length;
                            long readBytes = 0;
                            int read;

                            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                md5.TransformBlock(buffer, 0, read, null, 0);
                                sha1.TransformBlock(buffer, 0, read, null, 0);
                                sha256.TransformBlock(buffer, 0, read, null, 0);
                                sha512.TransformBlock(buffer, 0, read, null, 0);
                                crc = Crc32Update(crc, buffer, read);

                                readBytes += read;
                                if (totalBytes > 0)
                                {
                                    item.ProgressPercent = (int)(readBytes * 100 / totalBytes);
                                }
                            }
                        }

                        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        sha512.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                        item.SetHashResults(
                            ToHex(md5.Hash),
                            ToHex(sha1.Hash),
                            ToHex(sha256.Hash),
                            ToHex(sha512.Hash),
                            (crc ^ 0xFFFFFFFF).ToString("X8"));

                        item.ProgressPercent = 100;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    item.SetError(ex.Message);
                }
                finally
                {
                    item.IsProcessing = false;
                }
            }, cancellationToken);
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null) return "";
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
