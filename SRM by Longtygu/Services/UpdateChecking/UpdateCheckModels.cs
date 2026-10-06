namespace SRM_by_Longtygu.Services.UpdateChecking
{
    // Kết quả trả về sau khi 1 IUpdateSource kiểm tra xong cho 1 phần mềm
    public class UpdateCheckResult
    {
        public bool Success { get; set; }

        // Số phiên bản mới nhất tìm được (đã chuẩn hoá, không có tiền tố "v")
        public string LatestVersion { get; set; }

        // Link tải trực tiếp nếu nguồn cung cấp được (null nếu không có / cần người dùng tự tải)
        public string DownloadUrl { get; set; }

        // Lý do thất bại (hiển thị cho người dùng / ghi log), null nếu Success = true
        public string ErrorMessage { get; set; }
    }

    // MỚI: Gói thông tin tiến trình tải đầy đủ - báo cáo định kỳ (~200ms/lần) trong lúc tải file,
    // dùng cho progress bar hiển thị số MB đã tải / tổng MB và tốc độ tải thời gian thực.
    public class DownloadProgressInfo
    {
        // -1 nếu không xác định được % (server không trả Content-Length)
        public int PercentComplete { get; set; }

        public long BytesDownloaded { get; set; }

        // 0 nếu không xác định được tổng dung lượng
        public long TotalBytes { get; set; }

        // Tốc độ tải tức thời (byte/giây), tính theo khoảng thời gian kể từ lần báo cáo trước
        public double SpeedBytesPerSecond { get; set; }
    }
}
