using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SRM_by_Longtygu.Helpers
{
    public static class IconExtractor
    {
        // QUAN TRỌNG: ExtractAssociatedIcon có thể GHI NGƯỢC đường dẫn đã resolve vào chính buffer
        // truyền vào (ví dụ khi icon nằm trong 1 file khác, hoặc icon index thay đổi).
        // Nếu dùng "string" (immutable) thì buffer marshal ra đúng bằng độ dài chuỗi, KHÔNG có
        // khoảng dư -> API ghi tràn ra ngoài buffer -> lỗi marshal ngầm, hàm coi như thất bại
        // (trả về IntPtr.Zero hoặc ném exception bị nuốt mất). Đây là lỗi rất phổ biến khiến
        // hàm này "im lặng fail" với TẤT CẢ các file, dù trước đó từng chạy "được" một cách tình cờ.
        // Fix: dùng StringBuilder với capacity cố định = MAX_PATH (260) để có buffer đủ lớn, an toàn.
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, StringBuilder iconPath, out ushort piIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static ImageSource GetIconFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                // Chuẩn hóa đường dẫn (VD: "C:\app.exe,0" hoặc "C:\app.ico" -> "C:\app.exe")
                string cleanPath = path.Trim('"', ' ');
                int commaIndex = cleanPath.LastIndexOf(',');
                if (commaIndex > 0) cleanPath = cleanPath.Substring(0, commaIndex);

                if (!System.IO.File.Exists(cleanPath))
                {
                    Debug.WriteLine($"[IconExtractor] File không tồn tại: {cleanPath}");
                    return null;
                }

                // Buffer 260 ký tự (MAX_PATH) để API có chỗ ghi ngược đường dẫn resolve nếu cần
                var pathBuilder = new StringBuilder(cleanPath, 260);
                ushort iconIndex = 0;
                IntPtr hIcon = ExtractAssociatedIcon(IntPtr.Zero, pathBuilder, out iconIndex);

                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        // Chuyển đổi Icon của Windows thành ImageSource của WPF
                        ImageSource imageSource = Imaging.CreateBitmapSourceFromHIcon(
                            hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        imageSource.Freeze(); // Đóng băng để hiển thị an toàn trên DataGrid
                        return imageSource;
                    }
                    finally
                    {
                        DestroyIcon(hIcon); // Dọn rác bộ nhớ ngay lập tức (đảm bảo luôn chạy)
                    }
                }
                else
                {
                    Debug.WriteLine($"[IconExtractor] ExtractAssociatedIcon trả về NULL cho: {cleanPath}");
                }
            }
            catch (Exception ex)
            {
                // Log ra Output window thay vì nuốt im lặng, để còn biết lý do thật sự nếu vẫn lỗi
                Debug.WriteLine($"[IconExtractor] Lỗi khi trích icon từ '{path}': {ex}");
            }

            return null; // Trả về null nếu app không có icon
        }
    }
}