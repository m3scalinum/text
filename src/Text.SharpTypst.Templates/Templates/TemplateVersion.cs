using System;

namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>
/// Orders the version strings a template manifest declares, so a catalog can answer "the newest
/// version of this layout" without a semantic-versioning dependency.
/// </summary>
/// <remarks>
/// A manifest version is dot-separated segments, each of which is compared numerically when both
/// sides are numbers and ordinally otherwise. That is enough for <c>1.9.0</c> before <c>1.10.0</c>,
/// and it degrades to a string comparison for a version that is not numeric at all instead of
/// throwing on a value the manifest allowed.
/// </remarks>
internal static class TemplateVersion
{
    internal static int Compare(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        var length = Math.Max(a.Length, b.Length);

        for (var index = 0; index < length; index++)
        {
            var l = index < a.Length ? a[index] : "0";
            var r = index < b.Length ? b[index] : "0";

            var comparison = long.TryParse(l, out var leftNumber) && long.TryParse(r, out var rightNumber)
                ? leftNumber.CompareTo(rightNumber)
                : string.CompareOrdinal(l, r);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
