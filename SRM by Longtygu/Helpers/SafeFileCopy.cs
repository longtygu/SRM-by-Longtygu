using System;
using System.IO;

namespace SRM_by_Longtygu.Helpers
{
    // File.Copy của Windows (CopyFileEx) có thể dùng I/O không đệm cho file lớn; một số ổ/box USB (vd box NVMe)
    // từ chối với lỗi 87 "The parameter is incorrect". Khi gặp đúng lỗi đó, chép lại bằng luồng có đệm thông thường.
    // Lỗi khác vẫn được ném nguyên vẹn.
    public static class SafeFileCopy
    {
        private const int ErrorInvalidParameter = 87;
        private const int BufferSize = 1 << 20; // 1 MB

        public static void Copy(string source, string dest, bool overwrite)
        {
            try
            {
                File.Copy(source, dest, overwrite);
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == ErrorInvalidParameter)
            {
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                CopyByStream(source, dest, overwrite);
            }
        }

        private static void CopyByStream(string source, string dest, bool overwrite)
        {
            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            using (var output = new FileStream(dest, mode, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan))
            {
                input.CopyTo(output, BufferSize);
            }
            try { File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(source)); } catch { }
        }
    }
}
