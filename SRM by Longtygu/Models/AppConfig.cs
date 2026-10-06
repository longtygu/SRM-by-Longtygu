namespace SRM_by_Longtygu.Models
{
    public class AppConfig
    {
        public bool IsDarkMode { get; set; } = true;
        public bool AutoCreateRestorePoint { get; set; } = true;
        public string DefaultDataFolder { get; set; } = "Data\\";
        public string DefaultBackupFolder { get; set; } = "Backup\\";
        public string Language { get; set; } = "vi-VN";

        // MỚI: Đã xem hướng dẫn Trung tâm cập nhật lần đầu chưa - dùng để tự động hiện popup
        // hướng dẫn đúng 1 lần duy nhất khi người dùng mở trang này lần đầu tiên.
        public bool HasSeenUpdateCenterGuide { get; set; } = false;
    }
}
