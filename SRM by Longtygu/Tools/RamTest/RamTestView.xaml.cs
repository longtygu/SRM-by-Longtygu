using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SRM_by_Longtygu.Tools.RamTest
{
    public partial class RamTestView : UserControl
    {
        // Cấp phát theo từng khối 256MB thay vì 1 mảng byte[] khổng lồ,
        // để có thể test tới vài GB mà không vướng giới hạn object ~2GB của .NET.
        private const int ChunkSizeMb = 256;

        // Kiểm tra CancellationToken định kỳ theo số phần tử đã xử lý, tránh gọi quá thường xuyên làm chậm vòng lặp.
        private const long CancelCheckInterval = 100_000_000;

        private static readonly SolidColorBrush PendingBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
        private static readonly SolidColorBrush RunningBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
        private static readonly SolidColorBrush PassBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0xC9, 0x71));
        private static readonly SolidColorBrush FailBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23));

        private Ellipse[] _dots;
        private TextBlock[] _detailTexts;
        private TextBlock[] _durationTexts;

        private CancellationTokenSource _cts;

        public RamTestView()
        {
            InitializeComponent();

            _dots = new[] { Row1Dot, Row2Dot, Row3Dot, Row4Dot };
            _detailTexts = new[] { Row1Detail, Row2Detail, Row3Detail, Row4Detail };
            _durationTexts = new[] { Row1Duration, Row2Duration, Row3Duration, Row4Duration };
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            long sizeBytes = (long)GetSelectedSizeMb() * 1024 * 1024;

            StartButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            SizeComboBox.IsEnabled = false;

            ResetResults();
            LogText.Text = string.Empty;
            AppendLog($"Bắt đầu kiểm tra RAM với dung lượng {FormatSize(sizeBytes)}.");

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                await RunTestAsync(sizeBytes, token);
                SetStatus("Hoàn tất kiểm tra.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Đã hủy kiểm tra.");
                AppendLog("Người dùng đã hủy quá trình kiểm tra.");
            }
            catch (OutOfMemoryException)
            {
                SetStatus("Không đủ bộ nhớ trống để cấp phát dung lượng đã chọn.");
                AppendLog("LỖI: Không đủ bộ nhớ trống. Hãy thử chọn dung lượng nhỏ hơn.");
                SetStepFail(1, "Không đủ bộ nhớ", "");
            }
            catch (Exception ex)
            {
                SetStatus("Lỗi: " + ex.Message);
                AppendLog("LỖI: " + ex.Message);
            }
            finally
            {
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                SizeComboBox.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;

                // Giải phóng vùng nhớ test ngay sau khi xong, tránh chiếm RAM thật của người dùng
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            SetStatus("Đang hủy...");
        }

        private async Task RunTestAsync(long sizeBytes, CancellationToken token)
        {
            List<byte[]> chunks;
            uint crcAfterWrite;

            // ===== Bước 1: Allocate RAM =====
            SetStepRunning(1);
            var swAlloc = Stopwatch.StartNew();
            chunks = await Task.Run(() => AllocateChunks(sizeBytes, ChunkSizeMb, token), token);
            swAlloc.Stop();
            SetStepPass(1, $"Đã cấp phát {FormatSize(sizeBytes)}", FormatMs(swAlloc.ElapsedMilliseconds));
            AppendLog($"[Allocate RAM] Cấp phát thành công {FormatSize(sizeBytes)} ({chunks.Count} khối x {ChunkSizeMb}MB) trong {swAlloc.ElapsedMilliseconds} ms.");
            UpdateProgress(20);

            // ===== Bước 2: Write Pattern =====
            SetStepRunning(2);
            var swWrite = Stopwatch.StartNew();
            await Task.Run(() => WritePattern(chunks, token), token);
            swWrite.Stop();
            double writeSpeed = CalcSpeedMbPerSec(sizeBytes, swWrite.ElapsedMilliseconds);
            SetStepPass(2, $"{writeSpeed:0.#} MB/s", FormatMs(swWrite.ElapsedMilliseconds));
            AppendLog($"[Write Pattern] Ghi mẫu kiểm tra (0xAA XOR chỉ số byte) hoàn tất, tốc độ {writeSpeed:0.#} MB/s.");
            UpdateProgress(45);

            // Tính CRC32 ngay sau khi ghi để làm mốc đối chiếu (không hiển thị thành bước riêng, chỉ ghi log)
            var swCrcBaseline = Stopwatch.StartNew();
            crcAfterWrite = await Task.Run(() => Crc32Utility.Compute(chunks, token), token);
            swCrcBaseline.Stop();
            AppendLog($"[CRC Verify] Đã tính CRC32 mốc sau khi ghi: 0x{crcAfterWrite:X8} (trong {swCrcBaseline.ElapsedMilliseconds} ms).");
            UpdateProgress(60);

            // ===== Bước 3: Read Back =====
            SetStepRunning(3);
            var swRead = Stopwatch.StartNew();
            long checksumSum = await Task.Run(() => ReadBack(chunks, token), token);
            swRead.Stop();
            double readSpeed = CalcSpeedMbPerSec(sizeBytes, swRead.ElapsedMilliseconds);
            SetStepPass(3, $"{readSpeed:0.#} MB/s", FormatMs(swRead.ElapsedMilliseconds));
            AppendLog($"[Read Back] Đọc lại toàn bộ {FormatSize(sizeBytes)} hoàn tất, tốc độ {readSpeed:0.#} MB/s (tổng kiểm tra nội bộ: {checksumSum}).");
            UpdateProgress(85);

            // ===== Bước 4: CRC Verify (đối chiếu CRC sau khi đọc lại với CRC mốc) =====
            SetStepRunning(4);
            var swCrcFinal = Stopwatch.StartNew();
            uint crcAfterRead = await Task.Run(() => Crc32Utility.Compute(chunks, token), token);
            swCrcFinal.Stop();

            bool crcMatches = crcAfterRead == crcAfterWrite;
            long totalCrcMs = swCrcBaseline.ElapsedMilliseconds + swCrcFinal.ElapsedMilliseconds;

            if (crcMatches)
            {
                SetStepPass(4, $"Khớp (0x{crcAfterRead:X8})", FormatMs(totalCrcMs));
                AppendLog($"[CRC Verify] Đối chiếu thành công — CRC32 khớp hoàn toàn (0x{crcAfterRead:X8}). Bộ nhớ hoạt động chính xác, không phát hiện lỗi.");
            }
            else
            {
                SetStepFail(4, $"Lệch: 0x{crcAfterRead:X8} ≠ 0x{crcAfterWrite:X8}", FormatMs(totalCrcMs));
                AppendLog($"[CRC Verify] CẢNH BÁO: CRC32 KHÔNG khớp! Ghi: 0x{crcAfterWrite:X8}, Đọc: 0x{crcAfterRead:X8}. Có thể có lỗi bộ nhớ (bit-flip).");
            }

            UpdateProgress(100);
        }

        // ---------- Các thao tác xử lý bộ nhớ thực tế ----------

        private static List<byte[]> AllocateChunks(long totalBytes, int chunkSizeMb, CancellationToken token)
        {
            long chunkSizeBytes = (long)chunkSizeMb * 1024 * 1024;
            var chunks = new List<byte[]>();
            long remaining = totalBytes;

            while (remaining > 0)
            {
                token.ThrowIfCancellationRequested();
                int currentSize = (int)Math.Min(chunkSizeBytes, remaining);
                chunks.Add(new byte[currentSize]);
                remaining -= currentSize;
            }

            return chunks;
        }

        private static void WritePattern(List<byte[]> chunks, CancellationToken token)
        {
            long globalIndex = 0;
            long sinceCheck = 0;

            foreach (var chunk in chunks)
            {
                for (int i = 0; i < chunk.Length; i++)
                {
                    // Mẫu kiểm tra: XOR giữa hằng số 0xAA và byte thấp của chỉ số toàn cục,
                    // giúp phát hiện cả lỗi "dính giá trị cố định" lẫn lỗi theo vị trí địa chỉ.
                    chunk[i] = (byte)(0xAA ^ (globalIndex & 0xFF));

                    globalIndex++;
                    sinceCheck++;
                    if (sinceCheck >= CancelCheckInterval)
                    {
                        token.ThrowIfCancellationRequested();
                        sinceCheck = 0;
                    }
                }
            }
        }

        private static long ReadBack(List<byte[]> chunks, CancellationToken token)
        {
            long sum = 0;
            long sinceCheck = 0;

            foreach (var chunk in chunks)
            {
                for (int i = 0; i < chunk.Length; i++)
                {
                    sum += chunk[i];

                    sinceCheck++;
                    if (sinceCheck >= CancelCheckInterval)
                    {
                        token.ThrowIfCancellationRequested();
                        sinceCheck = 0;
                    }
                }
            }

            return sum;
        }

        // ---------- UI helpers ----------

        private int GetSelectedSizeMb()
        {
            if (SizeComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var mb))
            {
                return mb;
            }
            return 512;
        }

        private void ResetResults()
        {
            OverallProgressBar.Value = 0;
            for (int i = 0; i < 4; i++)
            {
                _dots[i].Fill = PendingBrush;
                _detailTexts[i].Text = "Đang chờ";
                _durationTexts[i].Text = "";
            }
            SetStatus("Đang chuẩn bị...");
        }

        private void SetStepRunning(int row)
        {
            int idx = row - 1;
            _dots[idx].Fill = RunningBrush;
            _detailTexts[idx].Text = "Đang chạy...";
        }

        private void SetStepPass(int row, string detail, string duration)
        {
            int idx = row - 1;
            _dots[idx].Fill = PassBrush;
            _detailTexts[idx].Text = detail;
            _durationTexts[idx].Text = duration;
        }

        private void SetStepFail(int row, string detail, string duration)
        {
            int idx = row - 1;
            _dots[idx].Fill = FailBrush;
            _detailTexts[idx].Text = detail;
            _durationTexts[idx].Text = duration;
        }

        private void SetStatus(string text)
        {
            StatusText.Text = text;
        }

        private void UpdateProgress(double percent)
        {
            OverallProgressBar.Value = percent;
        }

        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogText.Text += $"[{timestamp}] {message}\n";
            LogScrollViewer.ScrollToEnd();
        }

        private static double CalcSpeedMbPerSec(long bytes, long elapsedMs)
        {
            double seconds = Math.Max(elapsedMs, 1) / 1000.0;
            return (bytes / 1024.0 / 1024.0) / seconds;
        }

        private static string FormatMs(long ms) => $"{ms} ms";

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.##") + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.##") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.##") + " KB";
            return bytes + " Bytes";
        }
    }
}
