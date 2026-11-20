using System;

namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class InlineCodeNode(string code) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        var fence = new string('`', LongestBacktickRun(code) + 1);
        writer.WriteVerbatim(fence);
        writer.WriteVerbatim(code);
        writer.WriteVerbatim(fence);
    }

    private static int LongestBacktickRun(string text)
    {
        var longest = 0;
        var current = 0;

        foreach (var c in text)
        {
            current = c == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}