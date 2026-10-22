using SRM_by_Longtygu.Plugins;
using System;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Tools.RamTest
{
    // Tool tích hợp sẵn — kiểm tra RAM: cấp phát, ghi mẫu, đọc lại, xác minh CRC
    public class RamTestToolModule : IToolModule
    {
        public string Id => "builtin.ram-test";
        public string Name => "RAM Test";
        public string Description => "Allocate, write test patterns, read back and verify CRC to detect RAM errors";
        public string Version => "1.0.0";
        public string IconKind => "Memory";

        public UserControl CreateView(IServiceProvider serviceProvider)
        {
            return new RamTestView();
        }
    }
}
