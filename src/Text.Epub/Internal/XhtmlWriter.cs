using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace M3scalinum.Text.Epub.Internal;

/// <summary>
/// Serializes a parsed chapter into an XHTML content document.
/// </summary>
/// <remarks>
/// The output is written through <see cref="XmlWriter"/>, so the result is well-formed by
/// construction: text and attribute values are escaped, void elements are closed, and every element
/// sits in the XHTML namespace. The input is repaired on the way: an element that is not part of the
/// XHTML vocabulary is unwrapped (its children are kept), an element that cannot exist in a book's
/// content (a script, a frame, a form control) is dropped with its subtree, and an <c>id</c> that
/// already occurred in the book is dropped so the package's identifiers stay unique.
/// </remarks>
internal sealed class XhtmlWriter
{
    /// <summary>The XHTML namespace every content document declares.</summary>
    public const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    /// <summary>The EPUB structural vocabulary namespace, declared so <c>epub:type</c> is legal.</summary>
    public const string EpubNamespace = "http://www.idpf.org/2007/ops";

    /// <summary>The XML namespace of <c>xml:lang</c>.</summary>
    public const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Elements whose content is prose or table structure, kept as elements.</summary>
    private static readonly HashSet<string> Kept = new(StringComparer.Ordinal)
    {
        "p", "div", "span", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "dl", "dt", "dd",
        "blockquote", "pre", "code", "em", "strong", "b", "i", "u", "s", "strike", "del", "ins",
        "sub", "sup", "small", "big", "cite", "q", "abbr", "acronym", "dfn", "kbd", "samp", "var",
        "mark", "time", "br", "hr", "img", "a", "table", "caption", "thead", "tbody", "tfoot", "tr",
        "th", "td", "colgroup", "col", "figure", "figcaption", "section", "article", "header",
        "footer", "nav", "aside", "address", "main", "details", "summary", "hgroup", "menu", "center",
        "ruby", "rt", "rp", "wbr", "legend", "fieldset", "label",
    };

    /// <summary>Elements that carry no meaning in a book's content, dropped with their subtree.</summary>
    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal)
    {
        "script", "style", "head", "title", "meta", "link", "base", "iframe", "frame", "frameset",
        "object", "embed", "applet", "param", "video", "audio", "canvas", "svg", "math", "template",
        "noscript", "input", "button", "select", "option", "optgroup", "textarea", "map", "source",
        "track",
    };

    /// <summary>Wrappers that contribute nothing but their children.</summary>
    private static readonly HashSet<string> Transparent = new(StringComparer.Ordinal)
    {
        "html", "body", "picture", "form",
    };

    /// <summary>Elements that may stand at the top of a body without being wrapped in a paragraph.</summary>
    private static readonly HashSet<string> Blocks = new(StringComparer.Ordinal)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "dl", "blockquote", "pre", "hr",
        "table", "figure", "section", "article", "header", "footer", "nav", "aside", "address",
        "main", "details", "hgroup", "menu", "center", "fieldset",
    };

    private readonly HashSet<string> _identifiers;
    private readonly List<string> _references;

    /// <summary>Creates a writer sharing one identifier set and one reference list across the book.</summary>
    /// <param name="identifiers">The ids already used in the book, which this writer adds to.</param>
    /// <param name="references">The local references the chapter makes, which this writer adds to.</param>
    public XhtmlWriter(HashSet<string> identifiers, List<string> references)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        ArgumentNullException.ThrowIfNull(references);

        _identifiers = identifiers;
        _references = references;
    }

    /// <summary>Writes a whole content document around a chapter's body content.</summary>
    /// <param name="writer">The XML writer of the response or file.</param>
    /// <param name="title">The document title, shown by a reader that reads the head.</param>
    /// <param name="language">The language tag, on <c>html</c> as <c>xml:lang</c>.</param>
    /// <param name="stylesheet">The href of the book stylesheet, relative to this document.</param>
    /// <param name="nodes">The parsed body content.</param>
    public void WriteDocument(XmlWriter writer, string title, string language, string stylesheet, IReadOnlyList<HtmlNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(nodes);

        writer.WriteStartDocument();
        writer.WriteDocType("html", null, null, null);

        writer.WriteStartElement("html", XhtmlNamespace);
        writer.WriteAttributeString("xmlns", "epub", "http://www.w3.org/2000/xmlns/", EpubNamespace);
        writer.WriteAttributeString("xml", "lang", XmlNamespace, language);
        writer.WriteAttributeString("lang", language);

        writer.WriteStartElement("head", XhtmlNamespace);
        writer.WriteStartElement("title", XhtmlNamespace);
        writer.WriteString(Clean(title));
        writer.WriteEndElement();
        writer.WriteStartElement("meta", XhtmlNamespace);
        writer.WriteAttributeString("charset", "utf-8");
        writer.WriteEndElement();
        writer.WriteStartElement("link", XhtmlNamespace);
        writer.WriteAttributeString("rel", "stylesheet");
        writer.WriteAttributeString("type", "text/css");
        writer.WriteAttributeString("href", stylesheet);
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("body", XhtmlNamespace);
        WriteBody(writer, nodes);
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    /// <summary>Writes body content: block elements bare, runs of text and inline elements in a paragraph.</summary>
    private void WriteBody(XmlWriter writer, IReadOnlyList<HtmlNode> nodes)
    {
        var inline = new List<HtmlNode>();

        void Flush()
        {
            if (inline.Count == 0)
            {
                return;
            }

            writer.WriteStartElement("p", XhtmlNamespace);
            WriteChildren(writer, inline);
            writer.WriteEndElement();
            inline.Clear();
        }

        foreach (var node in nodes)
        {
            if (node is HtmlText text)
            {
                if (text.Text.Trim().Length == 0)
                {
                    continue;
                }

                inline.Add(node);
                continue;
            }

            if (node is not HtmlElement element)
            {
                continue;
            }

            if (Dropped.Contains(element.Name))
            {
                continue;
            }

            if (Transparent.Contains(element.Name))
            {
                WriteBody(writer, element.Children);
                continue;
            }

            if (Kept.Contains(element.Name) && Blocks.Contains(element.Name))
            {
                Flush();
                WriteElement(writer, element);
                continue;
            }

            inline.Add(node);
        }

        Flush();
    }

    private void WriteChildren(XmlWriter writer, IReadOnlyList<HtmlNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case HtmlText text:
                    WriteText(writer, text.Text);
                    break;

                case HtmlElement element:
                    WriteElement(writer, element);
                    break;

                default:
                    break;
            }
        }
    }

    private void WriteElement(XmlWriter writer, HtmlElement element)
    {
        var name = element.Name;

        if (Dropped.Contains(name))
        {
            return;
        }

        if (Transparent.Contains(name) || !Kept.Contains(name))
        {
            WriteChildren(writer, element.Children);
            return;
        }

        writer.WriteStartElement(name, XhtmlNamespace);

        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in element.Attributes)
        {
            WriteAttribute(writer, attribute.Key, attribute.Value, written);
        }

        foreach (var child in element.Children)
        {
            if (child is HtmlText text)
            {
                if (text.Text.Length == 0)
                {
                    continue;
                }

                WriteText(writer, text.Text);
                continue;
            }

            if (child is HtmlElement nested)
            {
                if (Dropped.Contains(nested.Name))
                {
                    continue;
                }

                WriteElement(writer, nested);
            }
        }

        writer.WriteEndElement();
    }

    private void WriteAttribute(XmlWriter writer, string name, string value, HashSet<string> written)
    {
        if (name.Length == 0
            || name.StartsWith("xmlns", StringComparison.Ordinal)
            || name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
            || !IsPlainName(name)
            || !written.Add(name))
        {
            return;
        }

        if (name == "id")
        {
            var identifier = value.Trim();
            if (identifier.Length == 0 || !IsPlainName(identifier) || !_identifiers.Add(identifier))
            {
                return;
            }

            writer.WriteAttributeString("id", identifier);
            return;
        }

        if ((name is "src" or "href" or "poster") && value.Length > 0)
        {
            AddReference(value);
        }

        writer.WriteAttributeString(name, Clean(value));
    }

    private void AddReference(string reference)
    {
        if (reference.StartsWith('#')
            || reference.StartsWith('?')
            || EpubPath.IsExternal(reference))
        {
            return;
        }

        _references.Add(reference);
    }

    private static void WriteText(XmlWriter writer, string text)
    {
        var cleaned = Clean(text);
        if (cleaned.Length > 0)
        {
            writer.WriteString(cleaned);
        }
    }

    private static bool IsPlainName(string name)
    {
        try
        {
            XmlConvert.VerifyNCName(name);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>Drops the characters XML 1.0 cannot carry, which a source document may still hold.</summary>
    private static string Clean(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (XmlConvert.IsXmlChar(c))
            {
                builder?.Append(c);
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder ??= new StringBuilder(text.Length).Append(text, 0, i);
                builder.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }

            builder ??= new StringBuilder(text.Length).Append(text, 0, i);
        }

        return builder?.ToString() ?? text;
    }
}
