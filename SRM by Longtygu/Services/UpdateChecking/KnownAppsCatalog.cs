using System;
using System.Collections.Generic;
using System.Linq;

namespace SRM_by_Longtygu.Services.UpdateChecking
{
    // Cấu hình gợi ý sẵn cho 1 phần mềm đã biết trước
    public class KnownAppEntry
    {
        // Các từ khóa để nhận diện tên phần mềm (không phân biệt hoa/thường, chỉ cần chứa 1 trong các từ này)
        public string[] NameKeywords { get; set; } = Array.Empty<string>();

        public string UpdateSourceType { get; set; } // "GitHubReleases" | "Winget"
        public string UpdateSourceUrl { get; set; }   // owner/repo (GitHub) hoặc Package Id (Winget)
    }

    // MỚI: Catalog gợi ý sẵn cấu hình cho các phần mềm phổ biến, để người dùng không phải tự tra
    // GitHub repo / Winget ID cho từng app một. Đây là danh sách "khởi đầu" — có thể tự bổ sung thêm
    // bằng cách thêm phần tử mới vào mảng bên dưới, không cần sửa gì khác trong app.
    //
    // Ưu tiên Winget trước (phủ được nhiều app closed-source hơn), GitHub cho app mã nguồn mở nổi tiếng.
    public static class KnownAppsCatalog
    {
        private static readonly List<KnownAppEntry> Entries = new List<KnownAppEntry>
        {
            // ===== Đa phương tiện =====
            new KnownAppEntry { NameKeywords = new[] { "vlc" }, UpdateSourceType = "GitHubReleases", UpdateSourceUrl = "videolan/vlc" },
            new KnownAppEntry { NameKeywords = new[] { "obs studio", "obs-studio", "obs" }, UpdateSourceType = "GitHubReleases", UpdateSourceUrl = "obsproject/obs-studio" },
            new KnownAppEntry { NameKeywords = new[] { "audacity" }, UpdateSourceType = "GitHubReleases", UpdateSourceUrl = "audacity/audacity" },
            new KnownAppEntry { NameKeywords = new[] { "handbrake" }, UpdateSourceType = "GitHubReleases", UpdateSourceUrl = "HandBrake/HandBrake" },

            // ===== Trình duyệt =====
            new KnownAppEntry { NameKeywords = new[] { "chrome", "google chrome" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Google.Chrome" },
            new KnownAppEntry { NameKeywords = new[] { "firefox" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Mozilla.Firefox" },
            new KnownAppEntry { NameKeywords = new[] { "cốc cốc", "coc coc" }, UpdateSourceType = "Winget", UpdateSourceUrl = "CocCoc.CocCocBrowser" },
            new KnownAppEntry { NameKeywords = new[] { "microsoft edge", "edge" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Microsoft.Edge" },

            // ===== Nén / giải nén =====
            new KnownAppEntry { NameKeywords = new[] { "7-zip", "7zip" }, UpdateSourceType = "Winget", UpdateSourceUrl = "7zip.7zip" },
            new KnownAppEntry { NameKeywords = new[] { "winrar" }, UpdateSourceType = "Winget", UpdateSourceUrl = "RARLab.WinRAR" },

            // ===== Chỉnh sửa / lập trình =====
            new KnownAppEntry { NameKeywords = new[] { "notepad++", "notepad plus plus" }, UpdateSourceType = "GitHubReleases", UpdateSourceUrl = "notepad-plus-plus/notepad-plus-plus" },
            new KnownAppEntry { NameKeywords = new[] { "visual studio code", "vscode", "vs code" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Microsoft.VisualStudioCode" },
            new KnownAppEntry { NameKeywords = new[] { "gimp" }, UpdateSourceType = "Winget", UpdateSourceUrl = "GIMP.GIMP" },
            new KnownAppEntry { NameKeywords = new[] { "inkscape" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Inkscape.Inkscape" },
            new KnownAppEntry { NameKeywords = new[] { "blender" }, UpdateSourceType = "Winget", UpdateSourceUrl = "BlenderFoundation.Blender" },

            // ===== Giao tiếp / hội họp =====
            new KnownAppEntry { NameKeywords = new[] { "zalo" }, UpdateSourceType = "Winget", UpdateSourceUrl = "VNGCorp.Zalo" },
            new KnownAppEntry { NameKeywords = new[] { "discord" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Discord.Discord" },
            new KnownAppEntry { NameKeywords = new[] { "zoom" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Zoom.Zoom" },
            new KnownAppEntry { NameKeywords = new[] { "skype" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Microsoft.Skype" },
            new KnownAppEntry { NameKeywords = new[] { "telegram" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Telegram.TelegramDesktop" },
            new KnownAppEntry { NameKeywords = new[] { "ultraviewer" }, UpdateSourceType = "Winget", UpdateSourceUrl = "UltraViewer.UltraViewer" },
            new KnownAppEntry { NameKeywords = new[] { "teamviewer" }, UpdateSourceType = "Winget", UpdateSourceUrl = "TeamViewer.TeamViewer" },
            new KnownAppEntry { NameKeywords = new[] { "anydesk" }, UpdateSourceType = "Winget", UpdateSourceUrl = "AnyDeskSoftwareGmbH.AnyDesk" },

            // ===== Tiện ích hệ thống =====
            new KnownAppEntry { NameKeywords = new[] { "everything" }, UpdateSourceType = "Winget", UpdateSourceUrl = "voidtools.Everything" },
            new KnownAppEntry { NameKeywords = new[] { "cpu-z", "cpuz" }, UpdateSourceType = "Winget", UpdateSourceUrl = "CPUID.CPU-Z" },
            new KnownAppEntry { NameKeywords = new[] { "hwmonitor" }, UpdateSourceType = "Winget", UpdateSourceUrl = "CPUID.HWMonitor" },
            new KnownAppEntry { NameKeywords = new[] { "rufus" }, UpdateSourceType = "GitHubReleases", UpdateSourceUrl = "pbatard/rufus" },
            new KnownAppEntry { NameKeywords = new[] { "balenaetcher", "etcher" }, UpdateSourceType = "GitHubReleases", UpdateSourceUrl = "balena-io/etcher" },
            new KnownAppEntry { NameKeywords = new[] { "qbittorrent" }, UpdateSourceType = "Winget", UpdateSourceUrl = "qBittorrent.qBittorrent" },
            new KnownAppEntry { NameKeywords = new[] { "steam" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Valve.Steam" },
            new KnownAppEntry { NameKeywords = new[] { "adobe reader", "acrobat reader" }, UpdateSourceType = "Winget", UpdateSourceUrl = "Adobe.Acrobat.Reader.64-bit" },
        };

        // Tìm cấu hình gợi ý dựa theo tên phần mềm (so khớp gần đúng, không phân biệt hoa/thường)
        public static bool TryFindMatch(string softwareName, out KnownAppEntry match)
        {
            match = null;
            if (string.IsNullOrWhiteSpace(softwareName)) return false;

            string normalized = softwareName.Trim().ToLowerInvariant();

            match = Entries.FirstOrDefault(entry =>
                entry.NameKeywords.Any(keyword => normalized.Contains(keyword.ToLowerInvariant())));

            return match != null;
        }
    }
}
