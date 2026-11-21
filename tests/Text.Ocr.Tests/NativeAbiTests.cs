using System;
using System.Text.Json;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// Whether the native library can be loaded in this process, and how to say so.
/// </summary>
internal static class NativeProbe
{
    internal const string SkipReason =
        "sharp_text_ocr is not loadable here: build native/text-ocr and point " +
        "M3SCALINUM_OCR_NATIVE_PATH at the library to run the native tests.";

    internal static bool IsAvailable
    {
        get
        {
            try
            {
                NativeOcr.Initialize();
                return NativeMethods.AbiVersion() == 1;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}

/// <summary>
/// The ABI itself: the handshake, the build information, and the error path.
/// </summary>
/// <remarks>
/// These run without a model, which is why they can run at all: they prove the
/// library loads, the exports resolve, an error crosses the boundary as an error
/// object rather than a crash, and the managed side turns it into an exception.
/// They do not prove that text is recognised: that needs the models, and
/// <see cref="ModelBackedTests"/> is where it is proved.
/// </remarks>
public sealed class NativeAbiTests
{
    [Fact]
    public void The_native_library_reports_abi_version_one()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        Assert.Equal(1u, NativeMethods.AbiVersion());
        Assert.Equal(1u, NativeOcr.Library.AbiVersion());
    }

    [Fact]
    public void The_native_library_names_the_engine_it_carries()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        using var document = JsonDocument.Parse(NativeOcr.BuildInfo());
        var root = document.RootElement;

        Assert.Equal("0.14", root.GetProperty("ocrs").GetString());
        Assert.Equal("0.28", root.GetProperty("rten").GetString());
        Assert.Equal("0.9", root.GetProperty("hayro").GetString());
    }

    [Fact]
    public void A_model_that_is_not_a_model_is_refused_rather_than_crashing()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var error = Assert.Throws<OcrException>(
            () => OcrEngine.FromModels([1, 2, 3, 4], [1, 2, 3, 4]));

        Assert.Equal("model", error.Code);
        Assert.Contains("detection model", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_engine_handle_is_leaked_by_a_failed_creation()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var before = NativeOcr.LiveHandleCount();

        Assert.Throws<OcrException>(() => OcrEngine.FromModels([1, 2, 3, 4], [1, 2, 3, 4]));

        Assert.Equal(before, NativeOcr.LiveHandleCount());
    }

    [Fact]
    public void The_recognize_entry_points_refuse_a_null_engine_instead_of_dereferencing_it()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var imageStatus = NativeOcr.Library.EngineRecognize(
            IntPtr.Zero, [], out var imageDocument, out var imageError);
        var pdfStatus = NativeOcr.Library.EngineRecognizePdf(
            IntPtr.Zero, [], 300, 0, 0, out var pdfDocument, out var pdfError);

        Assert.NotEqual(0, imageStatus);
        Assert.NotEqual(0, pdfStatus);
        Assert.Equal(imageStatus, NativeMethods.ErrorCode(imageError));
        Assert.Equal(pdfStatus, NativeMethods.ErrorCode(pdfError));
        Assert.Equal(0, (int)imageDocument.Length);
        Assert.Equal(0, (int)pdfDocument.Length);

        var imageException = NativeOcr.ToException(imageError);
        var pdfException = NativeOcr.ToException(pdfError);

        Assert.Equal("invalid_argument", imageException.Code);
        Assert.Equal("invalid_argument", pdfException.Code);
        Assert.Contains("engine must not be null", imageException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Freeing_a_null_engine_is_harmless()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var before = NativeOcr.LiveHandleCount();

        NativeOcr.Library.EngineFree(IntPtr.Zero);

        Assert.Equal(before, NativeOcr.LiveHandleCount());
    }
}
