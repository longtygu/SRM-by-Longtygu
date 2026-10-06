using SRM_by_Longtygu.Plugins;
using System;
using System.Windows.Controls;

namespace SRM_by_Longtygu.Tools.HashCalculator
{
    // Tool tích hợp sẵn trong app — không cần build .dll riêng hay thả vào Plugins/
    public class HashCalculatorToolModule : IToolModule
    {
        public string Id => "builtin.hash-calculator";
        public string Name => "Hash Calculator";
        public string Description => "Tính mã băm MD5, SHA1, SHA256, SHA512, CRC32 của file — kéo thả nhiều file, đối chiếu hash nhanh, không lưu trữ file nội bộ";
        public string Version => "1.0.0";
        public string IconKind => "Fingerprint";

        public UserControl CreateView(IServiceProvider serviceProvider)
        {
            return new HashCalculatorView();
        }
    }
}
