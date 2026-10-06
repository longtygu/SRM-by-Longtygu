using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SRM_by_Longtygu.Tools.HardwareInfo
{
    public partial class HardwareInfoView : UserControl
    {
        private Computer _computer;
        private readonly HardwareUpdateVisitor _updateVisitor = new HardwareUpdateVisitor();
        private DispatcherTimer _timer;

        private const int MaxHistoryPoints = 60;

        // Mỗi linh kiện (CPU/GPU/RAM/từng ổ đĩa) có 1 đường màu riêng trên biểu đồ Tải % real-time
        private class ChartSeries
        {
            public string Label;
            public Brush Color;
            public ISensor Sensor;
            public List<double> History = new List<double>();
        }

        private readonly List<ChartSeries> _chartSeries = new List<ChartSeries>();

        private static readonly Brush[] SeriesPalette =
        {
            new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)), // xanh dương
            new SolidColorBrush(Color.FromRgb(0xE8, 0xA2, 0x3D)), // cam
            new SolidColorBrush(Color.FromRgb(0x3D, 0xC9, 0x71)), // xanh lá
            new SolidColorBrush(Color.FromRgb(0xC7, 0x7D, 0xFF)), // tím
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x9D)), // hồng
            new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x66)), // vàng
        };

        // Danh sách (cảm biến, ô hiển thị) cần cập nhật mỗi tick — dùng chung cho mọi loại phần cứng,
        // tránh phải dò tên từng loại sensor thủ công như trước (đây cũng là nguồn gốc gây lỗi CPU trống dữ liệu).
        private readonly List<(ISensor Sensor, TextBlock Target)> _liveBindings = new List<(ISensor, TextBlock)>();

        private List<SystemInfoRow> _overviewRows = new List<SystemInfoRow>();

        // Định dạng số kiểu Việt Nam cho tab SMART: dấu chấm phân cách hàng nghìn, dấu phẩy phân cách thập phân
        // (VD: 13167 -> "13.167") theo đúng yêu cầu, khác với phần còn lại của app dùng dấu chấm thập phân.
        private static readonly NumberFormatInfo VnNumberFormat = new NumberFormatInfo
        {
            NumberGroupSeparator = ".",
            NumberDecimalSeparator = ","
        };

        private static readonly SolidColorBrush BrushNormal = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
        private static readonly SolidColorBrush BrushWarn = new SolidColorBrush(Color.FromRgb(0xE8, 0xA2, 0x3D));
        private static readonly SolidColorBrush BrushDanger = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23));
        private static readonly SolidColorBrush BrushMuted = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));

        public HardwareInfoView()
        {
            InitializeComponent();
            Loaded += HardwareInfoView_Loaded;
            Unloaded += HardwareInfoView_Unloaded;
        }

        private void HardwareInfoView_Loaded(object sender, RoutedEventArgs e)
        {
            SetActiveTab(TabOverviewButton, OverviewPanel);

            bool isAdmin = IsRunningAsAdministrator();
            if (isAdmin)
            {
                AdminHintText.Text = "";
                RestartAsAdminButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                AdminHintText.Text = "⚠ Đang chạy không có quyền Administrator — nhiệt độ và một số dữ liệu SMART có thể hiển thị N/A.";
                RestartAsAdminButton.Visibility = Visibility.Visible;
            }

            LoadStaticOverviewInfo();
            InitializeHardwareMonitor();
            BuildMonitorCards();
            BuildChartSeries();
            LoadDriveList();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Cập nhật ngay lần đầu, không cần đợi tick đầu tiên của timer
            Timer_Tick(null, EventArgs.Empty);
        }

        private void HardwareInfoView_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            _timer = null;

            try { _computer?.Close(); } catch { /* bỏ qua, chỉ dọn tài nguyên */ }
            _computer = null;
        }

        private static bool IsRunningAsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        // Khởi động lại toàn bộ ứng dụng với quyền Administrator, rồi đóng phiên hiện tại.
        private void RestartAsAdminButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Ứng dụng sẽ khởi động lại với quyền Administrator để đọc đầy đủ nhiệt độ và dữ liệu SMART.\n\n" +
                "Bạn có muốn tiếp tục không?",
                "Chạy lại với quyền Admin",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(startInfo);
                Application.Current.Shutdown();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Người dùng bấm "No" trên hộp thoại UAC — bỏ qua, giữ nguyên phiên hiện tại
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể khởi động lại với quyền Admin:\n{ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- Điều hướng tab (thuần code-behind, không cần ViewModel) ----------

        private void TabOverview_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabOverviewButton, OverviewPanel);
        private void TabMonitor_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabMonitorButton, MonitorPanel);
        private void TabSmart_Click(object sender, RoutedEventArgs e) => SetActiveTab(TabSmartButton, SmartPanel);

        private void SetActiveTab(Button activeButton, FrameworkElement panelToShow)
        {
            foreach (var btn in new[] { TabOverviewButton, TabMonitorButton, TabSmartButton })
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = BrushMuted;
            }
            activeButton.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            activeButton.Foreground = Brushes.White;

            OverviewPanel.Visibility = Visibility.Collapsed;
            MonitorPanel.Visibility = Visibility.Collapsed;
            SmartPanel.Visibility = Visibility.Collapsed;

            panelToShow.Visibility = Visibility.Visible;
        }

        // ---------- Tổng quan: thông tin tĩnh đọc qua WMI/Registry, hiển thị dạng bảng dọc ----------

        private void LoadStaticOverviewInfo()
        {
            OverviewRowsPanel.Children.Clear();
            _overviewRows = SystemStaticInfoProvider.GetOverviewRows();

            foreach (var row in _overviewRows)
            {
                OverviewRowsPanel.Children.Add(BuildOverviewRow(row.Label, row.Value));
            }
        }

        private Border BuildOverviewRow(string label, string value)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 14, 0, 14)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                VerticalAlignment = VerticalAlignment.Top
            };
            var valueText = new TextBlock
            {
                Text = string.IsNullOrEmpty(value) ? "—" : value,
                FontSize = 14.5,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };

            Grid.SetColumn(labelText, 0);
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(labelText);
            grid.Children.Add(valueText);

            border.Child = grid;
            return border;
        }

        // Xuất toàn bộ thông tin tab "Tổng quan" ra file .txt — dùng đúng dữ liệu đang hiển thị trên UI
        private void ExportOverviewButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Xuất thông tin hệ thống",
                Filter = "Text file (*.txt)|*.txt",
                FileName = $"ThongTinHeThong_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("THÔNG TIN HỆ THỐNG");
                sb.AppendLine("Xuất lúc: " + DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss"));
                sb.AppendLine(new string('-', 50));

                int maxLabelLength = _overviewRows.Count > 0 ? _overviewRows.Max(r => r.Label.Length) : 0;

                foreach (var row in _overviewRows)
                {
                    sb.AppendLine($"{row.Label.PadRight(maxLabelLength)} : {row.Value}");
                }

                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                MessageBox.Show("Đã xuất file thành công.", "Xuất file TXT",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể xuất file:\n{ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- Khởi tạo LibreHardwareMonitor ----------

        private void InitializeHardwareMonitor()
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = true,
                    IsNetworkEnabled = false,
                    IsControllerEnabled = false,
                    IsPsuEnabled = false
                };
                _computer.Open();
                _computer.Accept(_updateVisitor);
            }
            catch (Exception ex)
            {
                AdminHintText.Text = "Không thể khởi tạo module giám sát phần cứng: " + ex.Message;
                _computer = null;
            }
        }

        private static bool IsGpu(IHardware hw) =>
            hw.HardwareType.ToString().StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);

        // ---------- Xây dựng card giám sát cho từng phần cứng (CPU / GPU / RAM / Ổ đĩa) ----------

        private void BuildMonitorCards()
        {
            MonitorCardsPanel.Children.Clear();
            _liveBindings.Clear();

            if (_computer == null)
            {
                MonitorCardsPanel.Children.Add(new TextBlock
                {
                    Text = "Không thể khởi tạo module giám sát phần cứng.",
                    Foreground = BrushMuted,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
                MonitorCardsPanel.Children.Add(BuildHardwareCard(hw, "CPU"));

            foreach (var hw in _computer.Hardware.Where(IsGpu))
                MonitorCardsPanel.Children.Add(BuildHardwareCard(hw, "GPU"));

            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Memory))
                MonitorCardsPanel.Children.Add(BuildHardwareCard(hw, "RAM"));

            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage))
                MonitorCardsPanel.Children.Add(BuildHardwareCard(hw, "Ổ đĩa"));

            if (MonitorCardsPanel.Children.Count == 0)
            {
                MonitorCardsPanel.Children.Add(new TextBlock
                {
                    Text = "Không phát hiện được phần cứng nào để giám sát.",
                    Foreground = BrushMuted,
                    FontSize = 14
                });
            }
        }

        // ---------- Xây dựng danh sách series cho biểu đồ Tải % real-time nhiều màu ----------

        private void BuildChartSeries()
        {
            _chartSeries.Clear();
            ChartLegendPanel.Children.Clear();

            if (_computer == null) return;

            int colorIndex = 0;

            void TryAddSeries(IHardware hw, string label)
            {
                // Ưu tiên tuyệt đối cảm biến Load (Tải %) vì hầu như luôn đọc được kể cả không có quyền Admin,
                // khác với Nhiệt độ/Xung nhịp/Công suất vốn cần driver MSR cấp thấp.
                var loadSensor = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);
                if (loadSensor == null) return;

                var color = SeriesPalette[colorIndex % SeriesPalette.Length];
                colorIndex++;

                _chartSeries.Add(new ChartSeries { Label = label, Color = color, Sensor = loadSensor });
                AddLegendItem(label, color);
            }

            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
                TryAddSeries(hw, "CPU");

            foreach (var hw in _computer.Hardware.Where(IsGpu))
                TryAddSeries(hw, "GPU");

            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Memory))
                TryAddSeries(hw, "RAM");

            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage))
                TryAddSeries(hw, ShortenDriveName(hw.Name));

            if (_chartSeries.Count == 0)
            {
                ChartLegendPanel.Children.Add(new TextBlock
                {
                    Text = "Không có cảm biến Tải (%) nào khả dụng để vẽ biểu đồ.",
                    FontSize = 12,
                    Foreground = BrushMuted,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
        }

        private void AddLegendItem(string label, Brush color)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = color,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            var text = new TextBlock { Text = label, FontSize = 12, Foreground = BrushMuted, VerticalAlignment = VerticalAlignment.Center };

            item.Children.Add(dot);
            item.Children.Add(text);
            ChartLegendPanel.Children.Add(item);
        }

        private static string ShortenDriveName(string name)
        {
            return name.Length > 22 ? name.Substring(0, 20) + "…" : name;
        }

        private Border BuildHardwareCard(IHardware hw, string categoryLabel)
        {
            var outer = new Border { Style = (Style)FindResource("MonitorCardStyle") };
            var stack = new StackPanel();

            // ----- Header: tên phần cứng + nhãn loại -----
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = hw.Name,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            var categoryBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x4A, 0x63)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            categoryBadge.Child = new TextBlock { Text = categoryLabel, FontSize = 11.5, Foreground = Brushes.White };

            Grid.SetColumn(nameText, 0);
            Grid.SetColumn(categoryBadge, 1);
            headerGrid.Children.Add(nameText);
            headerGrid.Children.Add(categoryBadge);
            stack.Children.Add(headerGrid);

            // ----- Chip nổi bật: đại diện mỗi loại cảm biến quan trọng, font to -----
            var primaryTypes = new[]
            {
                SensorType.Load, SensorType.Temperature, SensorType.Clock,
                SensorType.Power, SensorType.Fan, SensorType.Level
            };

            var chipsPanel = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            bool anyChip = false;

            foreach (var type in primaryTypes)
            {
                var sensor = hw.Sensors.FirstOrDefault(s => s.SensorType == type);
                if (sensor == null) continue;

                anyChip = true;
                var chip = BuildChip(sensor, out var valueText);
                chipsPanel.Children.Add(chip);
                _liveBindings.Add((sensor, valueText));
            }

            if (anyChip) stack.Children.Add(chipsPanel);

            // ----- Khu vực mở rộng: TOÀN BỘ cảm biến của phần cứng này -----
            if (hw.Sensors.Length > 0)
            {
                var detailPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

                foreach (var sensor in hw.Sensors.OrderBy(s => s.SensorType.ToString()).ThenBy(s => s.Name))
                {
                    var row = BuildDetailRow(sensor, out var valueText);
                    detailPanel.Children.Add(row);
                    _liveBindings.Add((sensor, valueText));
                }

                var section = BuildExpandableSection($"Xem tất cả cảm biến ({hw.Sensors.Length})", detailPanel);
                stack.Children.Add(section);
            }

            outer.Child = stack;
            return outer;
        }

        private Border BuildChip(ISensor sensor, out TextBlock valueText)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 10, 10),
                MinWidth = 120
            };

            var innerStack = new StackPanel();
            innerStack.Children.Add(new TextBlock
            {
                Text = GetSensorTypeLabel(sensor.SensorType),
                FontSize = 12.5,
                Foreground = BrushMuted
            });

            valueText = new TextBlock
            {
                Text = FormatSensorValue(sensor),
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Foreground = BrushNormal,
                Margin = new Thickness(0, 3, 0, 0)
            };
            innerStack.Children.Add(valueText);

            chip.Child = innerStack;
            return chip;
        }

        private static Grid BuildDetailRow(ISensor sensor, out TextBlock valueText)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = sensor.Name,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0))
            };
            valueText = new TextBlock
            {
                Text = FormatSensorValue(sensor),
                FontSize = 14,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };

            Grid.SetColumn(nameText, 0);
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(nameText);
            grid.Children.Add(valueText);
            return grid;
        }

        // Khu vực thu gọn/mở rộng tự viết (không dùng Expander mặc định của WPF để tránh lệch tông màu sáng/tối)
        private StackPanel BuildExpandableSection(string headerLabel, UIElement content)
        {
            var container = new StackPanel();

            var headerBorder = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0, 6, 0, 6)
            };
            var headerText = new TextBlock
            {
                Text = "▸ " + headerLabel,
                Foreground = BrushMuted,
                FontSize = 13.5
            };
            headerBorder.Child = headerText;

            content.Visibility = Visibility.Collapsed;

            headerBorder.MouseLeftButtonUp += (s, e) =>
            {
                bool expand = content.Visibility != Visibility.Visible;
                content.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
                headerText.Text = (expand ? "▾ " : "▸ ") + headerLabel;
            };

            container.Children.Add(headerBorder);
            container.Children.Add(content);
            return container;
        }

        // ---------- Cập nhật realtime (chạy mỗi 1.5s qua DispatcherTimer) ----------

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_computer == null) return;

            try
            {
                _computer.Accept(_updateVisitor);
            }
            catch
            {
                return;
            }

            foreach (var (sensor, target) in _liveBindings)
            {
                target.Text = FormatSensorValue(sensor);

                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                {
                    double t = sensor.Value.Value;
                    target.Foreground = t >= 85 ? BrushDanger : t >= 70 ? BrushWarn : BrushNormal;
                }
            }

            // Đẩy giá trị Tải % mới nhất vào từng series rồi vẽ lại toàn bộ biểu đồ
            foreach (var series in _chartSeries)
            {
                double value = series.Sensor.Value ?? 0;
                series.History.Add(value);
                if (series.History.Count > MaxHistoryPoints)
                {
                    series.History.RemoveAt(0);
                }
            }

            DrawRealtimeChart();
        }

        // ---------- Biểu đồ Tải % real-time nhiều màu (vẽ tay bằng Polyline, không cần thư viện chart) ----------

        private void DrawRealtimeChart()
        {
            RealtimeChartCanvas.Children.Clear();
            if (_chartSeries.Count == 0) return;

            double width = RealtimeChartCanvas.ActualWidth > 0 ? RealtimeChartCanvas.ActualWidth : 500;
            double height = RealtimeChartCanvas.ActualHeight > 0 ? RealtimeChartCanvas.ActualHeight : 120;

            // Lưới tham chiếu 25% / 50% / 75%
            foreach (var pct in new[] { 0.25, 0.5, 0.75 })
            {
                var gridLine = new Line
                {
                    X1 = 0,
                    X2 = width,
                    Y1 = height * (1 - pct),
                    Y2 = height * (1 - pct),
                    Stroke = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                    StrokeThickness = 1
                };
                RealtimeChartCanvas.Children.Add(gridLine);
            }

            foreach (var series in _chartSeries)
            {
                if (series.History.Count < 2) continue;

                var points = new PointCollection();
                for (int i = 0; i < series.History.Count; i++)
                {
                    double x = (i / (double)(MaxHistoryPoints - 1)) * width;
                    double clamped = Math.Max(0, Math.Min(100, series.History[i]));
                    double y = height - (clamped / 100.0) * height;
                    points.Add(new Point(x, y));
                }

                var polyline = new Polyline
                {
                    Points = points,
                    Stroke = series.Color,
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round
                };

                RealtimeChartCanvas.Children.Add(polyline);
            }
        }

        // ---------- S.M.A.R.T ổ đĩa ----------

        private void LoadDriveList()
        {
            DriveSelectorComboBox.Items.Clear();
            SmartRowsPanel.Children.Clear();

            if (_computer == null) return;

            foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage))
            {
                DriveSelectorComboBox.Items.Add(hw.Name);
            }

            if (DriveSelectorComboBox.Items.Count > 0)
            {
                DriveSelectorComboBox.SelectedIndex = 0;
            }
            else
            {
                SmartRowsPanel.Children.Add(new TextBlock
                {
                    Text = "Không phát hiện được ổ đĩa nào để đọc dữ liệu SMART.",
                    Foreground = BrushMuted,
                    FontSize = 14
                });
            }
        }

        private void DriveSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RenderSmartForSelectedDrive();
        }

        private void RenderSmartForSelectedDrive()
        {
            SmartRowsPanel.Children.Clear();

            if (_computer == null || DriveSelectorComboBox.SelectedItem == null) return;

            var selectedName = DriveSelectorComboBox.SelectedItem.ToString();
            var hw = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Storage && h.Name == selectedName);
            if (hw == null) return;

            hw.Update();

            if (hw.Sensors.Length == 0)
            {
                SmartRowsPanel.Children.Add(new TextBlock
                {
                    Text = "Ổ đĩa này không cung cấp dữ liệu SMART qua driver hiện tại (thường gặp với ổ gắn qua USB/RAID/NAS).",
                    Foreground = BrushMuted,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var sensor in hw.Sensors.OrderBy(s => s.SensorType.ToString()).ThenBy(s => s.Name))
            {
                SmartRowsPanel.Children.Add(BuildSmartRow(sensor));
            }
        }

        private Border BuildSmartRow(ISensor sensor)
        {
            var border = new Border { Style = (Style)FindResource("SensorRowStyle") };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var nameText = new TextBlock
            {
                Text = sensor.Name,
                Foreground = Brushes.White,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Định dạng số theo kiểu Việt Nam: dấu chấm ngăn cách hàng nghìn (VD: 13167 -> "13.167")
            var valueText = new TextBlock
            {
                Text = sensor.Value.HasValue ? sensor.Value.Value.ToString("#,##0.##", VnNumberFormat) : "N/A",
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 16, 0)
            };
            var unitText = new TextBlock
            {
                Text = GetSensorTypeUnit(sensor.SensorType),
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Grid.SetColumn(nameText, 0);
            Grid.SetColumn(valueText, 1);
            Grid.SetColumn(unitText, 2);
            grid.Children.Add(nameText);
            grid.Children.Add(valueText);
            grid.Children.Add(unitText);

            border.Child = grid;
            return border;
        }

        // ---------- Định dạng / nhãn cảm biến dùng chung ----------

        private static string FormatSensorValue(ISensor sensor)
        {
            if (!sensor.Value.HasValue) return "N/A";

            double val = sensor.Value.Value;

            // Một số cảm biến (đặc biệt là Power) trả về đúng 0 khi không đọc được (thiếu quyền Admin)
            // thay vì null — coi đây là N/A để tránh hiểu lầm thiết bị tiêu thụ đúng 0W.
            if (sensor.SensorType == SensorType.Power && val == 0)
            {
                return "N/A";
            }

            string unit = GetSensorTypeUnit(sensor.SensorType);
            return unit.Length > 0 ? $"{val:0.#} {unit}" : $"{val:0.#}";
        }

        private static string GetSensorTypeLabel(SensorType type)
        {
            switch (type)
            {
                case SensorType.Load: return "Tải";
                case SensorType.Temperature: return "Nhiệt độ";
                case SensorType.Clock: return "Xung nhịp";
                case SensorType.Power: return "Công suất";
                case SensorType.Fan: return "Quạt";
                case SensorType.Level: return "Mức";
                case SensorType.Data: return "Dữ liệu";
                case SensorType.Throughput: return "Tốc độ truyền";
                case SensorType.Voltage: return "Điện áp";
                default: return type.ToString();
            }
        }

        private static string GetSensorTypeUnit(SensorType type)
        {
            switch (type)
            {
                case SensorType.Temperature: return "°C";
                case SensorType.Load: return "%";
                case SensorType.Clock: return "MHz";
                case SensorType.Power: return "W";
                case SensorType.Fan: return "RPM";
                case SensorType.Level: return "%";
                case SensorType.Data: return "GB";
                case SensorType.SmallData: return "MB";
                case SensorType.Throughput: return "MB/s";
                case SensorType.Voltage: return "V";
                case SensorType.Factor: return "x";
                default: return "";
            }
        }
    }
}
