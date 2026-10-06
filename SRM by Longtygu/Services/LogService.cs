using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Services
{
    public interface ILogService
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception ex = null);

        // ====== Bổ sung cho màn hình xem Nhật ký hoạt động ======
        string LogFolderPath { get; }
        List<DateTime> GetAvailableLogDates();
        List<LogEntryModel> ReadLogs(DateTime date);
        void OpenLogFolder();
    }

    public class LogService : ILogService
    {
        private readonly string _logFolder;
        private static readonly object _lockObj = new object();

        public LogService()
        {
            _logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(_logFolder))
            {
                Directory.CreateDirectory(_logFolder);
            }
        }

        private void WriteLog(string level, string message, Exception ex = null)
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd}-install.log";
            string filePath = Path.Combine(_logFolder, fileName);
            string timeStamp = DateTime.Now.ToString("HH:mm:ss");

            string logEntry = $"[{timeStamp}] [{level}] {message}";
            if (ex != null)
            {
                logEntry += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";
            }

            lock (_lockObj) // Đảm bảo an toàn khi ghi log từ nhiều thread (Bulk Install)
            {
                File.AppendAllText(filePath, logEntry + Environment.NewLine);
            }
        }

        public void LogInfo(string message) => WriteLog("INFO", message);
        public void LogWarning(string message) => WriteLog("WARNING", message);
        public void LogError(string message, Exception ex = null) => WriteLog("ERROR", message, ex);

        // ====================================================================
        // ĐỌC LẠI LOG CHO MÀN HÌNH "NHẬT KÝ HOẠT ĐỘNG"
        // ====================================================================

        public string LogFolderPath => _logFolder;

        // Regex khớp với định dạng do WriteLog() sinh ra: [HH:mm:ss] [LEVEL] message
        private static readonly Regex LogLineRegex =
            new Regex(@"^\[(\d{2}:\d{2}:\d{2})\]\s\[(INFO|WARNING|ERROR)\]\s(.*)$", RegexOptions.Compiled);

        public List<DateTime> GetAvailableLogDates()
        {
            var result = new List<DateTime>();
            if (!Directory.Exists(_logFolder))
                return result;

            foreach (var file in Directory.GetFiles(_logFolder, "*-install.log"))
            {
                string nameOnly = Path.GetFileNameWithoutExtension(file); // yyyy-MM-dd-install
                string datePart = nameOnly.Substring(0, nameOnly.Length - "-install".Length);

                if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null,
                        System.Globalization.DateTimeStyles.None, out var date))
                {
                    result.Add(date);
                }
            }

            return result.OrderByDescending(d => d).ToList();
        }

        public List<LogEntryModel> ReadLogs(DateTime date)
        {
            var result = new List<LogEntryModel>();
            string fileName = $"{date:yyyy-MM-dd}-install.log";
            string filePath = Path.Combine(_logFolder, fileName);

            if (!File.Exists(filePath))
                return result;

            string[] lines;
            lock (_lockObj)
            {
                // Cho phép đọc file dù đang có tiến trình khác ghi vào (FileShare.ReadWrite)
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    lines = reader.ReadToEnd().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                }
            }

            LogEntryModel current = null;
            foreach (var line in lines)
            {
                var match = LogLineRegex.Match(line);
                if (match.Success)
                {
                    if (current != null) result.Add(current);

                    DateTime.TryParseExact(
                        $"{date:yyyy-MM-dd} {match.Groups[1].Value}",
                        "yyyy-MM-dd HH:mm:ss", null,
                        System.Globalization.DateTimeStyles.None, out var timestamp);

                    current = new LogEntryModel
                    {
                        Timestamp = timestamp,
                        Level = match.Groups[2].Value,
                        Message = match.Groups[3].Value,
                        Detail = string.Empty
                    };
                }
                else if (current != null && !string.IsNullOrWhiteSpace(line))
                {
                    // Dòng tiếp theo (Exception / StackTrace) thuộc về entry log trước đó
                    current.Detail += (current.Detail.Length > 0 ? "\n" : "") + line;
                }
            }
            if (current != null) result.Add(current);

            return result.OrderByDescending(e => e.Timestamp).ToList();
        }

        public void OpenLogFolder()
        {
            if (!Directory.Exists(_logFolder))
                Directory.CreateDirectory(_logFolder);

            Process.Start(new ProcessStartInfo
            {
                FileName = _logFolder,
                UseShellExecute = true,
                Verb = "open"
            });
        }
    }
}