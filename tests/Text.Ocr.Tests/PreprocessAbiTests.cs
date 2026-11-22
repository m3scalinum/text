using System;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The two preprocessing exports of the native library, against the real library: the
/// symbols resolve, and a null engine is refused rather than dereferenced.
/// </summary>
/// <remarks>
/// These run only where <c>sharp_text_ocr</c> loads, so they skip (loudly) elsewhere. What
/// they add over <see cref="PreprocessOptionTests"/> is the one thing a double cannot
/// prove: that the library on disk really exports the two entry points this package calls.
/// </remarks>
public sealed class PreprocessAbiTests
{
    [Fact]
    public void The_library_exports_the_preprocessed_entry_points()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var imageStatus = NativeOcr.Library.EngineRecognizePreprocessed(
            IntPtr.Zero, [], 0.6, 0.5, 0.5, true, true, true, out var imageDocument, out var imageError);
        var pdfStatus = NativeOcr.Library.EngineRecognizePdfPreprocessed(
            IntPtr.Zero, [], 300, 0, 0, 0.6, 0.5, 0.5, true, true, true, out var pdfDocument, out var pdfError);

        Assert.NotEqual(0, imageStatus);
        Assert.NotEqual(0, pdfStatus);
        Assert.Equal(imageStatus, NativeMethods.ErrorCode(imageError));
        Assert.Equal(pdfStatus, NativeMethods.ErrorCode(pdfError));
        Assert.Equal(0, (int)imageDocument.Length);
        Assert.Equal(0, (int)pdfDocument.Length);

        var exception = NativeOcr.ToException(imageError);
        var pdfException = NativeOcr.ToException(pdfError);

        Assert.Equal("invalid_argument", exception.Code);
        Assert.Equal("invalid_argument", pdfException.Code);
        Assert.Contains("engine must not be null", exception.Message, StringComparison.Ordinal);
    }
}
