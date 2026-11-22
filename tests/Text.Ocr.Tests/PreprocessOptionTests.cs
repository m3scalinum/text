using System;
using System.Text;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The options that reach the ABI: which call is made, and with which of the three
/// preprocessing flags.
/// </summary>
/// <remarks>
/// The three stages run in Rust and are proved there, in
/// <c>native/text-ocr/src/preprocess.rs</c>. What the managed half owns is the mapping:
/// a switch a caller set must arrive at the ABI as the flag it names, and a page with
/// nothing switched on must take the call revision 2 already exported rather than a new
/// one. That is what this file asserts, against a double, without a model.
/// </remarks>
public sealed class PreprocessOptionTests
{
    [Fact]
    public void Every_preprocessing_stage_is_off_by_default()
    {
        var options = new OcrOptions();

        Assert.False(options.DetectRotation);
        Assert.False(options.Deskew);
        Assert.False(options.Denoise);
        Assert.False(options.RequiresPreprocessing);

        // PDF options inherit the switches, so one set governs images and pages.
        Assert.False(new OcrPdfOptions().RequiresPreprocessing);
    }

    [Fact]
    public void A_page_with_no_stage_switched_on_takes_the_call_of_revision_two()
    {
        var native = new PreprocessNative();
        using var handle = OcrEngineHandle.Create(native, [1], [2], null);

        handle.Recognize([9], new OcrOptions());

        Assert.Equal(1, native.PlainRecognizeCalls);
        Assert.Equal(0, native.PreprocessRecognizeCalls);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void Each_switch_reaches_the_abi_as_the_flag_it_names(bool rotation, bool deskew, bool denoise)
    {
        var native = new PreprocessNative();
        using var handle = OcrEngineHandle.Create(native, [1], [2], null);

        handle.Recognize([9], new OcrOptions
        {
            DetectRotation = rotation,
            Deskew = deskew,
            Denoise = denoise,
        });

        Assert.Equal(1, native.PreprocessRecognizeCalls);
        Assert.Equal(0, native.PlainRecognizeCalls);
        Assert.Equal(rotation, native.LastDetectRotation);
        Assert.Equal(deskew, native.LastDeskew);
        Assert.Equal(denoise, native.LastDenoise);
    }

    [Fact]
    public void The_thresholds_and_the_image_cross_the_same_call_the_preprocessing_flags_do()
    {
        var native = new PreprocessNative();
        using var handle = OcrEngineHandle.Create(native, [1], [2], null);

        handle.Recognize([4, 5, 6], new OcrOptions
        {
            MinimumPageConfidence = 0.7,
            MinimumWordConfidence = 0.4,
            MaximumRejectedWordRatio = 0.25,
            DetectRotation = true,
        });

        Assert.Equal([4, 5, 6], native.Inner.LastImage);
        Assert.Equal(0.7, native.Inner.LastPageConfidence);
        Assert.Equal(0.4, native.Inner.LastWordConfidence);
        Assert.Equal(0.25, native.Inner.LastMaxRejectedWordRatio);
    }

    [Fact]
    public void A_page_of_a_pdf_carries_the_flags_of_its_own_options()
    {
        var native = new PreprocessNative();
        using var handle = OcrEngineHandle.Create(native, [1], [2], null);

        handle.RecognizePdf([7], new OcrPdfOptions { Dpi = 200, Deskew = true }, 1, 3);

        Assert.Equal(1, native.PreprocessPdfCalls);
        Assert.Equal(0, native.PlainPdfCalls);
        Assert.True(native.LastDeskew);
        Assert.False(native.LastDetectRotation);
        Assert.Equal(200u, native.Inner.LastDpi);
        Assert.Equal(1u, native.Inner.LastFirstPage);
        Assert.Equal(3u, native.Inner.LastPageCount);
    }

    [Fact]
    public void Asking_a_library_that_predates_the_exports_for_preprocessing_names_the_export()
    {
        var native = new PreprocessNative { PreprocessingUnavailable = true };
        using var handle = OcrEngineHandle.Create(native, [1], [2], null);

        var error = Assert.Throws<OcrException>(
            () => handle.Recognize([9], new OcrOptions { DetectRotation = true }));

        Assert.Equal("unsupported", error.Code);
        Assert.Contains("to_engine_recognize_preprocessed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_double_that_never_learned_to_preprocess_refuses_loudly()
    {
        // The default interface implementation is what stops an older double from
        // silently dropping a stage; the failure must not be a page read at the wrong angle.
        var library = new FakeNativeLibrary();
        using var handle = OcrEngineHandle.Create(library, [1], [2], null);

        Assert.Throws<NotSupportedException>(
            () => handle.Recognize([9], new OcrOptions { Denoise = true }));
    }
}

/// <summary>
/// An <see cref="IOcrNative"/> that records the preprocessing flags as well as
/// forwarding everything else to <see cref="FakeNativeLibrary"/>.
/// </summary>
internal sealed class PreprocessNative : IOcrNative
{
    internal FakeNativeLibrary Inner { get; } = new();

    /// <summary>When set, the two preprocess entry points report a missing export.</summary>
    internal bool PreprocessingUnavailable { get; set; }

    internal int PlainRecognizeCalls { get; private set; }

    internal int PreprocessRecognizeCalls { get; private set; }

    internal int PlainPdfCalls { get; private set; }

    internal int PreprocessPdfCalls { get; private set; }

    internal bool LastDetectRotation { get; private set; }

    internal bool LastDeskew { get; private set; }

    internal bool LastDenoise { get; private set; }

    public uint AbiVersion() => Inner.AbiVersion();

    public IntPtr BuildInfo() => Inner.BuildInfo();

    public IntPtr EngineCreate(byte[] detectionModel, byte[] recognitionModel, byte[]? dictionary, out IntPtr error)
        => Inner.EngineCreate(detectionModel, recognitionModel, dictionary, out error);

    public void EngineFree(IntPtr engine) => Inner.EngineFree(engine);

    public uint EngineLiveCount() => Inner.EngineLiveCount();

    public int EngineRecognize(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error)
    {
        PlainRecognizeCalls++;
        return Inner.EngineRecognize(
            engine, image, pageConfidence, wordConfidence, maxRejectedWordRatio, out json, out error);
    }

    public int EngineRecognizePdf(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        out NativeBuffer json,
        out IntPtr error)
    {
        PlainPdfCalls++;
        return Inner.EngineRecognizePdf(
            engine, pdf, dpi, firstPage, pageCount,
            pageConfidence, wordConfidence, maxRejectedWordRatio, out json, out error);
    }

    public int EngineRecognizePreprocessed(
        IntPtr engine,
        byte[] image,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
    {
        PreprocessRecognizeCalls++;
        Record(detectRotation, deskew, denoise);

        if (PreprocessingUnavailable)
        {
            throw new EntryPointNotFoundException("to_engine_recognize_preprocessed");
        }

        return Inner.EngineRecognize(
            engine, image, pageConfidence, wordConfidence, maxRejectedWordRatio, out json, out error);
    }

    public int EngineRecognizePdfPreprocessed(
        IntPtr engine,
        byte[] pdf,
        uint dpi,
        uint firstPage,
        uint pageCount,
        double pageConfidence,
        double wordConfidence,
        double maxRejectedWordRatio,
        bool detectRotation,
        bool deskew,
        bool denoise,
        out NativeBuffer json,
        out IntPtr error)
    {
        PreprocessPdfCalls++;
        Record(detectRotation, deskew, denoise);

        if (PreprocessingUnavailable)
        {
            throw new EntryPointNotFoundException("to_engine_recognize_pdf_preprocessed");
        }

        return Inner.EngineRecognizePdf(
            engine, pdf, dpi, firstPage, pageCount,
            pageConfidence, wordConfidence, maxRejectedWordRatio, out json, out error);
    }

    public NativeBuffer ErrorJson(IntPtr error) => Inner.ErrorJson(error);

    public void ErrorFree(IntPtr error) => Inner.ErrorFree(error);

    public void BufferFree(NativeBuffer buffer) => Inner.BufferFree(buffer);

    private void Record(bool detectRotation, bool deskew, bool denoise)
    {
        LastDetectRotation = detectRotation;
        LastDeskew = deskew;
        LastDenoise = denoise;
    }
}
