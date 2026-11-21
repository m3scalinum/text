using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Epub.Internal;

/// <summary>
/// The path rules of the container. A name inside an EPUB is a relative path whose every segment is
/// plain ASCII and whose <c>..</c> segment is refused: an entry is written under the package
/// document's directory, and a name that climbs out of it would either be stored outside the
/// intended tree or be a reference nothing backs.
/// </summary>
internal static partial class EpubPath
{
    /// <summary>The names the writer generates itself, which an asset may not take.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "mimetype", "META-INF/container.xml", "content.opf", "nav.xhtml", "book.css",
    };

    [GeneratedRegex("^ch\\d{3}\\.xhtml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedChapter();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex Scheme();

    /// <summary>
    /// Checks a path stored inside the container, as an asset name or the target of a reference, and
    /// returns it unchanged.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <param name="what">What the path belongs to, for the message.</param>
    /// <exception cref="EpubValidationException">The path is empty, absolute, or leaves the container.</exception>
    public static string Validate(string path, string what)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(what);

        if (path.Length == 0)
        {
            throw new EpubValidationException($"The {what} is an empty path.");
        }

        if (path[0] == '/')
        {
            throw new EpubValidationException($"The {what} '{path}' is an absolute path; every path in a container is relative.");
        }

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            throw new EpubValidationException($"The {what} '{path}' uses a backslash; an EPUB path is a URI path with forward slashes.");
        }

        if (path.Contains('?', StringComparison.Ordinal) || path.Contains('#', StringComparison.Ordinal))
        {
            throw new EpubValidationException($"The {what} '{path}' carries a query or a fragment; pass the path alone.");
        }

        if (path.Contains(':', StringComparison.Ordinal))
        {
            throw new EpubValidationException($"The {what} '{path}' contains a colon, which a relative path inside the container cannot carry.");
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0)
            {
                throw new EpubValidationException($"The {what} '{path}' has an empty segment.");
            }

            if (segment == ".")
            {
                throw new EpubValidationException($"The {what} '{path}' has a '.' segment.");
            }

            if (segment == "..")
            {
                throw new EpubValidationException($"The {what} '{path}' climbs out of the container with '..'; every path must stay inside it.");
            }

            foreach (var c in segment)
            {
                var plain = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.';
                if (!plain)
                {
                    throw new EpubValidationException(
                        string.Create(CultureInfo.InvariantCulture, $"The {what} '{path}' contains '{c}', which a container path cannot carry; use letters, digits, '-', '_' and '.'."));
                }
            }
        }

        return path;
    }

    /// <summary>Checks that an asset name is a valid path and is not one the writer generates.</summary>
    public static string ValidateAssetName(string name)
    {
        Validate(name, "asset name");

        if (Reserved.Contains(name) || GeneratedChapter().IsMatch(name))
        {
            throw new EpubValidationException($"The asset name '{name}' is reserved: the writer generates that entry itself.");
        }

        return name;
    }

    /// <summary>Whether a reference is an absolute URL, which the container does not have to back.</summary>
    public static bool IsExternal(string reference)
        => reference.StartsWith("//", StringComparison.Ordinal)
            || Scheme().IsMatch(reference);
}
