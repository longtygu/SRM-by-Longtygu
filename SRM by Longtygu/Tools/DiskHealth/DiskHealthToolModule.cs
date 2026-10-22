using SRM_by_Longtygu.Plugins;
using System;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Tools.DiskHealth
{
    // Tool tích hợp sẵn trong app — không cần build .dll riêng hay thả vào Plugins/
    public class DiskHealthToolModule : IToolModule
    {
        public string Id => "builtin.disk-health";
        public string Name => "Quick Read/Write Speed Test";
        public string Description => "Measure disk read/write speed and check drive SMART health";
        public string Version => "1.0.0";
        public string IconKind => "Harddisk";

        public UserControl CreateView(IServiceProvider serviceProvider)
        {
            return new DiskHealthView();
        }
    }
}