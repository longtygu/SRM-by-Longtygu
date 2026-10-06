using System;
using System.IO;
using System.Text;

namespace SRM_by_Longtygu.Helpers
{
    public static class InstallerAnalyzer
    {
        // Phân tích mã nhị phân để trích xuất lệnh Silent chuẩn
        public static (string InstallCmd, string UninstallCmd) Analyze(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return ("", "");

            string ext = Path.GetExtension(filePath)?.ToLower();

            // 1. Phân tích file MSI (Microsoft Standard Installer)
            // Nguồn: Tài liệu kỹ thuật Microsoft Windows Installer Command-Line Options
            if (ext == ".msi")
                return ("/i /qn /norestart", "/x /qn /norestart");

            // 2. Phân tích file EXE (Quét 2MB đầu tiên của file để tìm chữ ký đóng gói)
            if (ext == ".exe")
            {
                try
                {
                    byte[] buffer = new byte[2048 * 1024]; // Quét 2MB dữ liệu đầu tiên
                    using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int bytesRead = fs.Read(buffer, 0, buffer.Length);
                        string content = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                        // Nguồn: Inno Setup Knowledge Base
                        if (content.Contains("Inno Setup"))
                            return ("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", "/VERYSILENT");

                        // Nguồn: Nullsoft Scriptable Install System (NSIS) Users Manual
                        if (content.Contains("Nullsoft") || content.Contains("NSIS"))
                            return ("/S", "/S");

                        // Nguồn: WiX Toolset Documentation
                        if (content.Contains("WiX") || content.Contains("Wix"))
                            return ("-quiet -norestart", "-quiet -uninstall");

                        // Nguồn: InstallShield Command-Line Parameters
                        if (content.Contains("InstallShield"))
                            return ("/s /v\"/qn\"", "");
                    }
                }
                catch { /* Bỏ qua nếu file bị khóa bảo mật */ }

                // Trả về lệnh dự phòng nếu ko xác định dc chữ ký
                return ("/S", "");
            }

            return ("", "");
        }
    }
}