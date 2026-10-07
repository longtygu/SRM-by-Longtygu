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
        private const bool SimulateSlowStartup = true;

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

            // ============ BƯỚC 1: HIỆN SPLASH SCREEN NGAY LẬP TỨC ============
            var splash = new SplashWindow();
            splash.Show();

            // Ép UI thread render xong Splash trước khi chạy các bước tiếp theo
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));

            // ============ BƯỚC 2: KHỞI TẠO DATABASE ============
            splash.SetStatus("Đang khởi tạo cơ sở dữ liệu...");
            splash.SetProgress(15);
            await SimulatedDelay(500);
            DatabaseBootstrapper.InitializeDatabase();

            // ============ BƯỚC 3: CẤU HÌNH DEPENDENCY INJECTION ============
            splash.SetStatus("Đang nạp các dịch vụ hệ thống...");
            splash.SetProgress(40);
            await SimulatedDelay(600);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            ServiceProvider = serviceCollection.BuildServiceProvider();

            // MỚI: Đồng bộ schema DB (thêm bảng/cột mới còn thiếu) - PHẢI await xong hẳn ở đây,
            // trước khi bất kỳ ViewModel/Repository nào được lấy ra và query DB bên dưới.
            // Thiếu bước này là nguyên nhân lỗi "no such column" khi thêm cột mới vào code
            // nhưng DB thật của người dùng (đặc biệt các bản backup cũ) chưa có cột đó.
            splash.SetStatus("Đang đồng bộ cấu trúc cơ sở dữ liệu...");
            splash.SetProgress(50);
            var schemaMigrator = ServiceProvider.GetRequiredService<IDatabaseSchemaMigrator>();
            await schemaMigrator.EnsureSchemaAsync();

            // ============ BƯỚC 4: ĐỌC CẤU HÌNH & NẠP NGÔN NGỮ ============
            splash.SetStatus("Đang nạp cấu hình & ngôn ngữ...");
            splash.SetProgress(65);
            await SimulatedDelay(500);

            var config = Helpers.SettingsManager.LoadSettings();
            var locService = ServiceProvider.GetRequiredService<ILocalizationService>();
            locService.SetLanguage(config.Language);

            // ============ BƯỚC 5: CHUẨN BỊ GIAO DIỆN CHÍNH ============
            splash.SetStatus("Đang chuẩn bị giao diện...");
            splash.SetProgress(85);
            await SimulatedDelay(500);

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();

            // ============ BƯỚC 6: HOÀN TẤT ============
            splash.SetStatus("Hoàn tất!");
            splash.SetProgress(100);
            await SimulatedDelay(400);

            // ============ BƯỚC 7: HIỆN MAINWINDOW RỒI ĐÓNG SPLASH ============
            Application.Current.MainWindow = mainWindow; // THÊM DÒNG NÀY để ấn định MainWindow
            mainWindow.Show();
            splash.Close();
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
            services.AddSingleton<ILocalizationService, LocalizationService>();
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
            MessageBox.Show($"Lỗi thao tác: {e.Exception.Message}\nĐã ghi log vào error_log.txt", "Lỗi hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            LogException(ex, "Background Thread Exception");
            MessageBox.Show("Lỗi nghiêm trọng ở luồng ngầm. Ứng dụng có thể phải đóng lại.\nVui lòng kiểm tra file error_log.txt", "Lỗi nghiêm trọng", MessageBoxButton.OK, MessageBoxImage.Error);
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