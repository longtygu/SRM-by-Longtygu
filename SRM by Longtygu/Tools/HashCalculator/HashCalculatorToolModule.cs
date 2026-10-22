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
        public string Description => "Calculate MD5, SHA1, SHA256, SHA512 and CRC32 hashes of files - drag and drop multiple files, compare hashes quickly, no files are stored internally";
        public string Version => "1.0.0";
        public string IconKind => "Fingerprint";

        public UserControl CreateView(IServiceProvider serviceProvider)
        {
            return new HashCalculatorView();
        }
    }
}
