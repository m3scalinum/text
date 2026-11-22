using System;
using System.IO;
using System.Text;
using M3scalinum.Text.Xlsx;
using Xunit;

namespace M3scalinum.Text.Xlsx.Tests;

/// <summary>What the reader refuses before it ever reaches the native library, and what it wraps.</summary>
public sealed class XlsxReaderGuardTests
{
    [Fact]
    public void A_null_workbook_is_an_argument_error()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        Assert.Throws<ArgumentNullException>(() => XlsxReader.Read((byte[])null!));
    }

    [Fact]
    public void A_null_stream_is_an_argument_error()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        Assert.Throws<ArgumentNullException>(() => XlsxReader.Read((Stream)null!));
    }

    [Fact]
    public void An_empty_workbook_is_an_invalid_argument()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        var exception = Assert.Throws<XlsxException>(() => XlsxReader.Read([]));

        Assert.Equal(XlsxErrorCode.InvalidArgument, exception.Code);
        Assert.Contains("empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_stream_is_an_invalid_argument()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        using var stream = new MemoryStream();

        Assert.Throws<XlsxException>(() => XlsxReader.Read(stream));
    }

    [Fact]
    public void A_stream_over_the_limit_is_refused_while_it_is_being_buffered()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        using var stream = new MemoryStream(XlsxReaderTests.Annexed());

        var exception = Assert.Throws<XlsxException>(
            () => XlsxReader.Read(stream, new XlsxOptions { MaxWorkbookBytes = 16 }));

        Assert.Equal(XlsxErrorCode.Limit, exception.Code);
        Assert.Contains("16-byte limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_over_the_limit_are_refused_by_the_native_reader()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        var exception = Assert.Throws<XlsxException>(
            () => XlsxReader.Read(XlsxReaderTests.Annexed(), new XlsxOptions { MaxWorkbookBytes = 16 }));

        Assert.Equal(XlsxErrorCode.Limit, exception.Code);
        Assert.Contains("16-byte limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_region_over_the_cell_limit_is_refused()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        var exception = Assert.Throws<XlsxException>(
            () => XlsxReader.Read(XlsxReaderTests.Annexed(), new XlsxOptions { MaxCells = 4 }));

        Assert.Equal(XlsxErrorCode.Limit, exception.Code);
        Assert.Contains("4-cell limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_limit_that_is_not_positive_is_refused_before_anything_is_read()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => XlsxReader.Read(XlsxReaderTests.Annexed(), new XlsxOptions { MaxWorkbookBytes = 0 }));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => XlsxReader.Read(XlsxReaderTests.Annexed(), new XlsxOptions { MaxCells = 0 }));
    }

    [Fact]
    public void A_negative_limit_on_a_stream_is_refused_before_it_is_read()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        using var stream = new MemoryStream(XlsxReaderTests.Annexed());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => XlsxReader.Read(stream, new XlsxOptions { MaxWorkbookBytes = -1 }));
    }

    [Fact]
    public void Bytes_that_are_not_a_package_are_a_malformed_package()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        var bytes = Encoding.UTF8.GetBytes("this is a text file, not a workbook");

        var exception = Assert.Throws<XlsxException>(() => XlsxReader.Read(bytes));

        Assert.Equal(XlsxErrorCode.MalformedPackage, exception.Code);
    }

    [Fact]
    public void A_zip_that_is_not_a_workbook_is_a_malformed_package()
    {
        Assert.SkipUnless(XlsxNativeProbe.IsAvailable, XlsxNativeProbe.SkipReason);

        using var buffer = new MemoryStream();

        using (var archive = new System.IO.Compression.ZipArchive(
            buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("readme.txt").Open();
            entry.Write(Encoding.UTF8.GetBytes("nothing else"));
        }

        Assert.Throws<XlsxException>(() => XlsxReader.Read(buffer.ToArray()));
    }

    [Fact]
    public void An_exception_keeps_the_failure_it_wraps()
    {
        var cause = new IOException("the part could not be read");
        var exception = new XlsxException(XlsxErrorCode.Internal, "the tree could not be read", cause);

        Assert.Same(cause, exception.InnerException);
        Assert.Equal(XlsxErrorCode.Internal, exception.Code);
        Assert.Equal("the tree could not be read", exception.Message);
    }

    [Fact]
    public void The_defaults_are_the_ones_the_specification_names()
    {
        var options = new XlsxOptions();

        Assert.Equal(64L * 1024 * 1024, options.MaxWorkbookBytes);
        Assert.Equal(1_000_000, options.MaxCells);
    }
}
