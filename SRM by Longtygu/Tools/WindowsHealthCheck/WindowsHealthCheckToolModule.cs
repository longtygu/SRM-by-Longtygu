using SRM_by_Longtygu.Plugins;
using System;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Tools.WindowsHealthCheck
{
    // Tool tích hợp sẵn trong app — không cần build .dll riêng hay thả vào Plugins/
    public class WindowsHealthCheckToolModule : IToolModule
    {
        public string Id => "builtin.windows-health-check";
        public string Name => "Windows Health Check";
        public string Description => "Kiểm tra tình trạng hệ thống (kích hoạt, bảo mật, TPM, BitLocker...) và sửa lỗi Windows nhanh (DISM, SFC, CHKDSK, mạng, Windows Update)";
        public string Version => "1.0.0";
        public string IconKind => "ShieldCheckOutline";

        public UserControl CreateView(IServiceProvider serviceProvider)
        {
            return new WindowsHealthCheckView();
        }
    }
}
