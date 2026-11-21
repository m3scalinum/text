using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Filling and flattening, on a form added to a page the compiler produced. The value is read back
/// from the field dictionary, not from the caller's input, and the appearance is read back from the
/// stream that was written, so a test cannot pass on state the output does not carry.
/// </summary>
public sealed class PdfFormTests
{
    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", name);

        Assert.True(File.Exists(path), $"the fixture {name} is missing from {path}");

        return File.ReadAllBytes(path);
    }

    private static byte[] Ua1() => Fixture("ua1.pdf");

    private static PdfFile Open(byte[] pdf)
    {
        Assert.True(PdfFile.TryOpen(pdf, out var file), "the produced bytes were not read as a PDF");
        Assert.Empty(file!.Problems);

        return file;
    }

    private static PdfDictionary Field(PdfFile file, string name)
    {
        var form = file.ResolveDictionary(file.Catalog!["AcroForm"]);
        Assert.NotNull(form);

        foreach (var item in file.ResolveArray(form!["Fields"]))
        {
            if (file.Resolve(item) is PdfDictionary field &&
                (file.Resolve(field["T"]) as PdfString)?.Decode() == name)
            {
                return field;
            }
        }

        throw new InvalidOperationException($"the field '{name}' is missing from the output");
    }

    private static string Text(PdfFile file, PdfValue? value)
        => (file.Resolve(value) as PdfString)?.Decode() ?? string.Empty;

    private static string Content(PdfFile file, PdfPage page)
    {
        var builder = new StringBuilder();
        var contents = page.Dictionary["Contents"];
        var items = contents is PdfArray array ? array.Items : [contents ?? PdfValue.Null];

        foreach (var item in items)
        {
            var stream = file.ResolveStream(item);
            if (stream is not null && file.Decode(stream) is { } data)
            {
                builder.Append(Encoding.Latin1.GetString(data));
            }
        }

        return builder.ToString();
    }

    [Fact]
    public void Filling_a_text_field_writes_the_value_and_redraws_the_appearance()
    {
        var source = TestForm.WithTextField(Ua1(), "Customer", "initial");

        var filled = PdfForm.Fill(source, new Dictionary<string, string>
        {
            ["Customer"] = "ACME Corporation",
        });

        var file = Open(filled);
        var field = Field(file, "Customer");

        Assert.Equal("ACME Corporation", Text(file, field["V"]));

        var forms = file.ResolveDictionary(field["AP"]);
        Assert.NotNull(forms);

        var appearance = file.ResolveStream(forms!["N"]);
        Assert.NotNull(appearance);
        Assert.Contains("ACME Corporation", Encoding.Latin1.GetString(file.Decode(appearance!)!), StringComparison.Ordinal);

        // The page the field sits on still renders its own text.
        Assert.Contains("Rapport", PdfTextProbe.Page(filled, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void Filling_an_unknown_field_is_refused()
    {
        var source = TestForm.WithTextField(Ua1(), "Customer", "initial");

        var missing = Assert.Throws<ArgumentException>(() => PdfForm.Fill(
            source,
            new Dictionary<string, string> { ["Nobody"] = "value" }));

        Assert.Contains("Nobody", missing.Message, StringComparison.Ordinal);
        Assert.Throws<PdfOperationException>(() => PdfForm.Fill(
            Ua1(),
            new Dictionary<string, string> { ["Customer"] = "value" }));
    }

    [Fact]
    public void Filling_a_checkbox_selects_the_named_state()
    {
        var source = TestForm.WithCheckBox(Ua1(), "Agree");

        var filled = Open(PdfForm.Fill(source, new Dictionary<string, string> { ["Agree"] = "Yes" }));
        var field = Field(filled, "Agree");

        Assert.Equal("Yes", (filled.Resolve(field["V"]) as PdfName)?.Value);
        Assert.Equal("Yes", (filled.Resolve(field["AS"]) as PdfName)?.Value);

        var off = Open(PdfForm.Fill(source, new Dictionary<string, string> { ["Agree"] = "Off" }));
        var offField = Field(off, "Agree");

        Assert.Equal("Off", (off.Resolve(offField["V"]) as PdfName)?.Value);
        Assert.Equal("Off", (off.Resolve(offField["AS"]) as PdfName)?.Value);

        Assert.Throws<PdfOperationException>(() => PdfForm.Fill(
            source,
            new Dictionary<string, string> { ["Agree"] = "Maybe" }));
    }

    [Fact]
    public void Flattening_draws_the_appearance_into_the_page_and_removes_the_form()
    {
        var source = TestForm.WithTextField(Ua1(), "Customer", "Flatten me");

        var result = PdfForm.Flatten(source);

        Assert.True(result.IsComplete);
        Assert.Equal(["Customer"], result.Flattened);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void Flattening_removes_the_form_from_the_catalog()
    {
        var source = TestForm.WithTextField(Ua1(), "Customer", "Flatten me");
        var flattened = PdfForm.Flatten(source);
        var file = Open(flattened.Document);

        Assert.True(flattened.IsComplete);
        Assert.False(file.Catalog!.ContainsKey("AcroForm"));

        var page = file.Pages()[0];
        var content = Content(file, page);

        Assert.Matches(@"/M3Form\d+_0 Do", content);

        var resources = file.ResolveDictionary(page.Dictionary["Resources"]);
        Assert.NotNull(resources);

        var xobjects = file.ResolveDictionary(resources!["XObject"]);
        Assert.NotNull(xobjects);
        Assert.Contains(xobjects!.Entries.Keys, name => name.StartsWith("M3Form", StringComparison.Ordinal) && name.EndsWith("_0", StringComparison.Ordinal));

        var form = file.ResolveStream(xobjects.Entries.First(pair => pair.Key.EndsWith("_0", StringComparison.Ordinal)).Value);
        Assert.NotNull(form);
        Assert.Contains(
            "Flatten me",
            Encoding.Latin1.GetString(file.Decode(form!)!),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            file.ResolveArray(page.Dictionary["Annots"]),
            item => file.Resolve(item) is PdfDictionary annotation &&
                    (file.Resolve(annotation["Subtype"]) as PdfName)?.Value == "Widget");

        Assert.Contains("Rapport", PdfTextProbe.Page(flattened.Document, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_without_an_appearance_is_left_interactive_and_reported()
    {
        var source = TestForm.WithTextField(Ua1(), "Customer", "keep me", withAppearance: false);

        var result = PdfForm.Flatten(source);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Flattened);

        var skip = Assert.Single(result.Skipped);
        Assert.Equal("Customer", skip.FieldName);
        Assert.Contains("appearance", skip.Reason, StringComparison.Ordinal);

        var file = Open(source);
        Assert.True(file.Catalog!.ContainsKey("AcroForm"));
        Assert.Equal("keep me", Text(file, Field(file, "Customer")["V"]));
    }

    [Fact]
    public void A_signature_field_is_neither_filled_nor_flattened()
    {
        var source = TestForm.WithSignatureField(Ua1(), "Signature1");

        Assert.Throws<PdfOperationException>(() => PdfForm.Fill(
            source,
            new Dictionary<string, string> { ["Signature1"] = "value" }));

        var result = PdfForm.Flatten(source);

        Assert.False(result.IsComplete);
        Assert.Contains(result.Skipped, skip => skip.FieldName == "Signature1");
        Assert.True(Open(source).Catalog!.ContainsKey("AcroForm"));
    }

    [Fact]
    public void A_document_without_a_form_has_nothing_to_flatten()
    {
        var result = PdfForm.Flatten(Ua1());

        Assert.True(result.IsComplete);
        Assert.Empty(result.Flattened);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void An_empty_form_is_removed()
    {
        var pdf = PdfTestDocument.Document(
            [
                "<</Type/Catalog/Pages 2 0 R/AcroForm<</Fields[]>>>>",
                "<</Type/Pages/Count 1/Kids[3 0 R]>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            ]);

        var result = PdfForm.Flatten(pdf);

        Assert.True(result.IsComplete);
        Assert.False(Open(result.Document).Catalog!.ContainsKey("AcroForm"));
    }

    [Fact]
    public void An_encrypted_document_cannot_be_filled()
    {
        var encrypted = PdfTestDocument.Document(
            [
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Count 1/Kids[3 0 R]>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            ],
            "/Encrypt<</Filter/Standard/V 1/R 2/O<00>/U<00>/P -1>>");

        Assert.Throws<PdfOperationException>(() => PdfForm.Fill(
            encrypted,
            new Dictionary<string, string> { ["Customer"] = "value" }));
        Assert.Throws<PdfOperationException>(() => PdfForm.Flatten(encrypted));
    }
}
