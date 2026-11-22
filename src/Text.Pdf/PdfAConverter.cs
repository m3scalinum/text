using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Turns a document someone else produced into a PDF/A or PDF/UA one, as far as a rewrite of the
/// existing objects allows.
/// </summary>
/// <remarks>
/// <para>
/// The compiler in this suite already produces PDF/A when it exports one. This is the other case: an
/// archive receives a PDF written by a word processor, a scanner or an invoicing tool and demands
/// PDF/A-2b for retention. What can be established from the file itself is rewritten — the version,
/// the XMP identifier, an output intent with the caller's ICC profile, <c>/MarkInfo</c>, a
/// <c>/ToUnicode</c> CMap for a font whose encoding makes one exact, and the removal of the actions
/// the standard forbids. What cannot be established is refused by name rather than claimed.
/// </para>
/// <para>
/// The rewrite is a rebuild, not an incremental update: objects are copied into a fresh numbering,
/// the cross-reference table is written afresh, and no history survives. Tagging, outlines, named
/// destinations, form fields and signatures travel with the copied catalog; a signature stops
/// verifying, because it covered bytes that no longer exist, which is what every rewrite does.
/// </para>
/// <para>
/// A font program or a structure tree is not something a rewrite can invent. An untagged page has no
/// structure elements, and PDF/A-1a, PDF/A-2a, PDF/A-3a and PDF/UA-1 need them: the conversion
/// refuses with one issue per page. A font with no embedded program is refused for the same reason.
/// </para>
/// <para>
/// PDF/A-4f is produced only from a document that already carries embedded files. Each one must
/// state an <c>/AFRelationship</c>; the catalog gains an <c>/AF</c> array listing them, and the
/// <c>/EmbeddedFiles</c> name tree is rewritten in the order a name tree requires. A document with
/// nothing embedded is refused — the profile is defined by the file it carries — and so is one whose
/// attachment states no relationship, because a relationship cannot be invented for it.
/// </para>
/// <para>
/// The result is checked, never asserted: <see cref="PdfAConversionResult.IsConformant"/> is the
/// verdict of <see cref="PdfConformance.Check"/> on the bytes this method produced. A file this
/// converter rewrote may still fail a rule it could not reach — a missing language, a figure without
/// alternate text — and the report names it. veraPDF remains the reference validator.
/// </para>
/// </remarks>
public static class PdfAConverter
{
    /// <summary>Actions the PDF/A parts forbid wherever they appear.</summary>
    private static readonly HashSet<string> ForbiddenActions = new(StringComparer.Ordinal)
    {
        "Launch",
        "Sound",
        "Movie",
        "ResetForm",
        "ImportData",
        "JavaScript",
        "SubmitForm",
        "Rendition",
        "Trans",
        "GoTo3DView",
        "SetOCGState",
    };

    /// <summary>Converts <paramref name="pdf"/> to the standard <paramref name="options"/> names.</summary>
    /// <param name="pdf">The document to convert, as whole bytes.</param>
    /// <param name="options">The target and the materials the target needs, such as an ICC profile.</param>
    /// <returns>
    /// The converted bytes and what was changed, or no bytes and the steps that could not be taken.
    /// The result also carries the conformance report of the bytes it produced.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The target is not a PDF/A or PDF/UA flavour, the target needs an ICC profile and none was
    /// supplied, or the profile supplied is not one this converter can describe.
    /// </exception>
    /// <exception cref="PdfOperationException">
    /// The document carries an XMP packet outside the bounds metadata is read under — larger than
    /// four megabytes, nested deeper than sixty-four levels, past a hundred thousand nodes, or
    /// declaring a document type. The message names the rule and the value observed. A packet that
    /// is merely not well-formed XML is not a refusal: it is replaced, as a mistyped packet always
    /// was.
    /// </exception>
    public static PdfAConversionResult Convert(ReadOnlySpan<byte> pdf, PdfAConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var requirements = options.Validate();
        var target = options.Target;
        var changes = new List<PdfAConversionChange>();
        var refusals = new List<PdfAConversionRefusal>();

        PdfAConversionResult Refuse(string code, string subject, string message)
        {
            refusals.Add(new PdfAConversionRefusal(code, subject, message));

            return new PdfAConversionResult(target, [], null, changes, refusals);
        }

        var source = pdf.ToArray();

        if (!PdfFile.TryOpen(source, out var file))
        {
            return Refuse(
                PdfAConversionCodes.NotAPdf,
                "header",
                "the bytes do not begin with a %PDF header, so there is nothing to convert");
        }

        if (file.IsEncrypted)
        {
            // Refused before a single object is copied: a rewrite would write plaintext into a file
            // whose trailer still says /Encrypt, and a reader would decrypt the plaintext again.
            return Refuse(
                PdfAConversionCodes.Encrypted,
                "trailer",
                "the document is encrypted, so its objects cannot be rewritten until it is decrypted: "
                + "call PdfEncryption.Open and convert the bytes it returns. Nothing was written");
        }

        if (file.Catalog is not { } catalog)
        {
            return Refuse(
                PdfAConversionCodes.CatalogMissing,
                "trailer",
                "the trailer names no /Root that resolves to a catalog, so there is nothing to convert");
        }

        var pages = file.Pages();
        if (pages.Count == 0)
        {
            return Refuse(
                PdfAConversionCodes.NoPages,
                "page tree",
                "the page tree holds no page, so there is nothing to convert");
        }

        var version = Version(file);
        var targetVersion = TargetVersion(target);

        if (targetVersion < version)
        {
            return Refuse(
                PdfAConversionCodes.VersionAboveTarget,
                "header",
                $"the file declares PDF {version} and {requirements.Name} is PDF {targetVersion}: "
                + $"constructs a later version allows — transparency, object streams, 256-bit AES — cannot be "
                + $"removed by rewriting the objects, so the result could not honestly be claimed as "
                + $"{requirements.Name}. Produce it from a PDF {targetVersion} source, or convert to a flavour "
                + "that allows the version the file declares");
        }

        // PDF/A-4f is not a stricter PDF/A-4: it is the level that permits embedded files of any
        // type, on condition that each one states how it relates to the document. The three
        // conditions the profile adds are checked before anything is copied, so a document that
        // cannot be 4f is refused with the reason rather than rewritten into a file that claims it.
        List<EmbeddedFile> embedded = target is PdfConformanceStandard.PdfA4f or PdfConformanceStandard.PdfA4
            ? EmbeddedFiles(file)
            : [];

        if (target is PdfConformanceStandard.PdfA4f)
        {
            if (embedded.Count == 0)
            {
                return Refuse(
                    PdfAConversionCodes.A4fWithoutEmbeddedFile,
                    "embedded files",
                    "PDF/A-4f is PDF/A-4 with embedded files, and ISO 19005-4 Annex A requires at least one: "
                    + "this document carries none, so the most it can honestly be is PDF/A-4. Add an attachment "
                    + "(PdfAttachments.Add) before converting, or target PDF/A-4");
            }

            foreach (var attachment in embedded)
            {
                if (string.IsNullOrEmpty(attachment.Relationship))
                {
                    refusals.Add(new PdfAConversionRefusal(
                        PdfAConversionCodes.A4fAttachmentWithoutRelationship,
                        $"attachment '{attachment.Name}'",
                        "the attachment declares no /AFRelationship: PDF/A-4f requires every embedded file to state "
                        + "how it relates to the document (/Source, /Data, /Alternative or /Supplement). Nothing was "
                        + "written, because a relationship the document does not state cannot be invented for it"));
                }
                else if (!KnownRelationship(attachment.Relationship))
                {
                    refusals.Add(new PdfAConversionRefusal(
                        PdfAConversionCodes.A4fRelationshipUnknown,
                        $"attachment '{attachment.Name}'",
                        $"the attachment declares /AFRelationship /{attachment.Relationship}, which is not one of "
                        + "the relationships ISO 32000-2 defines (/Source, /Data, /Alternative, /Supplement, "
                        + "/Unspecified), not /Schema, and not a second-class name"));
                }
            }

            if (refusals.Count > 0)
            {
                return new PdfAConversionResult(target, [], null, changes, refusals);
            }
        }

        if (target is PdfConformanceStandard.PdfA4)
        {
            // Base PDF/A-4 allows an embedded file only when that file is itself PDF/A. Whether it is
            // would take a full conformance run on a document inside the document, which a rewrite of
            // this catalog cannot perform, so the claim is refused rather than made.
            foreach (var attachment in embedded)
            {
                refusals.Add(new PdfAConversionRefusal(
                    PdfAConversionCodes.A4WithEmbeddedFiles,
                    $"attachment '{attachment.Name}'",
                    "PDF/A-4 permits only embedded files that are themselves PDF/A-1, PDF/A-2 or PDF/A-4, and a "
                    + $"rewrite cannot establish that of '{attachment.Name}'. Convert to PDF/A-4f, which permits an "
                    + "embedded file of any type, or remove the attachment first"));
            }

            if (refusals.Count > 0)
            {
                return new PdfAConversionResult(target, [], null, changes, refusals);
            }
        }

        if (requirements.RequiresTaggedStructure && !Tagged(file, catalog))
        {
            // One refusal per page, because the reason is per page: the structure tree is the mapping
            // from each page's marked content to an element, and a page that has none cannot acquire
            // one from its drawing instructions.
            foreach (var page in pages)
            {
                refusals.Add(new PdfAConversionRefusal(
                    PdfAConversionCodes.NotTagged,
                    page.Number > 0 ? $"page {page.Number}" : "page",
                    $"{requirements.Name} requires tagged content, and this page carries no structure element: "
                    + "a structure tree records what each mark on the page means, which the page description does "
                    + "not say, so one cannot be invented. Convert a tagged source, or compile the document with "
                    + $"tagging enabled and convert that"));
            }

            return new PdfAConversionResult(target, [], null, changes, refusals);
        }

        var fonts = Fonts(file, pages);
        var needsToUnicode = new List<(int Number, PdfDictionary Font)>();

        foreach (var (number, font) in fonts)
        {
            var subtype = (file.Resolve(font["Subtype"]) as PdfName)?.Value;
            var name = (file.Resolve(font["BaseFont"]) as PdfName)?.Value ?? "unnamed font";
            var subject = number > 0 ? $"obj {number}" : "font resource";

            // A Type3 font draws glyphs with its own content streams, so it is embedded by
            // construction and needs no program of its own.
            if (subtype == "Type3")
            {
                continue;
            }

            if (!Embedded(file, font, subtype))
            {
                refusals.Add(new PdfAConversionRefusal(
                    PdfAConversionCodes.FontNotEmbedded,
                    subject,
                    $"the font {name} has no embedded font program: the glyph outlines are not in the file, and "
                    + "they cannot be recovered from the codes the page draws. Embed the font in the source"));

                continue;
            }

            if (file.Resolve(font["ToUnicode"]) is not null)
            {
                continue;
            }

            if (number > 0 && WinAnsi(file, font, subtype))
            {
                needsToUnicode.Add((number, font));

                continue;
            }

            refusals.Add(new PdfAConversionRefusal(
                PdfAConversionCodes.FontWithoutToUnicode,
                subject,
                $"the font {name} has no /ToUnicode CMap, and its encoding does not determine one: only a "
                + "simple font whose encoding is WinAnsiEncoding can be mapped exactly. Add the CMap in the "
                + "source, or convert a document the compiler produced"));
        }

        if (refusals.Count > 0)
        {
            return new PdfAConversionResult(target, [], null, changes, refusals);
        }

        var strictOpenAction = target is PdfConformanceStandard.PdfA1a
            or PdfConformanceStandard.PdfA1b
            or PdfConformanceStandard.PdfUa1;

        // The actions are removed from the source graph before it is copied, so a forbidden action is
        // not written at all: removing the entry after copying would leave its dictionary behind as
        // an unreferenced object, and a JavaScript payload an archive can never run is still a
        // payload it carries.
        Scrub(file, catalog, requirements.Name, strictOpenAction, changes);

        var writer = new PdfObjectWriter();
        var catalogNumber = writer.Reserve();
        var copier = new PdfObjectCopier(file, writer);

        // The metadata is rewritten from scratch and an output intent is either replaced or kept, so
        // those two entries are not copied; everything else is, which is what carries the tagging,
        // the outlines, the form fields and the page content into the result.
        var skip = new List<string> { "Metadata", "Version" };
        if (options.IccProfile is { Length: > 0 })
        {
            skip.Add("OutputIntents");
        }

        var converted = copier.CopyDictionary(catalog, [.. skip]);
        writer.Set(catalogNumber, converted);

        foreach (var (number, _) in needsToUnicode)
        {
            if (copier.Map(number) is not { } mapped || writer.Get(mapped) is not PdfDictionary font)
            {
                // Not reachable through the page tree the copy walked, so there is nothing to patch.
                refusals.Add(new PdfAConversionRefusal(
                    PdfAConversionCodes.FontWithoutToUnicode,
                    $"obj {number}",
                    "the font is reachable from a page but not from the catalog's page tree, so a /ToUnicode "
                    + "CMap cannot be attached to it"));
                continue;
            }

            var cmap = writer.AddStream(new PdfDictionary(), PdfAWinAnsi.ToUnicodeCMap());
            font.Entries["ToUnicode"] = new PdfReference(cmap);

            changes.Add(new PdfAConversionChange(
                PdfAConversionCodes.ToUnicodeAdded,
                $"obj {mapped}",
                "a /ToUnicode CMap was generated from the font's WinAnsiEncoding"));
        }

        if (refusals.Count > 0)
        {
            return new PdfAConversionResult(target, [], null, changes, refusals);
        }

        AddMetadata(file, catalog, requirements, converted, writer, changes);
        AddOutputIntent(options, converted, writer, changes);

        if (target is PdfConformanceStandard.PdfA4f)
        {
            AddAssociatedFiles(copier, converted, embedded, writer, changes);
        }

        // A document that carries structure elements must say so, at every conformance level: the
        // entry is a single boolean about the catalog, not structure inferred from a page.
        if (converted["StructTreeRoot"] is not null && !Marked(writer, converted))
        {
            var markInfo = new PdfDictionary();
            markInfo.Entries["Marked"] = new PdfBoolean(true);
            converted.Entries["MarkInfo"] = markInfo;

            changes.Add(new PdfAConversionChange(
                PdfAConversionCodes.MarkInfoAdded,
                "catalog",
                "the catalog gained /MarkInfo << /Marked true >>, which the structure tree it already had requires"));
        }

        if (!string.IsNullOrWhiteSpace(options.Language) && converted["Lang"] is null)
        {
            converted.Entries["Lang"] = Text(options.Language);

            changes.Add(new PdfAConversionChange(
                PdfAConversionCodes.LanguageSet,
                "catalog",
                $"/Lang was set to {options.Language}"));
        }

        if (targetVersion != version)
        {
            changes.Insert(0, new PdfAConversionChange(
                PdfAConversionCodes.VersionSet,
                "header",
                $"the header was set from PDF {version} to PDF {targetVersion}, which {requirements.Name} fixes"));
        }

        var trailer = new Dictionary<string, PdfValue>(StringComparer.Ordinal);
        if (file.Trailer["Info"] is { } info)
        {
            trailer["Info"] = copier.Copy(info);
        }

        var result = writer.Build(catalogNumber, targetVersion.Major, targetVersion.Minor, trailer);

        return new PdfAConversionResult(
            target,
            result,
            PdfConformance.Check(result, target),
            changes,
            refusals);
    }

    /// <summary>The version a target fixes, which is the only version it can honestly claim.</summary>
    /// <exception cref="ArgumentException">The target is not an archive or accessibility flavour.</exception>
    private static PdfVersion TargetVersion(PdfConformanceStandard target) => target switch
    {
        PdfConformanceStandard.PdfA1a or PdfConformanceStandard.PdfA1b => new PdfVersion(1, 4),
        PdfConformanceStandard.PdfA2a or PdfConformanceStandard.PdfA2b or PdfConformanceStandard.PdfA2u => new PdfVersion(1, 7),
        PdfConformanceStandard.PdfA3a or PdfConformanceStandard.PdfA3b or PdfConformanceStandard.PdfA3u => new PdfVersion(1, 7),
        PdfConformanceStandard.PdfA4 or PdfConformanceStandard.PdfA4f or PdfConformanceStandard.PdfA4e => new PdfVersion(2, 0),
        PdfConformanceStandard.PdfUa1 => new PdfVersion(1, 7),
        _ => throw new ArgumentException($"{target} is not a PDF/A or PDF/UA flavour", nameof(target)),
    };

    /// <summary>
    /// The version a reader sees: the header, raised by the catalog's <c>/Version</c> when that names
    /// a higher one. It is the same rule <see cref="PdfConformance"/> applies.
    /// </summary>
    private static PdfVersion Version(PdfFile file)
    {
        var version = new PdfVersion(file.MajorVersion, file.MinorVersion);

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

    /// <summary>One embedded file the document carries, as the profile has to see it.</summary>
    /// <param name="Name">The name a reader shows: the name tree key, or the file specification's own name.</param>
    /// <param name="Specification">The file specification, as it appears in the name tree or in <c>/AF</c>.</param>
    /// <param name="Relationship">The <c>/AFRelationship</c> the document states, or <see langword="null"/>.</param>
    private sealed record EmbeddedFile(string Name, PdfValue Specification, string? Relationship);

    /// <summary>The relationships ISO 32000-2 defines, plus the one PDF/A-4 adds.</summary>
    /// <remarks>
    /// ISO 32000-2 Table 43 names <c>Source</c>, <c>Data</c>, <c>Alternative</c>, <c>Supplement</c>
    /// and <c>Unspecified</c>; ISO 19005-4 recommends <c>Schema</c> for a RELAX-NG schema associated
    /// with the document. Any other value is either a second-class name — a registered name with an
    /// underscore, which PDF 2.0 permits — or a value this converter does not know.
    /// </remarks>
    private static readonly HashSet<string> Relationships = new(StringComparer.Ordinal)
    {
        "Source",
        "Data",
        "Alternative",
        "Supplement",
        "Unspecified",
        "Schema",
    };

    /// <summary>Whether <paramref name="relationship"/> is a relationship this converter can vouch for.</summary>
    private static bool KnownRelationship(string relationship)
        => Relationships.Contains(relationship) || relationship.Contains('_', StringComparison.Ordinal);

    /// <summary>
    /// Every embedded file the document carries, from the name tree and from the catalog's
    /// <c>/AF</c> array, each once, in the order the document names them.
    /// </summary>
    /// <remarks>
    /// The two routes are not the same list: a document may list a file in the name tree and never
    /// associate it, or associate a file and never name it. PDF/A-4f needs both views to agree, so
    /// both are read, and the name tree wins the name when a file appears in each.
    /// </remarks>
    private static List<EmbeddedFile> EmbeddedFiles(PdfFile file)
    {
        var files = new List<EmbeddedFile>();
        var specs = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, specification) in EmbeddedNameTree(file))
        {
            if (!names.Add(name))
            {
                continue;
            }

            if (specification is PdfReference reference)
            {
                specs.Add(reference.Number);
            }

            files.Add(Describe(file, name, specification));
        }

        foreach (var specification in AssociatedFiles(file))
        {
            if (specification is PdfReference reference && !specs.Add(reference.Number))
            {
                continue;
            }

            if (file.ResolveDictionary(specification) is not { } dictionary)
            {
                continue;
            }

            var name = TextOf(file, dictionary["UF"]) ?? TextOf(file, dictionary["F"]) ?? string.Empty;
            if (name.Length == 0 || !names.Add(name))
            {
                continue;
            }

            files.Add(Describe(file, name, specification));
        }

        return files;
    }

    /// <summary>Reads one file specification into the name it carries and the relationship it states.</summary>
    private static EmbeddedFile Describe(PdfFile file, string name, PdfValue specification)
    {
        var dictionary = file.ResolveDictionary(specification);
        var stream = EmbeddedStream(file, dictionary);

        // ISO 32000-2 puts /AFRelationship on the file specification; a producer that wrote it on the
        // embedded file stream instead is read too, because it is the same statement in another place.
        var relationship = (file.Resolve(dictionary?["AFRelationship"]) as PdfName)?.Value
            ?? (file.Resolve(stream?.Dictionary["AFRelationship"]) as PdfName)?.Value;

        return new EmbeddedFile(name, specification, relationship);
    }

    /// <summary>The embedded file stream a file specification points at, or <see langword="null"/>.</summary>
    private static PdfStream? EmbeddedStream(PdfFile file, PdfDictionary? specification)
        => file.ResolveDictionary(specification?["EF"]) is { } embedded
            ? file.ResolveStream(embedded["F"]) ?? file.ResolveStream(embedded["UF"])
            : null;

    /// <summary>The <c>/Names /EmbeddedFiles</c> name tree's entries, in the order it lists them.</summary>
    private static List<(string Name, PdfValue Specification)> EmbeddedNameTree(PdfFile file)
    {
        var entries = new List<(string Name, PdfValue Specification)>();

        if (file.ResolveDictionary(file.Catalog?["Names"]) is not { } names ||
            names["EmbeddedFiles"] is not { } root)
        {
            return entries;
        }

        Walk(root, 0);

        return entries;

        void Walk(PdfValue? node, int depth)
        {
            // A name tree is a balanced structure a few levels deep; the bound stops a cyclic /Kids
            // from turning the walk into an infinite one.
            if (depth > 16 || file.ResolveDictionary(node) is not { } dictionary)
            {
                return;
            }

            foreach (var kid in file.ResolveArray(dictionary["Kids"]))
            {
                Walk(kid, depth + 1);
            }

            var items = file.ResolveArray(dictionary["Names"]);
            for (var index = 0; index + 1 < items.Count; index += 2)
            {
                if (TextOf(file, items[index]) is { Length: > 0 } name)
                {
                    entries.Add((name, items[index + 1]));
                }
            }
        }
    }

    /// <summary>The document's associated files, whether <c>/AF</c> is an array or a single value.</summary>
    private static List<PdfValue> AssociatedFiles(PdfFile file)
    {
        var value = file.Catalog?["AF"];

        return file.Resolve(value) switch
        {
            null => [],
            PdfArray array => array.Items,
            _ => [value!],
        };
    }

    /// <summary>Reads a text string or a name as text, or returns <see langword="null"/>.</summary>
    private static string? TextOf(PdfFile file, PdfValue? value) => file.Resolve(value) switch
    {
        PdfString text => text.Decode(),
        PdfName name => name.Value,
        _ => null,
    };

    /// <summary>
    /// Writes the catalog's <c>/AF</c> array and the <c>/Names /EmbeddedFiles</c> tree the profile
    /// requires, each file listed once with the relationship it states.
    /// </summary>
    private static void AddAssociatedFiles(
        PdfObjectCopier copier,
        PdfDictionary converted,
        IReadOnlyList<EmbeddedFile> files,
        PdfObjectWriter writer,
        List<PdfAConversionChange> changes)
    {
        var array = new PdfArray();
        var named = new List<(string Name, PdfValue Specification)>();

        foreach (var embedded in files)
        {
            var copied = copier.Copy(embedded.Specification);
            array.Items.Add(copied);
            named.Add((embedded.Name, copied));

            // A relationship stated on the embedded file stream is legal plain PDF and does not
            // satisfy the profile, which reads it from the file specification. It is stated in both
            // places rather than moved, so nothing the source said is dropped.
            if (copied is PdfReference reference &&
                writer.Get(reference.Number) is PdfDictionary specification &&
                specification["AFRelationship"] is null &&
                embedded.Relationship is { Length: > 0 } relationship)
            {
                specification.Entries["AFRelationship"] = new PdfName(relationship);
            }
        }

        converted.Entries["AF"] = array;

        changes.Add(new PdfAConversionChange(
            PdfAConversionCodes.A4fAssociatedFiles,
            "catalog",
            $"/AF lists {files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} associated "
            + "file(s), each of which states its /AFRelationship"));

        WriteEmbeddedFilesTree(converted, named, writer, changes);
    }

    /// <summary>Writes a fresh, ordered <c>/EmbeddedFiles</c> tree over the copied one.</summary>
    /// <remarks>
    /// A name tree's keys are sorted by their byte strings, and the profile requires the tree to hold
    /// every embedded file. Rebuilding it from the files this conversion listed makes both true at
    /// once, and keeps any other name tree — destinations, JavaScript, structure — untouched.
    /// </remarks>
    private static void WriteEmbeddedFilesTree(
        PdfDictionary converted,
        List<(string Name, PdfValue Specification)> files,
        PdfObjectWriter writer,
        List<PdfAConversionChange> changes)
    {
        var items = new PdfArray();
        foreach (var (name, specification) in files.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            items.Items.Add(Text(name));
            items.Items.Add(specification);
        }

        var tree = new PdfDictionary();
        tree.Entries["Names"] = items;
        var reference = new PdfReference(writer.Add(tree));

        if (Dereference(writer, converted["Names"]) is PdfDictionary names)
        {
            names.Entries["EmbeddedFiles"] = reference;
        }
        else
        {
            var catalogNames = new PdfDictionary();
            catalogNames.Entries["EmbeddedFiles"] = reference;
            converted.Entries["Names"] = new PdfReference(writer.Add(catalogNames));
        }

        changes.Add(new PdfAConversionChange(
            PdfAConversionCodes.A4fEmbeddedFilesTree,
            "catalog",
            $"the /Names /EmbeddedFiles tree was written with {files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} "
            + "name(s), sorted as a name tree requires"));
    }

    /// <summary>Whether the catalog carries the structure a tagged document needs.</summary>
    private static bool Tagged(PdfFile file, PdfDictionary catalog)
        => file.ResolveDictionary(catalog["StructTreeRoot"]) is { } root && root.IsType("StructTreeRoot");

    /// <summary>Whether the catalog already declares <c>/MarkInfo &lt;&lt; /Marked true &gt;&gt;</c>.</summary>
    private static bool Marked(PdfObjectWriter writer, PdfDictionary catalog)
        => Dereference(writer, catalog["MarkInfo"]) is PdfDictionary markInfo
            && Dereference(writer, markInfo["Marked"]) is PdfBoolean { Value: true };

    /// <summary>Whether a font carries its program, directly or through its descendant fonts.</summary>
    private static bool Embedded(PdfFile file, PdfDictionary font, string? subtype)
    {
        if (subtype == "Type0")
        {
            return file.ResolveArray(font["DescendantFonts"])
                .Any(descendant => file.ResolveDictionary(descendant) is { } dictionary && HasProgram(file, dictionary));
        }

        return HasProgram(file, font);
    }

    private static bool HasProgram(PdfFile file, PdfDictionary font)
    {
        var descriptor = file.ResolveDictionary(font["FontDescriptor"]);

        return descriptor is not null
            && (descriptor.ContainsKey("FontFile")
                || descriptor.ContainsKey("FontFile2")
                || descriptor.ContainsKey("FontFile3"));
    }

    /// <summary>
    /// Whether a font's encoding is <c>WinAnsiEncoding</c> with no <c>/Differences</c>, so that a
    /// <c>/ToUnicode</c> CMap built from that table states the mapping the file already has.
    /// </summary>
    private static bool WinAnsi(PdfFile file, PdfDictionary font, string? subtype)
    {
        if (subtype is not ("Type1" or "TrueType" or "MMType1"))
        {
            return false;
        }

        var encoding = file.Resolve(font["Encoding"]);

        string? name = encoding switch
        {
            PdfName direct => direct.Value,
            PdfDictionary dictionary => (file.Resolve(dictionary["BaseEncoding"]) as PdfName)?.Value,
            _ => null,
        };

        if (!string.Equals(name, "WinAnsiEncoding", StringComparison.Ordinal))
        {
            return false;
        }

        // A /Differences array replaces codes with glyphs the table does not describe.
        if (encoding is PdfDictionary overrides && file.Resolve(overrides["Differences"]) is not null)
        {
            return false;
        }

        return true;
    }

    /// <summary>Every font reachable from the pages' resources, including those of form XObjects.</summary>
    private static List<(int Number, PdfDictionary Font)> Fonts(PdfFile file, IReadOnlyList<PdfPage> pages)
    {
        var fonts = new List<(int Number, PdfDictionary Font)>();
        var seenFonts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var seenResources = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (var page in pages)
        {
            Walk(page.Resources, 0);
        }

        return fonts;

        void Walk(PdfDictionary? resources, int depth)
        {
            if (resources is null || depth > 8 || !seenResources.Add(resources))
            {
                return;
            }

            if (file.ResolveDictionary(resources["Font"]) is { } dictionaries)
            {
                foreach (var entry in dictionaries.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    if (file.Resolve(entry.Value) is PdfDictionary font && seenFonts.Add(font))
                    {
                        fonts.Add((entry.Value is PdfReference reference ? reference.Number : 0, font));
                    }
                }
            }

            if (file.ResolveDictionary(resources["XObject"]) is { } xobjects)
            {
                foreach (var entry in xobjects.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    if (file.Resolve(entry.Value) is PdfStream stream &&
                        stream.Dictionary.IsType("XObject") &&
                        (file.Resolve(stream.Dictionary["Subtype"]) as PdfName)?.Value == "Form")
                    {
                        Walk(file.ResolveDictionary(stream.Dictionary["Resources"]), depth + 1);
                    }
                }
            }
        }
    }

    /// <summary>Reads the document's XMP as decoded bytes, or <see langword="null"/> when it carries none.</summary>
    private static byte[]? ReadXmp(PdfFile file, PdfDictionary catalog)
    {
        var stream = file.ResolveStream(catalog["Metadata"]);

        return stream is null ? null : file.Decode(stream);
    }

    /// <summary>Writes the XMP stream that declares the target, and records what it did.</summary>
    private static void AddMetadata(
        PdfFile file,
        PdfDictionary catalog,
        PdfStandardRequirements requirements,
        PdfDictionary converted,
        PdfObjectWriter writer,
        List<PdfAConversionChange> changes)
    {
        var existing = ReadXmp(file, catalog);
        var target = requirements.Standard;
        var bytes = PdfAXmp.Rewrite(
            existing,
            requirements.ArchivePart,
            requirements.ArchiveConformance,
            target == PdfConformanceStandard.PdfUa1 ? 1 : null);

        var dictionary = new PdfDictionary();
        dictionary.Entries["Type"] = new PdfName("Metadata");
        dictionary.Entries["Subtype"] = new PdfName("XML");

        converted.Entries["Metadata"] = new PdfReference(writer.AddStream(dictionary, bytes));

        changes.Add(new PdfAConversionChange(
            existing is null ? PdfAConversionCodes.MetadataAdded : PdfAConversionCodes.MetadataRewritten,
            "xmp",
            existing is null
                ? $"an XMP /Metadata stream was added, declaring {requirements.Name}"
                : $"/Metadata was rewritten to declare {requirements.Name}, keeping the properties it already held"));
    }

    /// <summary>Writes the output intent that carries the caller's ICC profile.</summary>
    private static void AddOutputIntent(
        PdfAConversionOptions options,
        PdfDictionary converted,
        PdfObjectWriter writer,
        List<PdfAConversionChange> changes)
    {
        if (options.IccProfile is not { Length: > 0 } profile)
        {
            // Only PDF/A-4 and PDF/UA-1 can get here, and neither requires an output intent.
            return;
        }

        var (channels, alternate) = ColourSpace(profile);
        var identifier = options.OutputConditionIdentifier ?? "Custom";

        var profileDictionary = new PdfDictionary();
        profileDictionary.Entries["N"] = new PdfNumber(channels);
        if (alternate is not null)
        {
            profileDictionary.Entries["Alternate"] = new PdfName(alternate);
        }

        var profileNumber = writer.AddStream(profileDictionary, profile);

        var intent = new PdfDictionary();
        intent.Entries["Type"] = new PdfName("OutputIntent");
        intent.Entries["S"] = new PdfName("GTS_PDFA1");
        intent.Entries["OutputConditionIdentifier"] = Text(identifier);
        intent.Entries["Info"] = Text(identifier);
        intent.Entries["RegistryName"] = Text("http://www.color.org");
        intent.Entries["DestOutputProfile"] = new PdfReference(profileNumber);

        var intents = new PdfArray();
        intents.Items.Add(new PdfReference(writer.Add(intent)));
        converted.Entries["OutputIntents"] = intents;

        changes.Add(new PdfAConversionChange(
            PdfAConversionCodes.OutputIntentAdded,
            "catalog",
            $"an /OutputIntent was written around the caller's ICC profile ({channels} channel(s), {identifier})"));
    }

    /// <summary>
    /// The channel count and the device space an ICC profile describes, read from the signatures the
    /// format fixes: <c>acsp</c> at byte 36 and the data colour space at byte 16.
    /// </summary>
    /// <exception cref="ArgumentException">The bytes are not a profile this converter can describe.</exception>
    internal static (int Channels, string? Alternate) ColourSpace(byte[] profile)
    {
        if (profile.Length < 132)
        {
            throw new ArgumentException(
                $"an ICC profile is at least 132 bytes; these are {profile.Length}",
                nameof(profile));
        }

        var signature = Encoding.ASCII.GetString(profile, 36, 4);
        if (!string.Equals(signature, "acsp", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"the bytes carry '{signature}' at byte 36 instead of the ICC 'acsp' signature",
                nameof(profile));
        }

        var space = Encoding.ASCII.GetString(profile, 16, 4);

        return space switch
        {
            "RGB " => (3, "DeviceRGB"),
            "CMYK" => (4, "DeviceCMYK"),
            "GRAY" => (1, "DeviceGray"),
            _ => throw new ArgumentException(
                $"the profile's data colour space is '{space}', which is not RGB, CMYK or GRAY",
                nameof(profile)),
        };
    }

    /// <summary>
    /// Removes the actions the target forbids, and — for the flavours that restrict the document's
    /// open action — an <c>/OpenAction</c> that leaves the file.
    /// </summary>
    /// <remarks>
    /// The walk is over the copied graph, so an object reachable twice is visited once and a page's
    /// <c>/Parent</c> cycle terminates. Only the entries that point at an action are removed: the
    /// action dictionary itself may be reachable another way, and a link annotation's own
    /// <c>/URI</c> action is permitted, so it is left alone.
    /// </remarks>
    private static void Scrub(
        PdfFile file,
        PdfDictionary catalog,
        string standard,
        bool strictOpenAction,
        List<PdfAConversionChange> changes)
    {
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PdfDictionary>();
        pending.Push(catalog);

        while (pending.Count > 0)
        {
            var dictionary = pending.Pop();
            if (!visited.Add(dictionary))
            {
                continue;
            }

            var subject = ReferenceEquals(dictionary, catalog) ? "catalog" : "object";

            foreach (var key in new List<string>(dictionary.Entries.Keys))
            {
                var value = dictionary.Entries[key];

                if (key == "JavaScript" && file.Resolve(value) is PdfDictionary tree && tree.ContainsKey("Names"))
                {
                    dictionary.Entries.Remove(key);

                    changes.Add(new PdfAConversionChange(
                        PdfAConversionCodes.JavaScriptRemoved,
                        "catalog",
                        "the /Names /JavaScript name tree was removed: PDF/A forbids JavaScript actions"));

                    continue;
                }

                if (key is ("A" or "AA" or "OpenAction" or "Next" or "PA") &&
                    Carries(file, value, strictOpenAction && key == "OpenAction", out var action))
                {
                    dictionary.Entries.Remove(key);

                    changes.Add(new PdfAConversionChange(
                        PdfAConversionCodes.ActionRemoved,
                        subject,
                        $"a /{key} entry carrying a /{action} action was removed: {standard} does not permit it"));

                    continue;
                }

                foreach (var nested in Nested(file, value))
                {
                    pending.Push(nested);
                }
            }
        }
    }

    /// <summary>Whether <paramref name="value"/> holds an action the target forbids.</summary>
    private static bool Carries(PdfFile file, PdfValue? value, bool goToOnly, out string action)
    {
        var resolved = file.Resolve(value);

        var candidates = new List<PdfDictionary>();
        switch (resolved)
        {
            case PdfDictionary dictionary when dictionary.ContainsKey("S"):
                candidates.Add(dictionary);
                break;
            case PdfDictionary dictionary:
                // /AA holds one action per event; /Next holds an action or an array of them.
                foreach (var entry in dictionary.Entries.Values)
                {
                    if (file.Resolve(entry) is PdfDictionary action1)
                    {
                        candidates.Add(action1);
                    }
                }

                break;
            case PdfArray array:
                foreach (var item in array.Items)
                {
                    if (file.Resolve(item) is PdfDictionary fromArray)
                    {
                        candidates.Add(fromArray);
                    }
                }

                break;
        }

        foreach (var candidate in candidates)
        {
            if (file.Resolve(candidate["S"]) is not PdfName { } name || string.IsNullOrEmpty(name.Value))
            {
                continue;
            }

            var type = name.Value;

            if (ForbiddenActions.Contains(type) || (goToOnly && !string.Equals(type, "GoTo", StringComparison.Ordinal)))
            {
                action = type;

                return true;
            }
        }

        action = string.Empty;

        return false;
    }

    /// <summary>The dictionaries directly inside <paramref name="value"/>, for the walk to follow.</summary>
    private static IEnumerable<PdfDictionary> Nested(PdfFile file, PdfValue? value)
    {
        switch (file.Resolve(value))
        {
            case PdfDictionary dictionary:
                yield return dictionary;
                break;
            case PdfArray array:
                foreach (var item in array.Items)
                {
                    if (file.Resolve(item) is PdfDictionary nested)
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    /// <summary>Follows references inside the writer's own object table.</summary>
    private static PdfValue? Dereference(PdfObjectWriter writer, PdfValue? value)
    {
        for (var depth = 0; depth < 16 && value is PdfReference reference; depth++)
        {
            value = writer.Get(reference.Number);
        }

        return value is PdfNullValue ? null : value;
    }

    /// <summary>A PDF text string: Latin-1 when the text allows it, UTF-16BE otherwise.</summary>
    private static PdfString Text(string text)
        => new(text.All(character => character < 0x80)
            ? Encoding.ASCII.GetBytes(text)
            : [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)]);
}
