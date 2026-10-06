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
        public string Description => "Cấp phát, ghi mẫu, đọc lại và xác minh CRC để phát hiện lỗi bộ nhớ RAM";
        public string Version => "1.0.0";
        public string IconKind => "Memory";

        public UserControl CreateView(IServiceProvider serviceProvider)
        {
            return new RamTestView();
        }
    }
}
