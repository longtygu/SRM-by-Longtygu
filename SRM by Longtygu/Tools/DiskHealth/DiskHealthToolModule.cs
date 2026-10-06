using SRM_by_Longtygu.Plugins;
using System;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Tools.DiskHealth
{
    // Tool tích hợp sẵn trong app — không cần build .dll riêng hay thả vào Plugins/
    public class DiskHealthToolModule : IToolModule
    {
        public string Id => "builtin.disk-health";
        public string Name => "Test nhanh tốc độ đọc ghi";
        public string Description => "Đo tốc độ đọc/ghi và xem tình trạng SMART của ổ đĩa";
        public string Version => "1.0.0";
        public string IconKind => "Harddisk";

        public UserControl CreateView(IServiceProvider serviceProvider)
        {
            return new DiskHealthView();
        }
    }
}