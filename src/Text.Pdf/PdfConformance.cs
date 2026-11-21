using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Checks the conformance a PDF claims, from its bytes. Pure managed parsing: no renderer, no
/// validator, no dependency beyond the framework.
/// </summary>
/// <remarks>
/// <para>
/// This is a structural checker, not a conformance validator. It establishes what the file declares
/// — tagged structure, language, title, alternate text, embedded fonts, version, XMP identifiers —
/// and nothing that would require interpreting the page description: colour, output intents, glyph
/// coverage, reading order, contrast and the several hundred remaining rules of ISO 14289-1 are out
/// of scope. veraPDF remains the reference validator; see
/// <c>docs/specifications/15-pdf-conformance.md</c> for the exact list.
/// </para>
/// <para>
/// <see cref="PdfConformance.Check"/> never throws on malformed input: a file so broken that its
/// catalog cannot be read is reported as <see cref="PdfConformanceCodes.CatalogMissing"/> or
/// <see cref="PdfConformanceCodes.XrefInvalid"/>.
/// </para>
/// </remarks>
public static class PdfConformance
{
    /// <summary>Checks <paramref name="pdf"/> against <paramref name="standard"/>.</summary>
    /// <param name="pdf">The document's bytes.</param>
    /// <param name="standard">The standard the document claims.</param>
    /// <returns>The findings, and whether any of them is an error.</returns>
    public static PdfConformanceReport Check(ReadOnlySpan<byte> pdf, PdfConformanceStandard standard)
    {
        var findings = new Findings(standard);

        if (pdf.IsEmpty)
        {
            findings.Error(PdfConformanceCodes.NotAPdf, "bytes", "the document is empty");

            return findings.Build(null);
        }

        if (!PdfFile.TryOpen(pdf.ToArray(), out var file))
        {
            findings.Error(PdfConformanceCodes.NotAPdf, "header", "the bytes do not begin with a %PDF header");

            return findings.Build(null);
        }

        var problemsRead = 0;
        foreach (var problem in file.Problems)
        {
            findings.Error(problem.Code, problem.Target, problem.Message);
            problemsRead++;
        }

        var version = ReadVersion(file);
        var requirements = PdfStandardRequirements.Of(standard);

        CheckVersion(findings, requirements, version);

        if (file.IsEncrypted)
        {
            findings.Error(
                PdfConformanceCodes.Encrypted,
                "trailer",
                "the file is encrypted, so nothing inside it can be checked; decrypt it with PdfEncryption.Open and check the bytes it returns");

            return findings.Build(version);
        }

        if (file.Catalog is not { } catalog)
        {
            findings.Error(
                PdfConformanceCodes.CatalogMissing,
                "trailer",
                "the trailer names no /Root that resolves to a catalog");

            return findings.Build(version);
        }

        if (!catalog.IsType("Catalog"))
        {
            findings.Error(PdfConformanceCodes.CatalogInvalid, "catalog", "the catalog is not /Type /Catalog");
        }

        var pages = file.Pages();
        CheckPages(findings, file, catalog, pages);

        var scan = new ResourceScan(file);
        foreach (var page in pages)
        {
            scan.Walk(page.Resources);
        }

        if (requirements.RequiresFontChecks)
        {
            CheckFonts(findings, file, scan);
        }

        var metadata = ReadMetadata(file, catalog, requirements, findings);
        CheckIdentity(findings, requirements, metadata);

        if (requirements.RequiresTaggedStructure)
        {
            CheckStructure(findings, file, catalog, pages, scan);
        }

        if (requirements.RequiresTitle)
        {
            CheckTitle(findings, file, metadata);
        }

        // Reading an object is lazy: a syntax error inside the structure tree or a font is only
        // discovered here, and still belongs in the report.
        foreach (var problem in file.Problems.Skip(problemsRead))
        {
            findings.Error(problem.Code, problem.Target, problem.Message);
        }

        return findings.Build(version);
    }

    /// <summary>Checks the document and throws when it does not conform.</summary>
    /// <param name="pdf">The document's bytes.</param>
    /// <param name="standard">The standard the document claims.</param>
    /// <exception cref="PdfConformanceException">At least one error was found.</exception>
    public static void Assert(ReadOnlySpan<byte> pdf, PdfConformanceStandard standard)
    {
        var report = Check(pdf, standard);

        if (!report.IsConformant)
        {
            throw new PdfConformanceException(report);
        }
    }

    private static PdfVersion ReadVersion(PdfFile file)
    {
        var version = new PdfVersion(file.MajorVersion, file.MinorVersion);

        // ISO 32000 allows the catalog to raise the version, and a PDF 2.0 producer that keeps a
        // 1.7 header for compatibility does exactly that.
        if (file.Catalog?["Version"] is PdfName name &&
            name.Value.Length == 3 &&
            char.IsAsciiDigit(name.Value[0]) &&
            name.Value[1] == '.' &&
            char.IsAsciiDigit(name.Value[2]))
        {
            var declared = new PdfVersion(name.Value[0] - '0', name.Value[2] - '0');
            if (declared > version)
            {
                version = declared;
            }
        }

        return version;
    }

    private static void CheckVersion(Findings findings, PdfStandardRequirements requirements, PdfVersion actual)
    {
        if (actual < requirements.Minimum || actual > requirements.Maximum)
        {
            var expected = requirements.Minimum == requirements.Maximum
                ? requirements.Minimum.ToString()
                : $"{requirements.Minimum} to {requirements.Maximum}";

            findings.Error(
                PdfConformanceCodes.VersionMismatch,
                "header",
                $"{requirements.Name} requires PDF {expected}, but the file declares {actual}");
        }
    }

    private static void CheckPages(
        Findings findings,
        PdfFile file,
        PdfDictionary catalog,
        IReadOnlyList<PdfPage> pages)
    {
        var tree = file.ResolveDictionary(catalog["Pages"]);
        if (tree is null)
        {
            findings.Error(PdfConformanceCodes.PagesInvalid, "catalog", "the catalog names no page tree");

            return;
        }

        if (!tree.IsType("Pages"))
        {
            findings.Error(PdfConformanceCodes.PagesInvalid, "catalog", "the catalog's /Pages is not /Type /Pages");
        }

        if (pages.Count == 0)
        {
            findings.Error(PdfConformanceCodes.PagesInvalid, "page tree", "the page tree holds no page");
        }

        if (file.Resolve(tree["Count"]) is PdfNumber count && count.IsInteger && count.AsInteger != pages.Count)
        {
            findings.Error(
                PdfConformanceCodes.PagesInvalid,
                "page tree",
                $"/Count declares {count.AsInteger} page(s) but the tree holds {pages.Count}");
        }
    }

    private static void CheckFonts(Findings findings, PdfFile file, ResourceScan scan)
    {
        foreach (var (number, font) in scan.Fonts)
        {
            var subtype = (file.Resolve(font["Subtype"]) as PdfName)?.Value;
            var name = (file.Resolve(font["BaseFont"]) as PdfName)?.Value ?? "unnamed font";
            var target = number > 0 ? $"obj {number}" : "font resource";

            if (subtype != "Type3" && !HasEmbeddedProgram(file, font, subtype))
            {
                findings.Error(
                    PdfConformanceCodes.FontNotEmbedded,
                    target,
                    $"the font {name} has no embedded font program");
            }

            if (file.Resolve(font["ToUnicode"]) is null && subtype != "Type3")
            {
                findings.Error(
                    PdfConformanceCodes.FontWithoutToUnicode,
                    target,
                    $"the font {name} has no /ToUnicode CMap");
            }
        }
    }

    private static bool HasEmbeddedProgram(PdfFile file, PdfDictionary font, string? subtype)
    {
        if (subtype == "Type0")
        {
            foreach (var descendant in file.ResolveArray(font["DescendantFonts"]))
            {
                if (file.ResolveDictionary(descendant) is { } dictionary && HasFontFile(file, dictionary))
                {
                    return true;
                }
            }

            return false;
        }

        return HasFontFile(file, font);
    }

    private static bool HasFontFile(PdfFile file, PdfDictionary font)
    {
        var descriptor = file.ResolveDictionary(font["FontDescriptor"]);
        if (descriptor is null)
        {
            return false;
        }

        return descriptor.ContainsKey("FontFile")
            || descriptor.ContainsKey("FontFile2")
            || descriptor.ContainsKey("FontFile3");
    }

    private static string? ReadMetadata(
        PdfFile file,
        PdfDictionary catalog,
        PdfStandardRequirements requirements,
        Findings findings)
    {
        var stream = file.ResolveStream(catalog["Metadata"]);
        if (stream is null)
        {
            if (requirements.RequiresMetadata)
            {
                findings.Error(
                    PdfConformanceCodes.MetadataMissing,
                    "catalog",
                    "the catalog names no XMP /Metadata stream, so the standard cannot be declared");
            }

            return null;
        }

        var bytes = file.Decode(stream);
        if (bytes is null)
        {
            findings.Warning(
                PdfConformanceCodes.MetadataUnreadable,
                "xmp",
                "the XMP stream uses a filter this reader does not decode, so its identifiers were not read");

            return null;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static void CheckIdentity(Findings findings, PdfStandardRequirements requirements, string? metadata)
    {
        if (requirements.ArchivePart is { } part)
        {
            if (metadata is null)
            {
                return;
            }

            if (!Xmp.TryProperty(metadata, "pdfaid:part", out var declared))
            {
                findings.Error(
                    PdfConformanceCodes.XmpStandardMismatch,
                    "xmp",
                    $"{requirements.Name} requires pdfaid:part {part}, but the XMP declares none");

                return;
            }

            if (!string.Equals(declared, part.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                findings.Error(
                    PdfConformanceCodes.XmpStandardMismatch,
                    "xmp",
                    $"{requirements.Name} requires pdfaid:part {part}, but the XMP declares {declared}");
            }

            if (requirements.ArchiveConformance is { } conformance)
            {
                if (!Xmp.TryProperty(metadata, "pdfaid:conformance", out var declaredConformance))
                {
                    findings.Error(
                        PdfConformanceCodes.XmpStandardMismatch,
                        "xmp",
                        $"{requirements.Name} requires pdfaid:conformance {conformance}, but the XMP declares none");
                }
                else if (!string.Equals(declaredConformance, conformance, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Error(
                        PdfConformanceCodes.XmpStandardMismatch,
                        "xmp",
                        $"{requirements.Name} requires pdfaid:conformance {conformance}, but the XMP declares {declaredConformance}");
                }
            }

            return;
        }

        if (requirements.Standard == PdfConformanceStandard.PdfUa1 && metadata is not null)
        {
            if (!Xmp.TryProperty(metadata, "pdfuaid:part", out var declared))
            {
                findings.Error(
                    PdfConformanceCodes.XmpStandardMismatch,
                    "xmp",
                    "PDF/UA-1 requires pdfuaid:part 1, but the XMP declares none");

                return;
            }

            if (!string.Equals(declared, "1", StringComparison.Ordinal))
            {
                findings.Error(
                    PdfConformanceCodes.XmpStandardMismatch,
                    "xmp",
                    $"PDF/UA-1 requires pdfuaid:part 1, but the XMP declares {declared}");
            }
        }
    }

    private static void CheckTitle(Findings findings, PdfFile file, string? metadata)
    {
        var info = file.ResolveDictionary(file.Trailer["Info"]);
        var infoTitle = (file.Resolve(info?["Title"]) as PdfString)?.Decode().Trim();

        var hasInfoTitle = !string.IsNullOrEmpty(infoTitle);
        var hasXmpTitle = metadata is not null && Xmp.TryProperty(metadata, "dc:title", out var xmpTitle) && xmpTitle.Length > 0;

        if (!hasInfoTitle && !hasXmpTitle)
        {
            findings.Error(
                PdfConformanceCodes.TitleMissing,
                "document information",
                "neither /Info /Title nor the XMP dc:title names the document");
        }
    }

    private static void CheckStructure(
        Findings findings,
        PdfFile file,
        PdfDictionary catalog,
        IReadOnlyList<PdfPage> pages,
        ResourceScan scan)
    {
        CheckLanguage(findings, file, catalog);

        var markInfo = file.ResolveDictionary(catalog["MarkInfo"]);
        if (markInfo?["Marked"] is not PdfBoolean { Value: true })
        {
            findings.Error(
                PdfConformanceCodes.MarkInfoMissing,
                "catalog",
                "the catalog has no /MarkInfo dictionary whose /Marked is true");
        }

        if (file.ResolveDictionary(catalog["StructTreeRoot"]) is not { } root)
        {
            findings.Error(
                PdfConformanceCodes.StructTreeMissing,
                "catalog",
                "the catalog names no /StructTreeRoot, so the document has no tagged structure");

            return;
        }

        if (!root.IsType("StructTreeRoot"))
        {
            findings.Error(
                PdfConformanceCodes.StructTreeInconsistent,
                "struct tree root",
                "the /StructTreeRoot dictionary is not /Type /StructTreeRoot");
        }

        var rootNumber = file.TryResolveReference(catalog["StructTreeRoot"], out var number, out _) ? number : 0;
        var parentKeys = ParentTreeKeys(file, root, findings);
        var figures = 0;
        var seen = new HashSet<int>();

        var pending = new Stack<(PdfDictionary Node, int Number)>();
        pending.Push((root, rootNumber));

        while (pending.Count > 0)
        {
            var (node, nodeNumber) = pending.Pop();

            foreach (var kid in file.ResolveArray(node["K"]))
            {
                if (!file.TryResolveReference(kid, out var kidNumber, out var resolved) ||
                    resolved is not PdfDictionary child ||
                    child["S"] is null)
                {
                    // A marked-content identifier or a direct MCR/OBJR dictionary: not a branch.
                    continue;
                }

                if (!seen.Add(kidNumber) && kidNumber != 0)
                {
                    findings.Error(
                        PdfConformanceCodes.StructTreeInconsistent,
                        $"obj {kidNumber}",
                        "the structure element is reachable more than once, so its /P cannot be right");

                    continue;
                }

                if (nodeNumber > 0)
                {
                    if (file.TryResolveReference(child["P"], out var parentNumber, out _))
                    {
                        if (parentNumber != nodeNumber)
                        {
                            findings.Error(
                                PdfConformanceCodes.StructTreeInconsistent,
                                $"obj {kidNumber}",
                                $"/P names object {parentNumber}, but the element is a child of object {nodeNumber}");
                        }
                    }
                    else
                    {
                        findings.Error(
                            PdfConformanceCodes.StructTreeInconsistent,
                            $"obj {kidNumber}",
                            "the structure element has no /P, so it is not attached to its parent");
                    }
                }

                if ((file.Resolve(child["S"]) as PdfName)?.Value == "Figure")
                {
                    figures++;
                    CheckAlternateText(findings, file, child, kidNumber);
                }

                pending.Push((child, kidNumber));
            }
        }

        foreach (var page in pages)
        {
            var structParents = file.Resolve(page.Dictionary["StructParents"]);
            if (structParents is not PdfNumber key || !key.IsInteger)
            {
                continue;
            }

            if (parentKeys is not null && !parentKeys.Contains((int)key.AsInteger))
            {
                findings.Error(
                    PdfConformanceCodes.StructTreeInconsistent,
                    $"page {page.Number}",
                    $"/StructParents is {key.AsInteger}, which the root's /ParentTree does not index");
            }
        }

        if (figures == 0 && scan.ImageNumbers.Count > 0)
        {
            findings.Warning(
                PdfConformanceCodes.ImageNotStructured,
                "struct tree",
                $"the pages draw {scan.ImageNumbers.Count} image(s) but the structure tree holds no /Figure element; they are either decorative or unexplained");
        }
    }

    private static void CheckLanguage(Findings findings, PdfFile file, PdfDictionary catalog)
    {
        if (file.Resolve(catalog["Lang"]) is PdfString language && language.Decode().Trim().Length > 0)
        {
            return;
        }

        findings.Error(
            PdfConformanceCodes.LanguageMissing,
            "catalog",
            "the catalog declares no /Lang, so a reader cannot know the natural language of the text");
    }

    private static HashSet<int>? ParentTreeKeys(PdfFile file, PdfDictionary root, Findings findings)
    {
        if (file.ResolveDictionary(root["ParentTree"]) is not { } tree)
        {
            findings.Error(
                PdfConformanceCodes.StructTreeInconsistent,
                "struct tree root",
                "the root has no /ParentTree, so marked content cannot be mapped to structure elements");

            return null;
        }

        if (file.Resolve(tree["Nums"]) is not PdfArray nums)
        {
            if (file.Resolve(tree["Kids"]) is PdfArray)
            {
                // A parent tree split into number trees: the keys are not directly readable, and
                // the page association check is skipped rather than guessed.
                return null;
            }

            findings.Error(
                PdfConformanceCodes.StructTreeInconsistent,
                "parent tree",
                "the /ParentTree has neither /Nums nor /Kids");

            return null;
        }

        var keys = new HashSet<int>();
        for (var i = 0; i + 1 < nums.Items.Count; i += 2)
        {
            if (file.Resolve(nums.Items[i]) is PdfNumber key && key.IsInteger)
            {
                keys.Add((int)key.AsInteger);
            }
        }

        return keys;
    }

    private static void CheckAlternateText(Findings findings, PdfFile file, PdfDictionary figure, int number)
    {
        if (HasText(file, figure["Alt"]) || HasText(file, figure["ActualText"]))
        {
            return;
        }

        findings.Error(
            PdfConformanceCodes.FigureAlternateTextMissing,
            number > 0 ? $"obj {number}" : "figure",
            "the /Figure structure element has neither /Alt nor /ActualText");
    }

    private static bool HasText(PdfFile file, PdfValue? value)
        => file.Resolve(value) is PdfString text && text.Decode().Trim().Length > 0;

    /// <summary>Collects the fonts and images reachable from page resources.</summary>
    private sealed class ResourceScan(PdfFile file)
    {
        private readonly HashSet<PdfDictionary> _seenFonts = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<PdfDictionary> _seenResources = new(ReferenceEqualityComparer.Instance);

        public List<(int Number, PdfDictionary Font)> Fonts { get; } = [];

        public HashSet<int> ImageNumbers { get; } = [];

        public void Walk(PdfDictionary? resources, int depth = 0)
        {
            if (resources is null || depth > 8 || !_seenResources.Add(resources))
            {
                return;
            }

            if (file.ResolveDictionary(resources["Font"]) is { } fonts)
            {
                foreach (var entry in fonts.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    if (file.TryResolveReference(entry.Value, out var number, out var resolved) &&
                        resolved is PdfDictionary font &&
                        _seenFonts.Add(font))
                    {
                        Fonts.Add((number, font));
                    }
                }
            }

            if (file.ResolveDictionary(resources["XObject"]) is { } xobjects)
            {
                foreach (var value in xobjects.Entries.Values)
                {
                    if (!file.TryResolveReference(value, out var number, out var resolved) ||
                        resolved is not PdfStream stream)
                    {
                        continue;
                    }

                    if (stream.Dictionary.IsType("XObject"))
                    {
                        var subtype = (file.Resolve(stream.Dictionary["Subtype"]) as PdfName)?.Value;

                        if (subtype == "Image")
                        {
                            ImageNumbers.Add(number);
                        }
                        else if (subtype == "Form")
                        {
                            Walk(file.ResolveDictionary(stream.Dictionary["Resources"]), depth + 1);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Accumulates findings while the checks run.</summary>
    private sealed class Findings(PdfConformanceStandard standard)
    {
        private readonly List<PdfConformanceIssue> _issues = [];

        public void Error(string code, string target, string message)
            => _issues.Add(new PdfConformanceIssue(code, PdfConformanceSeverity.Error, target, message));

        public void Warning(string code, string target, string message)
            => _issues.Add(new PdfConformanceIssue(code, PdfConformanceSeverity.Warning, target, message));

        public PdfConformanceReport Build(PdfVersion? version)
        {
            // Errors first, then warnings, each group in the order the checks ran.
            var ordered = _issues
                .OrderBy(issue => issue.Severity)
                .ToList();

            return new PdfConformanceReport(standard, version, ordered);
        }
    }

    /// <summary>Reads the handful of XMP properties a standard identifier uses.</summary>
    private static class Xmp
    {
        public static bool TryProperty(string xml, string name, out string value)
        {
            if (TryElement(xml, name, out value))
            {
                return true;
            }

            return TryAttribute(xml, name, out value);
        }

        private static bool TryElement(string xml, string name, out string value)
        {
            value = string.Empty;

            var open = xml.IndexOf("<" + name, StringComparison.Ordinal);
            if (open < 0)
            {
                return false;
            }

            var headerEnd = xml.IndexOf('>', open);
            if (headerEnd < 0)
            {
                return false;
            }

            var close = xml.IndexOf("</" + name, headerEnd, StringComparison.Ordinal);
            if (close < 0)
            {
                return false;
            }

            value = StripMarkup(xml[(headerEnd + 1)..close]);

            return value.Length > 0;
        }

        private static bool TryAttribute(string xml, string name, out string value)
        {
            value = string.Empty;

            var key = name + "=\"";
            var start = xml.IndexOf(key, StringComparison.Ordinal);
            var quote = '"';

            if (start < 0)
            {
                key = name + "='";
                start = xml.IndexOf(key, StringComparison.Ordinal);
                quote = '\'';
            }

            if (start < 0)
            {
                return false;
            }

            start += key.Length;
            var end = xml.IndexOf(quote, start);
            if (end < 0)
            {
                return false;
            }

            value = xml[start..end].Trim();

            return value.Length > 0;
        }

        /// <summary>Drops nested elements and collapses the whitespace a pretty-printed XMP leaves.</summary>
        private static string StripMarkup(string text)
        {
            var builder = new StringBuilder(text.Length);
            var inside = false;

            foreach (var c in text)
            {
                if (c == '<')
                {
                    inside = true;
                    if (builder.Length > 0 && builder[^1] != ' ')
                    {
                        builder.Append(' ');
                    }

                    continue;
                }

                if (c == '>')
                {
                    inside = false;
                    continue;
                }

                if (!inside)
                {
                    builder.Append(c);
                }
            }

            return string.Join(
                " ",
                builder.ToString().Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
