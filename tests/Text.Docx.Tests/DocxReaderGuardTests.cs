using System.IO;
using System.Text;
using M3scalinum.Text.Docx;
using Xunit;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>What the reader refuses, and what it does with the edges of its options.</summary>
public sealed class DocxReaderGuardTests
{
    [Fact]
    public void A_null_package_is_an_argument_error()
    {
        Assert.Throws<System.ArgumentNullException>(() => DocxReader.Read((byte[])null!));
    }

    [Fact]
    public void A_null_stream_is_an_argument_error()
    {
        Assert.Throws<System.ArgumentNullException>(() => DocxReader.Read((Stream)null!));
    }

    [Fact]
    public void An_empty_stream_is_an_invalid_argument()
    {
        using var stream = new MemoryStream();

        var exception = Assert.Throws<DocxException>(() => DocxReader.Read(stream));

        Assert.Equal(DocxErrorCode.InvalidArgument, exception.Code);
    }

    [Fact]
    public void A_stream_over_the_limit_is_refused_while_it_is_being_buffered()
    {
        using var stream = new MemoryStream(DocxFixture.Build());

        var exception = Assert.Throws<DocxException>(
            () => DocxReader.Read(stream, new DocxOptions { MaxDocumentBytes = 16 }));

        Assert.Equal(DocxErrorCode.Limit, exception.Code);
        Assert.Contains("16-byte limit", exception.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_limit_on_a_stream_is_refused_before_it_is_read()
    {
        using var stream = new MemoryStream(DocxFixture.Build());

        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => DocxReader.Read(stream, new DocxOptions { MaxDocumentBytes = 0 }));
    }

    [Fact]
    public void A_stream_that_is_not_a_package_is_a_malformed_package()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a package"));

        Assert.Throws<DocxException>(() => DocxReader.Read(stream));
    }

    [Fact]
    public void An_exception_keeps_the_failure_it_wraps()
    {
        var cause = new IOException("the part could not be read");
        var exception = new DocxException(DocxErrorCode.Internal, "the tree could not be read", cause);

        Assert.Same(cause, exception.InnerException);
        Assert.Equal(DocxErrorCode.Internal, exception.Code);
        Assert.Equal("the tree could not be read", exception.Message);
    }
}
