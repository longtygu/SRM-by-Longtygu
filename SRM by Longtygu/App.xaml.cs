using Microsoft.Extensions.DependencyInjection;
using SRM_by_Longtygu.Database;
using SRM_by_Longtygu.Repositories;
using SRM_by_Longtygu.Services;
using SRM_by_Longtygu.Services.AppUpdate;
using SRM_by_Longtygu.Services.UpdateChecking;
using SRM_by_Longtygu.ViewModels;
using System;
using System.IO; // Đã thêm thư viện này để hỗ trợ ghi file log
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SRM_by_Longtygu
{
    public partial class App : Application
    {
        // Provider chứa toàn bộ các Service của ứng dụng
        public static IServiceProvider ServiceProvider { get; private set; }

        // ĐỂ TEST HIỆU ỨNG SPLASH: bật/tắt delay giả lập ở đây.
        // Khi dự án đã có nhiều dữ liệu thật (khởi động tự nhiên đủ chậm để thấy splash),
        // chỉ cần đổi thành false — không cần xóa code delay bên dưới.
        private const bool SimulateSlowStartup = false;

        // MỚI: dịch vụ đa ngôn ngữ, tạo ngay đầu OnStartup (trước splash) rồi đăng ký vào DI bằng chính instance này
        private ILocalizationService? _loc;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            // ============ BƯỚC 0: BẮT LỖI TOÀN CỤC TRƯỚC TIÊN ============
            // 1. Bắt lỗi trên UI Thread
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            // 2. Bắt lỗi trên các Thread ngầm (Background Threads)
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            // 3. Bắt lỗi từ các Task bất đồng bộ (async/await)
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // ============ BƯỚC 0.5: NẠP NGÔN NGỮ TRƯỚC KHI HIỆN SPLASH ============
            // Splash và các thông báo khởi động đều dùng chuỗi đa ngôn ngữ nên phải nạp từ điển trước (rất nhẹ, vài mili giây).
            try
            {
                var startupConfig = Helpers.SettingsManager.LoadSettings();
                _loc = new LocalizationService();
                _loc.SetLanguage(startupConfig.Language, persist: false);
            }
            catch (Exception langEx)
            {
                LogException(langEx, "Nạp ngôn ngữ khi khởi động");
            }

            // ============ BƯỚC 1: HIỆN SPLASH SCREEN NGAY LẬP TỨC ============
            var splash = new SplashWindow();
            splash.Show();

            // Ép UI thread render xong Splash trước khi chạy các bước tiếp theo
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));

            // ============ BƯỚC 2: KHỞI TẠO DATABASE ============
            splash.SetStatus(L("Splash_Database"));
            splash.SetProgress(15);
            await SimulatedDelay(500);
            DatabaseBootstrapper.InitializeDatabase();

            // ============ BƯỚC 3: CẤU HÌNH DEPENDENCY INJECTION ============
            splash.SetStatus(L("Splash_Services"));
            splash.SetProgress(40);
            await SimulatedDelay(600);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            ServiceProvider = serviceCollection.BuildServiceProvider();

            // MỚI: Đồng bộ schema DB (thêm bảng/cột mới còn thiếu) - PHẢI await xong hẳn ở đây,
            // trước khi bất kỳ ViewModel/Repository nào được lấy ra và query DB bên dưới.
            // Thiếu bước này là nguyên nhân lỗi "no such column" khi thêm cột mới vào code
            // nhưng DB thật của người dùng (đặc biệt các bản backup cũ) chưa có cột đó.
            splash.SetStatus(L("Splash_Schema"));
            splash.SetProgress(50);
            var schemaMigrator = ServiceProvider.GetRequiredService<IDatabaseSchemaMigrator>();
            await schemaMigrator.EnsureSchemaAsync();

            // ============ BƯỚC 4: ĐỌC CẤU HÌNH & NẠP NGÔN NGỮ ============
            splash.SetStatus(L("Splash_Config"));
            splash.SetProgress(65);
            await SimulatedDelay(500);

            // (Ngôn ngữ đã được nạp ở BƯỚC 0.5 trước splash; bước này chỉ còn là mốc tiến trình.)

            // ============ BƯỚC 5: CHUẨN BỊ GIAO DIỆN CHÍNH ============
            splash.SetStatus(L("Splash_UI"));
            splash.SetProgress(85);
            await SimulatedDelay(500);

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();

            // ============ BƯỚC 6: HOÀN TẤT ============
            splash.SetStatus(L("Splash_Done"));
            splash.SetProgress(100);
            await SimulatedDelay(400);

            // ============ BƯỚC 7: HIỆN MAINWINDOW RỒI ĐÓNG SPLASH ============
            Application.Current.MainWindow = mainWindow; // THÊM DÒNG NÀY để ấn định MainWindow
            mainWindow.Show();
            splash.Close();

            // MỚI: dọn file cũ (.srm-old) và thư mục tạm còn sót sau lần cập nhật trước (chạy nền, không chặn giao diện)
            AppUpdateCleanup.StartBackgroundCleanup(ServiceProvider.GetRequiredService<ILogService>());
        }

        // MỚI: lấy chuỗi đa ngôn ngữ; nếu dịch vụ chưa nạp được (lỗi rất sớm) thì dùng chuỗi dự phòng (hoặc chính khóa)
        private string L(string key, string? fallback = null)
        {
            return _loc != null ? _loc.GetString(key) : (fallback ?? key);
        }

        // MỚI: như L nhưng có tham số {0}; không bao giờ ném lỗi (hàm này chạy trong trình xử lý lỗi toàn cục)
        private string LFormat(string key, string fallbackFormat, params object[] args)
        {
            if (_loc != null) return _loc.Format(key, args);
            try { return string.Format(fallbackFormat, args); }
            catch (FormatException) { return fallbackFormat; }
        }

        // Delay giả lập — chỉ chạy khi SimulateSlowStartup = true, dùng để test hiệu ứng loading
        private static Task SimulatedDelay(int milliseconds)
        {
            return SimulateSlowStartup ? Task.Delay(milliseconds) : Task.CompletedTask;
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Database
            services.AddSingleton<IDatabaseConnectionFactory, SqliteConnectionFactory>();

            // Repositories
            services.AddTransient<ISoftwareRepository, SoftwareRepository>();
            services.AddTransient<ISoftwareVersionRepository, SoftwareVersionRepository>();
            services.AddSingleton<IPresetRepository, PresetRepository>();
            services.AddTransient<IResourceFileRepository, ResourceFileRepository>();
            services.AddTransient<ISoftwareResourceRepository, SoftwareResourceRepository>(); // MỚI: mapping Software <-> ResourceFile

            // Services
            services.AddSingleton<ILogService, LogService>();
            services.AddTransient<ISystemService, SystemService>();
            services.AddTransient<IDashboardService, DashboardService>();
            services.AddTransient<IFileProcessingService, FileProcessingService>();
            // Dùng đúng instance đã nạp ngôn ngữ từ đầu OnStartup (splash đã dùng nó)
            services.AddSingleton<ILocalizationService>(_loc ?? new LocalizationService());
            services.AddSingleton<IDialogService, DialogService>();
            services.AddTransient<IDeploymentService, DeploymentService>();
            services.AddTransient<IUninstallService, UninstallService>();
            services.AddSingleton<IPluginService, PluginService>();

            // MỚI: Trạng thái "đang bận" dùng chung toàn app (khóa chuyển tab khi có tiến trình dài đang chạy)
            services.AddSingleton<IBusyService, BusyService>();

            // MỚI: Dịch vụ kiểm tra + tải cập nhật ứng dụng (GitHub Releases / Regex trang web)
            services.AddTransient<IUpdateCheckService, UpdateCheckService>();

            // MỚI: Kiểm tra cập nhật cho CHÍNH SRM (GitHub Releases) - khác hẳn IUpdateCheckService ở trên
            services.AddSingleton<IAppSelfUpdateChecker, AppSelfUpdateChecker>();
            services.AddSingleton<IAppUpdateDownloader, AppUpdateDownloader>(); // MỚI: tải + xác minh gói cập nhật
            services.AddSingleton<IAppUpdateInstaller, AppUpdateInstaller>(); // MỚI: cài đặt gói cập nhật (thay file, hoàn tác khi lỗi)

            // Views
            services.AddTransient<MainWindow>();
            services.AddTransient<Views.AddSoftwareWindow>();
            services.AddTransient<Views.ConfigureUpdateSourceWindow>(); // MỚI
            services.AddTransient<Views.UpdateCenterView>(); // MỚI: trang riêng "Trung tâm cập nhật"

            // ViewModels
            services.AddSingleton<MainViewModel>(); // MainViewModel giữ vai trò host
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<LibraryViewModel>();
            services.AddTransient<DeploymentViewModel>();
            services.AddTransient<PresetsViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<AddSoftwareViewModel>();
            services.AddTransient<UninstallViewModel>();
            services.AddTransient<PresetMainViewModel>();
            services.AddTransient<ToolsViewModel>();
            services.AddTransient<ResourceFileMainViewModel>();
            services.AddTransient<AboutViewModel>();
            services.AddTransient<Views.AddMultipleSoftwareWindow>();
            services.AddTransient<ViewModels.AddMultipleSoftwareViewModel>();
            services.AddTransient<UpdateCenterViewModel>(); // MỚI: trang riêng "Trung tâm cập nhật"
            services.AddSingleton<AppUpdateViewModel>(); // MỚI: card "Cập nhật ứng dụng" trong trang Cài đặt (Singleton để giữ kết quả khi chuyển trang)
            services.AddTransient<IDatabaseSchemaMigrator, DatabaseSchemaMigrator>();
            // (hoặc AddSingleton, tuỳ bản chất service - dùng đúng lifetime bạn từng khai báo trước đây)
        }

        // ====================================================================
        // HỆ THỐNG BẮT VÀ GHI LOG LỖI KHI PUBLISH
        // ====================================================================

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException(e.Exception, "UI Thread Exception");
            MessageBox.Show(LFormat("Error_UiMessage", "Lỗi thao tác: {0}", e.Exception.Message) + "\n" + L("Error_LogHint", "Đã ghi log vào error_log.txt"),
                L("Error_UiTitle", "Lỗi hệ thống"), MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            LogException(ex, "Background Thread Exception");
            MessageBox.Show(L("Error_BackgroundMessage", "Lỗi nghiêm trọng ở luồng ngầm. Ứng dụng có thể phải đóng lại.") + "\n" + L("Error_CheckLogHint", "Vui lòng kiểm tra file error_log.txt"),
                L("Error_FatalTitle", "Lỗi nghiêm trọng"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException(e.Exception, "Task Exception");
            e.SetObserved();
        }

        private void LogException(Exception ex, string errorSource)
        {
            if (ex == null) return;

            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string logFilePath = Path.Combine(appDir, "error_log.txt");

                string logMessage = $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss}] - Nguồn: {errorSource}\n" +
                                    $"Lỗi: {ex.Message}\n" +
                                    $"Stack Trace:\n{ex.StackTrace}\n" +
                                    new string('-', 80) + "\n";

                File.AppendAllText(logFilePath, logMessage);
            }
            catch
            {
                // Bỏ qua lỗi tại đây để tránh vòng lặp crash
            }
        }
    }
}