using System;

namespace SRM_by_Longtygu.Models
{
    /// <summary>
    /// Đại diện cho một mục nhật ký hoạt động, dùng để hiển thị trong màn hình Log Viewer.
    /// </summary>
    public class LogEntryModel
    {
        public DateTime Timestamp { get; set; }

        /// <summary>INFO, WARNING hoặc ERROR</summary>
        public string Level { get; set; }

        public string Message { get; set; }

        /// <summary>Chi tiết Exception/StackTrace nếu có (rỗng nếu không có lỗi)</summary>
        public string Detail { get; set; } = string.Empty;

        public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

        public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
    }
}
