using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Conversion is judged on bytes, not on intent: every test here either converts a document the real
/// compiler produced and re-checks the result with <see cref="PdfConformance"/>, or converts one that
/// cannot reach the target and asserts the exact reason it gave. The fixture documents are the same
/// real Typst output the conformance suite uses.
/// </summary>
public sealed class PdfAConverterTests
{
    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", name);

        Assert.True(File.Exists(path), $"the fixture {name} is missing from {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// The sRGB profile the compiler embedded in its own PDF/A-2b, reused as the caller-supplied
    /// profile: this package ships none, and the test needs one that is really an ICC profile.
    /// </summary>
    private static byte[] IccProfile()
    {
        var pdf = Fixture("a2b.pdf");

        Assert.True(PdfFile.TryOpen(pdf, out var file));
        Assert.NotNull(file.Catalog);

        var intents = file.ResolveArray(file.Catalog["OutputIntents"]);
        Assert.NotEmpty(intents);

        var intent = file.ResolveDictionary(intents[0]);
        Assert.NotNull(intent);

        var stream = file.ResolveStream(intent["DestOutputProfile"]);
        Assert.NotNull(stream);

        var profile = file.Decode(stream) ?? stream.Data;

        Assert.Equal("acsp", Encoding.ASCII.GetString(profile, 36, 4));

        return profile;
    }

    private static PdfAConversionOptions Options(PdfConformanceStandard target, bool icc = true)
        => new() { Target = target, IccProfile = icc ? IccProfile() : null };

    /// <summary>
    /// The document rebuilt as it was, minus the catalog entries named: a producer that tagged, run
    /// without tagging, differs from this only in the objects left behind.
    /// </summary>
    private static byte[] Rebuild(byte[] pdf, params string[] drop)
    {
        Assert.True(PdfFile.TryOpen(pdf, out var file));

        var writer = new PdfObjectWriter();
        var catalogNumber = writer.Reserve();
        var copier = new PdfObjectCopier(file, writer);
        var catalog = copier.CopyDictionary(file.Catalog!);

        foreach (var key in drop)
        {
            catalog.Entries.Remove(key);
        }

        writer.Set(catalogNumber, catalog);

        if (drop.Contains("StructTreeRoot", StringComparer.Ordinal))
        {
            // A page of an untagged document carries no /StructParents; without the root the entry
            // would point at a parent tree that no longer exists.
            for (var number = 1; number <= writer.Count; number++)
            {
                if (writer.Get(number) is PdfDictionary { } page && page.IsType("Page"))
                {
                    page.Entries.Remove("StructParents");
                }
            }
        }

        var trailer = new Dictionary<string, PdfValue>(StringComparer.Ordinal);
        if (file.Trailer["Info"] is { } info)
        {
            trailer["Info"] = copier.Copy(info);
        }

        return writer.Build(catalogNumber, file.MajorVersion, file.MinorVersion, trailer);
    }

    /// <summary>Rewrites the header version, keeping every byte offset where it was.</summary>
    private static byte[] WithVersion(byte[] pdf, string version)
    {
        var text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-", text, StringComparison.Ordinal);
        Assert.Equal(3, version.Length);

        return Encoding.Latin1.GetBytes(string.Concat("%PDF-", version, text.AsSpan(8)));
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static PdfAConversionRefusal TheOnlyRefusal(PdfAConversionResult result, string code)
    {
        var refusal = Assert.Single(result.Refusals);
        Assert.Equal(code, refusal.Code);

        return refusal;
    }

    [Fact]
    public void A_compiler_PDF_A_2b_becomes_a_conformant_PDF_A_3b()
    {
        var result = PdfAConverter.Convert(Fixture("a2b.pdf"), Options(PdfConformanceStandard.PdfA3b));

        Assert.Empty(result.Refusals);
        Assert.True(result.HasBytes);
        Assert.True(result.IsConformant, result.Report?.ToString());

        var report = Assert.IsType<PdfConformanceReport>(result.Report);
        Assert.Equal(PdfConformanceStandard.PdfA3b, report.Standard);
        Assert.Empty(report.Issues);
        Assert.Equal(new PdfVersion(1, 7), report.Version.GetValueOrDefault());

        // The page content, the structure tree and the fonts survive: the conversion is a rebuild,
        // not a re-render, so the text a reader extracts is the text that was there.
        Assert.True(PdfFile.TryOpen(result.Bytes, out var converted));
        Assert.Equal(2, converted.Pages().Count);
        Assert.NotNull(converted.Catalog?["StructTreeRoot"]);

        var text = PdfText.Extract(result.Bytes);
        Assert.Equal(2, text.PageCount);
        Assert.Contains("Rapport de conformité", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_identifier_of_an_existing_XMP_packet_is_replaced_not_duplicated()
    {
        var result = PdfAConverter.Convert(Fixture("a2b.pdf"), Options(PdfConformanceStandard.PdfA3b));
        var text = Encoding.Latin1.GetString(result.Bytes);

        Assert.Equal(1, Count(text, "pdfaid:part"));
        Assert.Equal(1, Count(text, "pdfaid:part=\"3\""));
        Assert.Equal(0, Count(text, "pdfaid:part=\"2\""));
        Assert.Equal(1, Count(text, "pdfaid:conformance=\"B\""));

        // The rest of the packet is the document's: a conversion that replaced it would lose the
        // title, the creator and the language the producer declared.
        Assert.Contains("dc:title", text, StringComparison.Ordinal);
        Assert.Contains("Typst 0.15.1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_untagged_document_is_refused_page_by_page_for_PDF_A_2a()
    {
        // PDF/A-2b does not require tagging, so the same bytes convert; the "a" level does, and a
        // structure tree cannot be inferred from the page description.
        var untagged = Rebuild(Fixture("a2b.pdf"), "StructTreeRoot", "MarkInfo");
        var result = PdfAConverter.Convert(untagged, Options(PdfConformanceStandard.PdfA2a));

        Assert.False(result.HasBytes);
        Assert.Null(result.Report);
        Assert.False(result.IsConformant);
        Assert.Equal(2, result.Refusals.Count);
        Assert.All(result.Refusals, refusal => Assert.Equal(PdfAConversionCodes.NotTagged, refusal.Code));
        Assert.Contains(result.Refusals, refusal => refusal.Target.StartsWith("page ", StringComparison.Ordinal));
        Assert.All(result.Refusals, refusal => Assert.Contains("structure", refusal.Message, StringComparison.Ordinal));

        // The same untagged bytes reach the basic level, where tagging is not a requirement.
        var basic = PdfAConverter.Convert(untagged, Options(PdfConformanceStandard.PdfA2b));

        Assert.True(basic.IsConformant, basic.Report?.ToString());
    }

    [Fact]
    public void A_document_with_an_unembedded_font_is_refused()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Resources<</Font<</F1 4 0 R>>>>>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>",
        ]);

        var result = PdfAConverter.Convert(pdf, Options(PdfConformanceStandard.PdfA2b));

        Assert.False(result.HasBytes);
        var refusal = TheOnlyRefusal(result, PdfAConversionCodes.FontNotEmbedded);
        Assert.Equal("obj 4", refusal.Target);
        Assert.Contains("Helvetica", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_encrypted_document_is_refused_before_anything_is_written()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "encryption", "qpdf-r6-user-password.pdf");
        var encrypted = File.ReadAllBytes(path);

        var result = PdfAConverter.Convert(encrypted, Options(PdfConformanceStandard.PdfA2b));

        Assert.False(result.HasBytes);
        Assert.False(result.IsConformant);
        Assert.Empty(result.Changes);

        var refusal = TheOnlyRefusal(result, PdfAConversionCodes.Encrypted);
        Assert.Equal("trailer", refusal.Target);
        Assert.Contains("PdfEncryption", refusal.Message, StringComparison.Ordinal);

        // Decrypting first is the path the refusal names.
        var opened = PdfEncryption.Open(encrypted, "asdfzxcv");
        var converted = PdfAConverter.Convert(opened.Pdf, Options(PdfConformanceStandard.PdfA2b));

        Assert.True(converted.HasBytes);
        Assert.DoesNotContain(converted.Refusals, refusal => refusal.Code == PdfAConversionCodes.Encrypted);
    }

    [Fact]
    public void The_same_input_and_options_produce_the_same_bytes()
    {
        var pdf = Fixture("a2b.pdf");
        var options = Options(PdfConformanceStandard.PdfA3b);

        var first = PdfAConverter.Convert(pdf, options);
        var second = PdfAConverter.Convert(pdf, options);

        Assert.True(first.IsConformant);
        Assert.Equal(first.Bytes, second.Bytes);
        Assert.Equal(
            first.Changes.Select(change => change.Code),
            second.Changes.Select(change => change.Code));
    }

    [Fact]
    public void A_document_that_still_breaks_a_rule_reports_the_error_instead_of_claiming_success()
    {
        // PDF/UA-1 needs a natural language. Removing it leaves a document the converter can rewrite
        // — and must not call conformant.
        var untagged = Rebuild(Fixture("a2b.pdf"), "Lang");
        var result = PdfAConverter.Convert(untagged, Options(PdfConformanceStandard.PdfUa1, icc: false));

        Assert.True(result.HasBytes);
        Assert.Empty(result.Refusals);
        Assert.False(result.IsConformant);

        var report = Assert.IsType<PdfConformanceReport>(result.Report);
        var issue = Assert.Single(report.Errors);
        Assert.Equal(PdfConformanceCodes.LanguageMissing, issue.Code);
        Assert.Contains(result.Errors, reported => reported.Code == PdfConformanceCodes.LanguageMissing);

        // Supplying the language is what closes it, and only the checker decides that it did.
        var fixedResult = PdfAConverter.Convert(
            untagged,
            new PdfAConversionOptions { Target = PdfConformanceStandard.PdfUa1, Language = "fr" });

        Assert.True(fixedResult.IsConformant, fixedResult.Report?.ToString());
        Assert.Contains(fixedResult.Changes, change => change.Code == PdfAConversionCodes.LanguageSet);
        Assert.Contains("pdfuaid:part=\"1\"", Encoding.Latin1.GetString(fixedResult.Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void A_PDF_UA_1_target_adds_the_pdfuaid_identifier_and_keeps_the_structure()
    {
        var result = PdfAConverter.Convert(Fixture("ua1.pdf"), Options(PdfConformanceStandard.PdfUa1, icc: false));

        Assert.True(result.IsConformant, result.Report?.ToString());
        Assert.Equal(PdfConformanceStandard.PdfUa1, result.Report!.Standard);
        Assert.Equal(1, Count(Encoding.Latin1.GetString(result.Bytes), "pdfuaid:part"));
        Assert.Contains(result.Changes, change => change.Code == PdfAConversionCodes.MetadataRewritten);
    }

    [Fact]
    public void A_PDF_A_target_without_an_ICC_profile_is_a_caller_mistake()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => PdfAConverter.Convert(Fixture("a2b.pdf"), new PdfAConversionOptions { Target = PdfConformanceStandard.PdfA2b }));

        Assert.Contains("OutputIntent", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IccProfile", exception.Message, StringComparison.Ordinal);

        // PDF/A-4 needs no output intent, so the same call is allowed without a profile.
        var result = PdfAConverter.Convert(Fixture("a2b.pdf"), new PdfAConversionOptions { Target = PdfConformanceStandard.PdfA4 });

        Assert.True(result.HasBytes);
        Assert.True(result.IsConformant, result.Report?.ToString());
        Assert.Equal(new PdfVersion(2, 0), result.Report!.Version.GetValueOrDefault());
    }

    [Fact]
    public void A_document_above_the_version_the_target_fixes_is_refused()
    {
        // The PDF/A-4 fixture declares 2.0. PDF/A-2b is 1.7, and a rewrite cannot remove what PDF 2.0
        // allowed, so the answer is a refusal rather than a claim veraPDF would contradict.
        var result = PdfAConverter.Convert(Fixture("a4.pdf"), Options(PdfConformanceStandard.PdfA2b));

        Assert.False(result.HasBytes);
        var refusal = TheOnlyRefusal(result, PdfAConversionCodes.VersionAboveTarget);
        Assert.Equal("header", refusal.Target);
        Assert.Contains("2.0", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("1.7", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_simple_font_whose_encoding_is_WinAnsi_gains_a_ToUnicode_CMap()
    {
        // PDF/A needs a Unicode mapping for every font that is drawn. This font is embedded, and its
        // encoding is a published table, so the CMap states the mapping the file already has instead
        // of being guessed.
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Resources<</Font<</F1 4 0 R>>>>>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding/FontDescriptor 5 0 R>>",
            "<</Type/FontDescriptor/FontName/Helvetica/FontFile2 6 0 R>>",
            "<</Length 4>>\nstream\nabcd\nendstream",
        ]);

        var result = PdfAConverter.Convert(pdf, Options(PdfConformanceStandard.PdfA2b));

        Assert.True(result.IsConformant, result.Report?.ToString());
        Assert.Contains(result.Changes, change => change.Code == PdfAConversionCodes.ToUnicodeAdded);

        var text = Encoding.Latin1.GetString(result.Bytes);
        Assert.Contains("begincmap", text, StringComparison.Ordinal);
        Assert.Contains("<41><0041>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_JavaScript_action_is_removed_from_the_document_it_is_reachable_from()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/OpenAction 4 0 R/Names<</JavaScript 5 0 R>>>>",
            "<</Type/Pages/Count 1/Kids[3 0 R]>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            "<</S/JavaScript/JS(app.alert(1))>>",
            "<</Names[(seed)<</S/JavaScript/JS(app.alert(2))>>]>>",
        ]);

        var result = PdfAConverter.Convert(pdf, Options(PdfConformanceStandard.PdfA2b));

        Assert.True(result.IsConformant, result.Report?.ToString());
        Assert.Contains(result.Changes, change => change.Code == PdfAConversionCodes.JavaScriptRemoved);
        Assert.Contains(result.Changes, change => change.Code == PdfAConversionCodes.ActionRemoved);

        // The source is stripped before it is copied, so the payloads are not in the result at all,
        // not merely unreachable from it.
        var text = Encoding.Latin1.GetString(result.Bytes);
        Assert.False(text.Contains("app.alert", StringComparison.Ordinal));
        Assert.False(text.Contains("/JavaScript", StringComparison.Ordinal));
    }

    [Fact]
    public void A_document_level_URI_open_action_is_removed_for_PDF_A_1_and_kept_otherwise()
    {
        // PDF/A-1 permits an /OpenAction only when it is a GoTo: one that leaves the file is not an
        // open action an archive can vouch for. PDF/A-2 permits it, so it is not touched.
        var pdf = WithVersion(
            PdfTestDocument.Document(
            [
                "<</Type/Catalog/Pages 2 0 R/OpenAction 4 0 R>>",
                "<</Type/Pages/Count 1/Kids[3 0 R]>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
                "<</S/URI/URI(https://example.com/invoice)>>",
            ]),
            "1.4");

        var strict = PdfAConverter.Convert(pdf, Options(PdfConformanceStandard.PdfA1b));

        Assert.True(strict.IsConformant, strict.Report?.ToString());
        Assert.Contains(strict.Changes, change => change.Code == PdfAConversionCodes.ActionRemoved);

        Assert.True(PdfFile.TryOpen(strict.Bytes, out var strictFile));
        Assert.Null(strictFile.Catalog?["OpenAction"]);

        var relaxed = PdfAConverter.Convert(pdf, Options(PdfConformanceStandard.PdfA2b));

        Assert.True(relaxed.IsConformant, relaxed.Report?.ToString());
        Assert.DoesNotContain(relaxed.Changes, change => change.Code == PdfAConversionCodes.ActionRemoved);

        Assert.True(PdfFile.TryOpen(relaxed.Bytes, out var relaxedFile));
        Assert.NotNull(relaxedFile.Catalog?["OpenAction"]);
    }

    [Fact]
    public void A_conversion_that_cannot_reach_the_target_says_so_through_EnsureConformant()
    {
        var untagged = Rebuild(Fixture("a2b.pdf"), "StructTreeRoot", "MarkInfo");
        var result = PdfAConverter.Convert(untagged, Options(PdfConformanceStandard.PdfA3a));

        var exception = Assert.Throws<PdfConformanceException>(result.EnsureConformant);

        Assert.False(exception.Report.IsConformant);
        Assert.Contains(exception.Report.Issues, issue => issue.Code == PdfAConversionCodes.NotTagged);
    }
}
