using System;
using SRM_by_Longtygu.Services;

namespace SRM_by_Longtygu.Plugins
{
    // Lớp bọc quanh IToolModule: trả tên/mô tả đã dịch theo ngôn ngữ đang chọn.
    // Thứ tự ưu tiên:
    //   1) DLL tự cung cấp bản dịch qua ILocalizedToolModule
    //   2) Công cụ tích hợp: khóa từ điển Tool_<Tên>_Name / Tool_<Tên>_Desc (suy ra từ Id "builtin.xxx-yyy")
    //   3) Name / Description gốc của module (dự phòng)
    public sealed class ToolItem
    {
        private const string BuiltInPrefix = "builtin.";

        public ToolItem(IToolModule module)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public IToolModule Module { get; }

        public string Id => Module.Id;
        public string Version => Module.Version;
        public string IconKind => Module.IconKind;

        public bool IsBuiltIn => !string.IsNullOrEmpty(Module.Id)
                                 && Module.Id.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase);

        public string StatusText => Loc.T(IsBuiltIn ? "Tools_BuiltInBadge" : "Tools_PluginBadge");

        public string Name => Resolve(true);
        public string Description => Resolve(false);

        private string Resolve(bool isName)
        {
            string raw = isName ? Module.Name : Module.Description;
            try
            {
                if (Module is ILocalizedToolModule localized)
                {
                    string code = LocalizationService.Instance?.CurrentLanguage ?? "en-US";
                    string fromDll = isName ? localized.GetName(code) : localized.GetDescription(code);
                    if (!string.IsNullOrWhiteSpace(fromDll)) return fromDll;
                }

                if (IsBuiltIn)
                {
                    string key = "Tool_" + KeyOf(Module.Id) + (isName ? "_Name" : "_Desc");
                    string fromDict = Loc.T(key);
                    if (!string.Equals(fromDict, key, StringComparison.Ordinal)) return fromDict;
                }
            }
            catch
            {
                // Tool lỗi khi trả bản dịch thì dùng chuỗi gốc, không làm hỏng trang
            }
            return raw ?? string.Empty;
        }

        // "builtin.disk-health" -> "DiskHealth"
        private static string KeyOf(string id)
        {
            string body = id.Substring(BuiltInPrefix.Length);
            var parts = body.Split(new[] { '-', '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p.Substring(1));
            }
            return sb.ToString();
        }
    }
}
