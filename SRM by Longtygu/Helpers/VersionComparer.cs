using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SRM_by_Longtygu.Helpers
{
    /// <summary>
    /// So sánh version dạng chuỗi tự do (không dùng được System.Version vì nó throw
    /// exception với format lạ như "v2021.10", "24.3.1-beta", "10.0.19045.1"...).
    /// Logic: tách theo dấu . - _ , lấy phần số của mỗi đoạn rồi so sánh tuần tự.
    /// Không parse được đoạn nào thì coi đoạn đó = 0 (an toàn, không throw).
    /// </summary>
    public class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new VersionComparer();

        public int Compare(string x, string y)
        {
            var a = ParseSegments(x);
            var b = ParseSegments(y);
            int len = Math.Max(a.Count, b.Count);

            for (int i = 0; i < len; i++)
            {
                long av = i < a.Count ? a[i] : 0;
                long bv = i < b.Count ? b[i] : 0;
                int cmp = av.CompareTo(bv);
                if (cmp != 0) return cmp;
            }
            return 0;
        }

        private static List<long> ParseSegments(string version)
        {
            var list = new List<long>();
            if (string.IsNullOrWhiteSpace(version)) return list;

            string cleaned = version.Trim().TrimStart('v', 'V');
            var parts = Regex.Split(cleaned, @"[.\-_]");

            foreach (var part in parts)
            {
                var digits = Regex.Match(part, @"\d+");
                list.Add(digits.Success ? long.Parse(digits.Value) : 0);
            }
            return list;
        }
    }
}
