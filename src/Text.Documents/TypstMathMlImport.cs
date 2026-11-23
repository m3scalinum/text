using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace M3scalinum.Text.Documents;

/// <summary>The MathML subset, read under the suite's XML policy.</summary>
/// <remarks>
/// <para>
/// MathML is XML written by a third party — a browser, an editor, a publisher's pipeline — so it is read
/// under the same policy the suite applies to every XML part it parses (22-docx.md §10): no document
/// type declaration, no entity, an explicit character cap, an explicit nesting cap and an explicit
/// element count. The settings below are the ones <c>PdfLotl</c> uses; this project has no dependency on
/// the PDF assembly and no XML dependency of its own, so the door is repeated here rather than shared.
/// </para>
/// <para>
/// An element outside <see cref="Supported"/> is refused with <see cref="TypstMathMlError.Unsupported"/>
/// and its name, never skipped. A container that breaks the shape its element requires — an
/// <c>msup</c> with one child, an <c>mtable</c> whose rows have different widths — is
/// <see cref="TypstMathMlError.Malformed"/>.
/// </para>
/// </remarks>
internal static class TypstMathMlReader
{
    /// <summary>The longest formula read, in characters. A formula is a phrase, not a document.</summary>
    internal const int MaxCharacters = 1024 * 1024;

    /// <summary>The deepest nesting accepted. A displayed formula nests well under this.</summary>
    internal const int MaxDepth = 64;

    /// <summary>The most elements one formula may hold.</summary>
    internal const int MaxElements = 10_000;

    /// <summary>The MathML namespace, and the one every element must be in — or in none.</summary>
    private static readonly XNamespace MathMl = "http://www.w3.org/1998/Math/MathML";

    /// <summary>The token elements: they hold text and no children.</summary>
    private static readonly HashSet<string> Tokens = new(StringComparer.Ordinal)
    {
        "mi", "mn", "mo", "mtext",
    };

    /// <summary>The elements of the subset this model carries.</summary>
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        "mi", "mn", "mo", "mtext", "mrow", "msup", "msub", "msubsup", "mfrac", "msqrt", "mroot",
        "mtable", "mtr", "mtd",
    };

    /// <summary>Reads a formula and returns its tree.</summary>
    /// <param name="mathml">The <c>&lt;math&gt;</c> element, as text.</param>
    /// <returns>The root's children and the display mode it asked for.</returns>
    /// <exception cref="ArgumentException"><paramref name="mathml"/> is blank.</exception>
    /// <exception cref="TypstMathMlException">The XML is refused by the policy, passes a bound, is not
    /// well formed, or uses an element outside the subset.</exception>
    internal static TypstMathMlExpression Read(string mathml)
    {
        ArgumentNullException.ThrowIfNull(mathml);

        if (string.IsNullOrWhiteSpace(mathml))
        {
            throw new ArgumentException("a formula cannot be blank", nameof(mathml));
        }

        if (mathml.Length > MaxCharacters)
        {
            throw new TypstMathMlException(
                TypstMathMlError.Limit,
                $"the formula is {mathml.Length} characters, over the {MaxCharacters}-character cap");
        }

        Guard(mathml);
        var root = Load(mathml);

        if (root.Name.LocalName != "math")
        {
            throw Refusal(
                TypstMathMlError.Unsupported,
                $"the root element is <{root.Name.LocalName}>; a formula is a <math> element",
                root.Name.LocalName);
        }

        CheckNamespace(root, "math");

        var display = (string?)root.Attribute("display");
        var elements = 0;
        var children = Children(root, "math", 0, ref elements);

        if (children.Count == 0)
        {
            throw Refusal(
                TypstMathMlError.Malformed,
                "the <math> element holds no content; an empty formula has nothing to lay out",
                "math");
        }

        return new TypstMathMlExpression(
            children.ToArray(),
            string.Equals(display, "block", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Refuses a document type declaration or an entity before any parser sees one.</summary>
    /// <param name="mathml">The formula, as text.</param>
    private static void Guard(string mathml)
    {
        // A formula from a browser or an editor has neither a DTD nor an entity declaration, and
        // refusing the string outright costs nothing: a symbol name never contains the syntax.
        if (mathml.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || mathml.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
        {
            throw new TypstMathMlException(
                TypstMathMlError.XmlPolicy,
                "the formula declares a document type or an entity; a MathML formula has neither,"
                + " and both are refused");
        }
    }

    /// <summary>Loads the document with DTD processing prohibited and every resolver removed.</summary>
    /// <param name="mathml">The formula, as text.</param>
    /// <returns>The parsed document.</returns>
    private static XElement Load(string mathml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(mathml), Settings());
            var document = XDocument.Load(reader, LoadOptions.None);

            return document.Root
                ?? throw new TypstMathMlException(
                    TypstMathMlError.Malformed,
                    "the formula has no root element");
        }
        catch (XmlException exception)
        {
            throw new TypstMathMlException(
                TypstMathMlError.Malformed,
                $"the formula is not well-formed XML: {exception.Message}");
        }
    }

    /// <summary>The reader settings: no DTD, no resolver, bounded entities, comments ignored.</summary>
    private static XmlReaderSettings Settings() => new()
    {
        ConformanceLevel = ConformanceLevel.Document,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = false,
        MaxCharactersInDocument = MaxCharacters,
        MaxCharactersFromEntities = MaxCharacters,
    };

    /// <summary>Reads one element, with its subtree, refusing anything outside the subset.</summary>
    /// <param name="source">The element.</param>
    /// <param name="depth">How deep in the tree it sits, the root's child being 1.</param>
    /// <param name="elements">How many elements have been read so far, for the cap.</param>
    private static TypstMathMlElement Parse(XElement source, int depth, ref int elements)
    {
        if (depth > MaxDepth)
        {
            throw Refusal(
                TypstMathMlError.Limit,
                $"the formula nests past {MaxDepth} levels",
                source.Name.LocalName);
        }

        if (++elements > MaxElements)
        {
            throw Refusal(
                TypstMathMlError.Limit,
                $"the formula holds more than {MaxElements} elements",
                source.Name.LocalName);
        }

        var name = source.Name.LocalName;
        CheckNamespace(source, name);

        if (!Supported.Contains(name))
        {
            throw Refusal(
                TypstMathMlError.Unsupported,
                $"<{name}> is not in the MathML subset this model imports; the supported elements are"
                + " mi, mn, mo, mtext, mrow, msup, msub, msubsup, mfrac, msqrt, mroot, mtable, mtr and mtd",
                name);
        }

        if (Tokens.Contains(name))
        {
            foreach (var child in source.Elements())
            {
                throw Refusal(
                    TypstMathMlError.Malformed,
                    $"<{name}> holds a <{child.Name.LocalName}>; a token element holds text only",
                    name);
            }

            var text = TokenText(source, name);
            return new TypstMathMlElement(name, text, []);
        }

        var children = Children(source, name, depth, ref elements);
        CheckShape(name, children);

        return new TypstMathMlElement(name, string.Empty, children.ToArray());
    }

    /// <summary>Reads the element children of a container, refusing stray text.</summary>
    /// <param name="source">The container.</param>
    /// <param name="name">Its element name, for the message.</param>
    /// <param name="depth">How deep the container sits.</param>
    /// <param name="elements">How many elements have been read so far.</param>
    private static List<TypstMathMlElement> Children(
        XElement source,
        string name,
        int depth,
        ref int elements)
    {
        var children = new List<TypstMathMlElement>();

        foreach (var node in source.Nodes())
        {
            switch (node)
            {
                case XElement element:
                    children.Add(Parse(element, depth + 1, ref elements));
                    break;

                case XText text when !string.IsNullOrWhiteSpace(text.Value):
                    throw Refusal(
                        TypstMathMlError.Malformed,
                        $"<{name}> holds the text '{text.Value.Trim()}' outside a token element;"
                        + " wrap it in mi, mn, mo or mtext",
                        name);

                default:
                    break;
            }
        }

        return children;
    }

    /// <summary>The trimmed text of a token element.</summary>
    /// <param name="source">The token element.</param>
    /// <param name="name">Its element name, for the message.</param>
    private static string TokenText(XElement source, string name)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var node in source.Nodes())
        {
            if (node is XText text)
            {
                builder.Append(text.Value);
            }
        }

        var value = builder.ToString().Trim();

        if (value.Length == 0)
        {
            throw Refusal(
                TypstMathMlError.Malformed,
                $"<{name}> is empty; a token element holds the text it renders",
                name);
        }

        return value;
    }

    /// <summary>Refuses a container whose children do not match what its element requires.</summary>
    /// <param name="name">The element name.</param>
    /// <param name="children">Its children.</param>
    private static void CheckShape(string name, List<TypstMathMlElement> children)
    {
        switch (name)
        {
            case "mrow":
                RequireAtLeast(name, children, 1);
                break;

            case "msqrt":
                RequireExactly(name, children, 1, "the radicand");
                break;

            case "msup":
                RequireExactly(name, children, 2, "a base and a superscript");
                break;

            case "msub":
                RequireExactly(name, children, 2, "a base and a subscript");
                break;

            case "mfrac":
                RequireExactly(name, children, 2, "a numerator and a denominator");
                break;

            case "mroot":
                RequireExactly(name, children, 2, "a radicand and an index");
                break;

            case "msubsup":
                RequireExactly(name, children, 3, "a base, a subscript and a superscript");
                break;

            case "mtable":
                RequireAtLeast(name, children, 1);

                foreach (var row in children)
                {
                    if (row.Name != "mtr")
                    {
                        throw Refusal(
                            TypstMathMlError.Malformed,
                            $"<mtable> holds a <{row.Name}>; every row of a table is an <mtr>",
                            row.Name);
                    }
                }

                CheckRows(name, children);
                break;

            case "mtr":
                RequireAtLeast(name, children, 1);

                foreach (var cell in children)
                {
                    if (cell.Name != "mtd")
                    {
                        throw Refusal(
                            TypstMathMlError.Malformed,
                            $"<mtr> holds a <{cell.Name}>; every cell of a row is an <mtd>",
                            cell.Name);
                    }
                }

                break;

            default:
                break;
        }
    }

    /// <summary>Refuses a table whose rows are not all the same width.</summary>
    /// <param name="name">The table's element name, for the message.</param>
    /// <param name="rows">Its rows.</param>
    private static void CheckRows(string name, List<TypstMathMlElement> rows)
    {
        var columns = rows[0].Children.Count;

        foreach (var row in rows)
        {
            if (row.Children.Count != columns)
            {
                throw Refusal(
                    TypstMathMlError.Malformed,
                    $"<{name}> rows have different widths: {columns} cells then {row.Children.Count};"
                    + " Typst's mat() needs a rectangle",
                    name);
            }
        }
    }

    /// <summary>Refuses an element with more children than its definition takes.</summary>
    /// <param name="name">The element name.</param>
    /// <param name="children">Its children.</param>
    /// <param name="expected">How many the element takes.</param>
    /// <param name="what">What those children are, in the message.</param>
    private static void RequireExactly(string name, List<TypstMathMlElement> children, int expected, string what)
    {
        if (children.Count != expected)
        {
            throw Refusal(
                TypstMathMlError.Malformed,
                $"<{name}> takes {expected} children — {what} — and it has {children.Count}",
                name);
        }
    }

    /// <summary>Refuses an element with fewer children than its definition requires.</summary>
    /// <param name="name">The element name.</param>
    /// <param name="children">Its children.</param>
    /// <param name="minimum">The fewest the element takes.</param>
    private static void RequireAtLeast(string name, List<TypstMathMlElement> children, int minimum)
    {
        if (children.Count < minimum)
        {
            throw Refusal(
                TypstMathMlError.Malformed,
                $"<{name}> takes at least {minimum} child element(s) and it has {children.Count}",
                name);
        }
    }

    /// <summary>Refuses an element carrying a foreign namespace.</summary>
    /// <param name="source">The element.</param>
    /// <param name="name">Its local name.</param>
    private static void CheckNamespace(XElement source, string name)
    {
        var space = source.Name.Namespace;

        if (space != MathMl && space != XNamespace.None)
        {
            throw Refusal(
                TypstMathMlError.Unsupported,
                $"<{name}> is in the namespace '{space.NamespaceName}', not MathML",
                name);
        }
    }

    /// <summary>Builds the refusal, naming the element.</summary>
    /// <param name="error">Which class of refusal this is.</param>
    /// <param name="message">What was refused.</param>
    /// <param name="element">The element's local name.</param>
    private static TypstMathMlException Refusal(TypstMathMlError error, string message, string element)
        => new(error, message) { ElementName = element };
}

public sealed partial class TypstMathMlExpression
{
    /// <summary>Imports a MathML formula into the model.</summary>
    /// <param name="mathml">
    /// A <c>&lt;math&gt;</c> element, with or without the MathML namespace. Its <c>display="block"</c>
    /// attribute is kept and selects the display equation the document writes.
    /// </param>
    /// <returns>The formula, ready to be written as Typst markup or as MathML again.</returns>
    /// <exception cref="ArgumentException"><paramref name="mathml"/> is blank.</exception>
    /// <exception cref="TypstMathMlException">
    /// The XML declares a document type or an entity, passes the character, depth or element cap, is not
    /// well formed, holds an element outside the subset, or breaks the shape an element requires.
    /// </exception>
    public static TypstMathMlExpression Import(string mathml) => TypstMathMlReader.Read(mathml);
}
