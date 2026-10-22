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
        public string Description => "Check system health (activation, security, TPM, BitLocker...) and quickly fix Windows issues (DISM, SFC, CHKDSK, network, Windows Update)";
        public string Version => "1.0.0";
        public string IconKind => "ShieldCheckOutline";

        public UserControl CreateView(IServiceProvider serviceProvider)
        {
            return new WindowsHealthCheckView();
        }
    }
}
