using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// The value types are small but they are what every check reads: a name with an escape, a string
/// that is UTF-16, a number that is not an integer, a null. The tests parse one document that holds
/// them all and assert the value each reader method sees, including the debug text used in a report.
/// </summary>
public sealed class PdfValueTests
{
    private static PdfDictionary Parsed()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R"
                + "/Real 2.5/Whole -3/Array [1 2.5 /name (text)]"
                + "/True true/False false/Nothing null"
                + "/Ref 2 0 R/String <4142>/Empty []/Dict<</Inner 7>>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
        ]);

        Assert.True(PdfFile.TryOpen(pdf, out var file));
        Assert.NotNull(file);

        return Assert.IsType<PdfDictionary>(file.GetObject(1));
    }

    [Fact]
    public void Numbers_keep_their_fraction_and_their_sign()
    {
        var catalog = Parsed();

        var real = Assert.IsType<PdfNumber>(catalog["Real"]);
        var whole = Assert.IsType<PdfNumber>(catalog["Whole"]);

        Assert.Equal(2.5, real.Value);
        Assert.False(real.IsInteger);
        Assert.Equal(-3, whole.Value);
        Assert.True(whole.IsInteger);
        Assert.Equal(-3, whole.AsInteger);
        Assert.Equal("2.5", real.ToString());
    }

    [Fact]
    public void An_array_reports_its_items()
    {
        var array = Assert.IsType<PdfArray>(Parsed()["Array"]);

        Assert.Equal(4, array.Items.Count);
        Assert.Equal("[4 items]", array.ToString());
    }

    [Fact]
    public void A_dictionary_reports_its_size_and_finds_its_entries()
    {
        var catalog = Parsed();
        var inner = Assert.IsType<PdfDictionary>(catalog["Dict"]);

        Assert.True(inner.ContainsKey("Inner"));
        Assert.False(inner.ContainsKey("Outer"));
        Assert.Equal("<1 entries>", inner.ToString());
        Assert.Equal("<12 entries>", catalog.ToString());
    }

    [Fact]
    public void A_boolean_and_a_null_render_as_pdf_keywords()
    {
        var catalog = Parsed();

        Assert.True(Assert.IsType<PdfBoolean>(catalog["True"]).Value);
        Assert.Equal("true", catalog["True"]!.ToString());
        Assert.False(Assert.IsType<PdfBoolean>(catalog["False"]).Value);
        Assert.Equal("false", catalog["False"]!.ToString());

        Assert.Same(PdfValue.Null, catalog["Nothing"]);
        Assert.Equal("null", catalog["Nothing"]!.ToString());
    }

    [Fact]
    public void A_name_renders_with_its_slash_and_a_reference_with_its_object_number()
    {
        var catalog = Parsed();
        var array = Assert.IsType<PdfArray>(catalog["Array"]);
        var reference = Assert.IsType<PdfReference>(catalog["Ref"]);

        Assert.Equal("/name", array.Items[2].ToString());
        Assert.Equal("2 0 R", reference.ToString());
        Assert.Equal(2, reference.Number);
        Assert.Equal("/Catalog", Assert.IsType<PdfName>(catalog["Type"]).ToString());
    }

    [Fact]
    public void A_literal_and_a_hexadecimal_string_both_decode_to_text()
    {
        var catalog = Parsed();
        var array = Assert.IsType<PdfArray>(catalog["Array"]);
        var literal = Assert.IsType<PdfString>(array.Items[3]);
        var hexadecimal = Assert.IsType<PdfString>(catalog["String"]);

        Assert.Equal("text", literal.Text);
        Assert.Equal("AB", hexadecimal.Text);
        Assert.Equal("AB", hexadecimal.Decode());
        Assert.Equal(2, hexadecimal.Bytes.Length);
    }

    [Fact]
    public void A_stream_reports_its_length()
    {
        var pdf = PdfTestDocument.Document(
            ["<</Type/Catalog>>", "<</Length 5>>\nstream\nhello\nendstream"]);

        Assert.True(PdfFile.TryOpen(pdf, out var file));
        var stream = Assert.IsType<PdfStream>(file!.GetObject(2));

        Assert.Equal("stream of 5 bytes", stream.ToString());
        Assert.Equal("hello", Encoding.ASCII.GetString(stream.Data));
        Assert.Single(stream.Dictionary.Entries);
        Assert.Equal(5, Assert.IsType<PdfNumber>(stream.Dictionary["Length"]).AsInteger);
    }

    [Fact]
    public void An_empty_array_reports_no_items()
    {
        var array = Assert.IsType<PdfArray>(Parsed()["Empty"]);

        Assert.Empty(array.Items);
        Assert.Equal("[0 items]", array.ToString());
        Assert.Empty(array.Items.Select(item => item.ToString()));
    }
}
