using SRM_by_Longtygu.Plugins;
using System;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Tools.HardwareInfo
{
    // Tool tích hợp sẵn — hiển thị thông tin phần cứng, giám sát nhiệt độ thời gian thực và SMART ổ đĩa
    public class HardwareInfoToolModule : IToolModule
    {
        public string Id => "builtin.hardware-info";
        public string Name => "Thông tin phần cứng";
        public string Description => "Xem chi tiết CPU/GPU/RAM/Mainboard, giám sát nhiệt độ thời gian thực và S.M.A.R.T ổ đĩa";
        public string Version => "1.0.0";
        public string IconKind => "Chip";

        public UserControl CreateView(IServiceProvider serviceProvider)
        {
            return new HardwareInfoView();
        }
    }
}
