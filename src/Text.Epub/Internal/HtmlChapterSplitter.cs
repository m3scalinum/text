using System;
using System.Collections.Generic;
using System.Text;

namespace M3scalinum.Text.Epub.Internal;

/// <summary>
/// Splits a document into chapters at its headings.
/// </summary>
/// <remarks>
/// The split level is the shallowest heading level the document uses: a document written with
/// <c>h1</c> chapters and <c>h2</c> sections splits at its <c>h1</c>, and one that starts at
/// <c>h2</c> splits at that. A heading starts a chapter and stays at its head, so a reader that
/// jumps to the chapter sees its title. Content before the first heading becomes a chapter of its
/// own under the book's title — that is where a preface or an abstract lives. Only the wrappers that
/// carry no structure of their own (<c>div</c>, <c>section</c>, <c>article</c>, <c>main</c>) are
/// unwrapped, so a list, a table and a paragraph keep their shape.
/// </remarks>
internal static class HtmlChapterSplitter
{
    /// <summary>Wrappers that are unwrapped before splitting, so a heading they hold starts a chapter.</summary>
    private static readonly HashSet<string> Unwrapped = new(StringComparer.Ordinal)
    {
        "div", "section", "article", "main", "body", "header", "footer",
    };

    /// <summary>Splits the body of a document into chapters.</summary>
    /// <remarks>The returned items carry the heading text and the nodes that make up the chapter.</remarks>
    public static IReadOnlyList<(string Title, List<HtmlNode> Nodes)> Split(HtmlElement body, string fallbackTitle)
    {
        ArgumentNullException.ThrowIfNull(body);

        var level = Level(body);
        var nodes = new List<HtmlNode>();
        Expand(body, nodes);

        if (level == 0)
        {
            return [(fallbackTitle, nodes)];
        }

        var chapters = new List<(string Title, List<HtmlNode> Nodes)>();
        var title = fallbackTitle;
        var chapter = new List<HtmlNode>();

        void Flush()
        {
            if (!HasContent(chapter))
            {
                chapter.Clear();
                return;
            }

            chapters.Add((title, chapter));
            chapter = [];
        }

        foreach (var node in nodes)
        {
            if (node is HtmlElement element && HeadingLevel(element.Name) == level)
            {
                Flush();
                title = HeadingText(element) is { Length: > 0 } text ? text : fallbackTitle;
                chapter.Add(element);
                continue;
            }

            chapter.Add(node);
        }

        Flush();

        if (chapters.Count == 0)
        {
            chapters.Add((fallbackTitle, []));
        }

        return chapters;
    }

    /// <summary>The shallowest heading level in the document, or 0 when it has no heading.</summary>
    private static int Level(HtmlElement element)
    {
        var level = 0;

        foreach (var child in element.Children)
        {
            if (child is not HtmlElement nested)
            {
                continue;
            }

            var heading = HeadingLevel(nested.Name);
            if (heading > 0)
            {
                level = level == 0 || heading < level ? heading : level;
                continue;
            }

            var deeper = Level(nested);
            if (deeper > 0 && (level == 0 || deeper < level))
            {
                level = deeper;
            }
        }

        return level;
    }

    /// <summary>Adds a container's children, unwrapping the wrappers that carry no structure.</summary>
    private static void Expand(HtmlElement container, List<HtmlNode> into)
    {
        foreach (var child in container.Children)
        {
            if (child is HtmlElement nested && Unwrapped.Contains(nested.Name))
            {
                Expand(nested, into);
                continue;
            }

            into.Add(child);
        }
    }

    /// <summary>Whether a chapter's nodes carry anything a reader would see.</summary>
    private static bool HasContent(List<HtmlNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is HtmlText text && text.Text.Trim().Length > 0)
            {
                return true;
            }

            if (node is HtmlElement)
            {
                return true;
            }
        }

        return false;
    }

    private static int HeadingLevel(string name)
        => name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6' ? name[1] - '0' : 0;

    /// <summary>The visible text of a heading, with whitespace collapsed to single spaces.</summary>
    private static string HeadingText(HtmlElement heading)
    {
        var builder = new StringBuilder();
        AppendText(heading, builder);

        return string.Join(' ', builder.ToString().Split((char[])[' ', '\t', '\n', '\r', '\u00A0'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static void AppendText(HtmlElement element, StringBuilder builder)
    {
        foreach (var child in element.Children)
        {
            if (child is HtmlText text)
            {
                builder.Append(text.Text);
                continue;
            }

            if (child is HtmlElement nested)
            {
                AppendText(nested, builder);
            }
        }
    }
}
