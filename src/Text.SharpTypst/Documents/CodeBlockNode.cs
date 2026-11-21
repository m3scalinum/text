using System;

namespace M3scalinum.Text.SharpTypst.Documents;

internal sealed class CodeBlockNode(string language, string code) : TypstNode
{
    internal override void Write(TypstMarkupWriter writer)
    {
        // A fence longer than any backtick run in the code, so a block that contains ``` stays a
        // block instead of ending at the first one. Three is the shortest fence Typst accepts.
        var fence = new string('`', Math.Max(3, LongestBacktickRun(code) + 1));

        writer.BlankLine();
        writer.WriteVerbatim(fence + language);
        writer.NewLine();
        writer.WriteVerbatim(code.TrimEnd('\n'));
        writer.NewLine();
        writer.WriteVerbatim(fence);
        writer.NewLine();
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
