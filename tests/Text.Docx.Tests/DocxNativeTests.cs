using M3scalinum.Text.Docx;
using M3scalinum.Text.Docx.Internal;
using Xunit;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>The native calls underneath the reader, driven through the real library.</summary>
public sealed class DocxNativeTests
{
    [Fact]
    public void The_media_count_is_the_number_of_parts_the_ast_lists()
    {
        using var document = DocxNative.Parse(DocxFixture.Build(), DocxNative.OptionsJson(true, DocxOptions.DefaultMaxDocumentBytes));

        Assert.Equal(1, DocxNative.ImageCount(document));
    }

    [Fact]
    public void A_handle_the_document_does_not_own_is_an_invalid_argument()
    {
        using var document = DocxNative.Parse(DocxFixture.Build(), DocxNative.OptionsJson(true, DocxOptions.DefaultMaxDocumentBytes));

        var exception = Assert.Throws<DocxException>(() => DocxNative.ReadImage(document, 9_999));

        Assert.Equal(DocxErrorCode.InvalidArgument, exception.Code);
        Assert.Contains("9999", exception.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_options_payload_is_the_json_the_native_reader_parses()
    {
        Assert.Equal(
            """{"images": false, "maxBytes": 4096}""",
            DocxNative.OptionsJson(false, 4096));
    }
}
