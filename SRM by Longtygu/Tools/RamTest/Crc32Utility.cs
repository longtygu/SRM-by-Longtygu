using System.Collections.Generic;
using System.Threading;

namespace SRM_by_Longtygu.Tools.RamTest
{
    // Cài đặt CRC32 chuẩn (IEEE 802.3, polynomial 0xEDB88320) — không cần thêm NuGet.
    // Hỗ trợ tính tuần tự qua danh sách nhiều chunk byte[] (dùng cho vùng nhớ lớn được cấp phát theo khối).
    internal static class Crc32Utility
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            const uint poly = 0xEDB88320;
            var table = new uint[256];

            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = ((c & 1) != 0) ? (poly ^ (c >> 1)) : (c >> 1);
                }
                table[i] = c;
            }

            return table;
        }

        // Tính CRC32 trên toàn bộ dữ liệu nằm rải rác trong nhiều chunk (mô phỏng 1 vùng nhớ liên tục).
        // Kiểm tra CancellationToken định kỳ để có thể hủy giữa chừng khi test với vùng nhớ lớn.
        public static uint Compute(IList<byte[]> chunks, CancellationToken token)
        {
            uint crc = 0xFFFFFFFF;
            long processedSinceCheck = 0;
            const long checkInterval = 100_000_000; // ~100MB kiểm tra hủy 1 lần

            foreach (var chunk in chunks)
            {
                for (int i = 0; i < chunk.Length; i++)
                {
                    crc = Table[(crc ^ chunk[i]) & 0xFF] ^ (crc >> 8);

                    processedSinceCheck++;
                    if (processedSinceCheck >= checkInterval)
                    {
                        token.ThrowIfCancellationRequested();
                        processedSinceCheck = 0;
                    }
                }
            }

            return crc ^ 0xFFFFFFFF;
        }
    }
}
