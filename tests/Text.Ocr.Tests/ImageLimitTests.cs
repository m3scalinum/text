using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Ocr.Internal;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The ceilings an image is decoded under: the option that lowers them, and what a caller
/// sees when one refuses the input.
/// </summary>
/// <remarks>
/// The dimension checks run in Rust, in <c>native/text-ocr/src/limits.rs</c>, where they are
/// proved against an image whose header declares a size no decoder could allocate. What is
/// proved here is the two halves the managed side owns: the option may only lower a ceiling
/// and is refused rather than clamped when it tries to raise one, and the refusal that
/// crosses the ABI becomes an <see cref="OcrImageLimitException"/> naming the size observed
/// and the ceiling that refused it.
/// </remarks>
public sealed class ImageLimitTests
{
    /// <summary>The native status an image past a ceiling returns.</summary>
    private const int ErrorImageLimit = 8;

    [Fact]
    public void The_defaults_ask_for_no_ceiling_of_their_own()
    {
        var options = new OcrOptions();

        Assert.Equal(0, options.MaximumImagePixels);
        Assert.Equal(0, options.MaximumImageBytes);
        Assert.False(options.RequiresImageLimits);

        // Zero is the library's ceiling, so it is a valid request, not an absent one.
        options.Validate();
    }

    [Fact]
    public void The_hard_maxima_are_an_a4_page_at_six_hundred_dpi_with_room_to_spare()
    {
        // A4 at 600 DPI is 4961 x 7016 = 34 804 576 pixels, so the ceiling is nearly twice
        // the largest page a scanner produces, and it is the same 64 megapixels and 256 MiB
        // Text.Barcodes puts on an image it decodes.
        Assert.Equal(64_000_000, OcrOptions.MaximumImagePixelsCeiling);
        Assert.Equal(256L * 1024 * 1024, OcrOptions.MaximumImageBytesCeiling);
        Assert.True(OcrOptions.MaximumImagePixelsCeiling > 4961L * 7016L);
    }

    [Fact]
    public void A_lowered_ceiling_is_accepted_and_asks_for_the_limited_calls()
    {
        var options = new OcrOptions
        {
            MaximumImagePixels = 1_000_000,
            MaximumImageBytes = 4 * 1024 * 1024,
        };

        options.Validate();

        Assert.True(options.RequiresImageLimits);
    }

    [Fact]
    public void A_ceiling_above_the_hard_maximum_is_refused_rather_than_clamped()
    {
        var pixels = new OcrOptions { MaximumImagePixels = OcrOptions.MaximumImagePixelsCeiling + 1 };
        var pixelError = Assert.Throws<ArgumentOutOfRangeException>(pixels.Validate);
        Assert.Equal("MaximumImagePixels", pixelError.ParamName);
        Assert.Contains("64000000", pixelError.Message, StringComparison.Ordinal);

        var bytes = new OcrOptions { MaximumImageBytes = OcrOptions.MaximumImageBytesCeiling + 1 };
        var byteError = Assert.Throws<ArgumentOutOfRangeException>(bytes.Validate);
        Assert.Equal("MaximumImageBytes", byteError.ParamName);
        Assert.Contains("268435456", byteError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_ceiling_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(new OcrOptions { MaximumImagePixels = -1 }.Validate);
        Assert.Throws<ArgumentOutOfRangeException>(new OcrOptions { MaximumImageBytes = -1 }.Validate);
    }

    [Fact]
    public void A_png_that_declares_forty_thousand_square_is_refused_as_a_typed_limit()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var png = PngDeclaring(40_000, 40_000);

        var status = NativeOcr.Library.ImageAdmit(png, 0, 0, out var json, out var error);

        Assert.Equal(ErrorImageLimit, status);
        Assert.Equal(IntPtr.Zero, json.Pointer);

        var exception = Assert.IsType<OcrImageLimitException>(NativeOcr.ToException(error));

        Assert.Equal("image_limit", exception.Code);
        Assert.Equal(OcrImageLimit.Pixels, exception.Limit);
        Assert.Equal(1_600_000_000L, exception.Observed);
        Assert.Equal(64_000_000L, exception.Maximum);

        // The message names both numbers, so a log line carries the whole story.
        Assert.Contains("40000 x 40000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1600000000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("64000000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_normal_image_is_measured_from_the_size_its_header_states()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var png = PngDeclaring(320, 200);

        var status = NativeOcr.Library.ImageAdmit(png, 0, 0, out var json, out var error);

        Assert.Equal(0, status);
        Assert.Equal(IntPtr.Zero, error);

        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;

            Assert.Equal(320, root.GetProperty("width").GetInt32());
            Assert.Equal(200, root.GetProperty("height").GetInt32());
            Assert.Equal(64_000, root.GetProperty("pixels").GetInt64());
            Assert.Equal("png", root.GetProperty("format").GetString());
            Assert.Equal(64_000_000L, root.GetProperty("maximumPixels").GetInt64());
            Assert.Equal(268_435_456L, root.GetProperty("maximumBytes").GetInt64());
        }
        finally
        {
            NativeOcr.Library.BufferFree(json);
        }
    }

    [Fact]
    public void A_byte_ceiling_lower_than_the_input_refuses_it_on_bytes()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var png = PngDeclaring(320, 200);

        var status = NativeOcr.Library.ImageAdmit(
            png, 0, (nuint)(png.Length - 1), out var json, out var error);

        Assert.Equal(ErrorImageLimit, status);

        var exception = Assert.IsType<OcrImageLimitException>(NativeOcr.ToException(error));

        Assert.Equal(OcrImageLimit.Bytes, exception.Limit);
        Assert.Equal(png.Length, exception.Observed);
        Assert.Equal(png.Length - 1, exception.Maximum);
    }

    [Fact]
    public void The_abi_refuses_a_ceiling_above_the_hard_maximum_without_looking_at_the_image()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var png = PngDeclaring(320, 200);

        var status = NativeOcr.Library.ImageAdmit(
            png, 1_000_000_000, 0, out var json, out var error);

        Assert.Equal(1, status);
        Assert.Equal(IntPtr.Zero, json.Pointer);

        var exception = NativeOcr.ToException(error);
        Assert.Contains("may only be lowered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lowered_ceiling_reaches_the_abi_as_the_caller_set_it()
    {
        var native = new RecordingNative();
        using var handle = OcrEngineHandle.Create(native, [1], [2], null);

        var options = new OcrOptions
        {
            MaximumImagePixels = 1_000_000,
            MaximumImageBytes = 4_096,
        };

        var document = handle.Recognize([0x89, 0x50, 0x4E, 0x47], options);

        Assert.Equal(1_000_000UL, native.MaximumPixels);
        Assert.Equal((nuint)4_096, native.MaximumBytes);
        Assert.Equal("{\"status\":\"recognized\"}", Encoding.UTF8.GetString(document));
    }

    [Fact]
    public void A_call_that_names_no_ceiling_keeps_the_plain_entry_point()
    {
        var native = new RecordingNative();
        using var handle = OcrEngineHandle.Create(native, [1], [2], null);

        handle.Recognize([0x89, 0x50, 0x4E, 0x47], new OcrOptions());

        Assert.False(native.LimitedCallSeen);
        Assert.True(native.PlainCallSeen);
    }

    [Fact]
    public void A_lowered_ceiling_with_preprocessing_takes_the_preprocessed_limited_call()
    {
        var native = new RecordingNative();
        using var handle = OcrEngineHandle.Create(native, [1], [2], null);

        handle.Recognize(
            [0x89, 0x50, 0x4E, 0x47],
            new OcrOptions { MaximumImagePixels = 10, DetectRotation = true });

        Assert.True(native.PreprocessedLimitedSeen);
        Assert.Equal(10UL, native.MaximumPixels);
    }

    [Fact]
    public void A_lowered_ceiling_on_a_language_engine_takes_the_language_limited_call()
    {
        var native = new RecordingNative();
        using var handle = OcrEngineHandle.CreateLanguages(
            native,
            "{\"default\":\"fr\",\"sets\":[{\"language\":\"fr\",\"directory\":\"/w\"}]}"u8.ToArray());

        handle.RecognizeLanguage(
            [0x89, 0x50, 0x4E, 0x47],
            new OcrOptions { MaximumImageBytes = 2_048 },
            "fr");

        Assert.True(native.LanguageLimitedSeen);
        Assert.Equal((nuint)2_048, native.MaximumBytes);
    }

    [Fact]
    public void A_library_that_predates_the_limited_exports_is_reported_as_unsupported()
    {
        var plan = "{\"default\":\"fr\",\"sets\":[{\"language\":\"fr\",\"directory\":\"/w\"}]}"u8.ToArray();

        using var handle = OcrEngineHandle.Create(new RecordingNative { ThrowOnLimited = true }, [1], [2], null);

        var image = Assert.Throws<OcrException>(() => handle.Recognize(
            [0x89, 0x50, 0x4E, 0x47],
            new OcrOptions { MaximumImagePixels = 10, DetectRotation = true }));

        Assert.Equal("unsupported", image.Code);
        Assert.Contains("to_engine_recognize_preprocessed_limited", image.Message, StringComparison.Ordinal);

        using var languages = OcrEngineHandle.CreateLanguages(
            new RecordingNative { ThrowOnLimited = true }, plan);

        var language = Assert.Throws<OcrException>(() => languages.RecognizeLanguage(
            [0x89, 0x50, 0x4E, 0x47],
            new OcrOptions { MaximumImageBytes = 2_048 },
            "fr"));

        Assert.Contains("to_engine_recognize_language_limited", language.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_native_that_cannot_measure_an_image_fails_loudly_rather_than_silently()
    {
        // The interface's own defaults: a double written before the ceilings existed still
        // compiles, and a call that would need one fails instead of pretending it measured.
        IOcrNative bare = new BareNative();

        Assert.Throws<NotSupportedException>(() => bare.ImageAdmit([0x89], 0, 0, out _, out _));

        using var handle = OcrEngineHandle.Create(new BareNative(), [1], [2], null);

        Assert.Throws<NotSupportedException>(
            () => handle.Recognize([0x89], new OcrOptions { MaximumImagePixels = 10 }));

        Assert.Throws<NotSupportedException>(
            () => handle.Recognize([0x89], new OcrOptions { MaximumImagePixels = 10, Deskew = true }));
    }

    [Fact]
    public void The_limited_exports_resolve_and_refuse_a_null_engine()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }

        var png = PngDeclaring(32, 16);

        var plain = NativeOcr.Library.EngineRecognizeLimited(
            IntPtr.Zero, png, 0, 0, 0.6, 0.5, 0.5, out var plainJson, out var plainError);

        Assert.Equal(1, plain);
        Assert.Equal(IntPtr.Zero, plainJson.Pointer);
        Assert.Equal("invalid_argument", NativeOcr.ToException(plainError).Code);

        var preprocessed = NativeOcr.Library.EngineRecognizePreprocessedLimited(
            IntPtr.Zero, png, 0, 0, 0.6, 0.5, 0.5, false, false, false, out _, out var preprocessedError);

        Assert.Equal(1, preprocessed);
        Assert.Equal("invalid_argument", NativeOcr.ToException(preprocessedError).Code);

        var language = NativeOcr.Library.EngineRecognizeLanguageLimited(
            IntPtr.Zero, png, 0, 0, 0.6, 0.5, 0.5, "fr"u8.ToArray(), false, false, false,
            out _, out var languageError);

        Assert.Equal(1, language);
        Assert.Equal("invalid_argument", NativeOcr.ToException(languageError).Code);
    }

    [Fact]
    public void A_ceiling_name_this_package_does_not_know_still_reads_as_a_typed_refusal()
    {
        var native = new BareNative
        {
            ErrorDocument =
                "{\"code\":\"image_limit\",\"message\":\"refused\",\"limit\":\"quarts\","
                + "\"observed\":5,\"maximum\":1}",
        };

        var exception = Assert.IsType<OcrImageLimitException>(
            NativeOcr.ToException(native, new IntPtr(1)));

        Assert.Equal(OcrImageLimit.Unspecified, exception.Limit);
        Assert.Equal(5L, exception.Observed);
        Assert.Equal(1L, exception.Maximum);
    }

    /// <summary>
    /// An <see cref="IOcrNative"/> that answers like the library for the calls an engine
    /// needs, and nothing more: the members the interface gives a default are the defaults.
    /// </summary>
    /// <remarks>
    /// A real engine needs two models that weigh tens of megabytes, and this repository ships
    /// none; a double is what lets the choice between the plain and the limited calls be
    /// observed at all. It is in this test file rather than in the shared fakes so the fakes
    /// stay exactly what they were.
    /// </remarks>
    private class BareNative : IOcrNative
    {
        public uint AbiVersion() => 2;

        public IntPtr BuildInfo() => IntPtr.Zero;

        public IntPtr EngineCreate(
            byte[] detectionModel, byte[] recognitionModel, byte[]? dictionary, out IntPtr error)
        {
            error = IntPtr.Zero;
            return new IntPtr(1);
        }

        public IntPtr EngineCreateLanguages(byte[] plan, out IntPtr error)
        {
            error = IntPtr.Zero;
            return new IntPtr(1);
        }

        public void EngineFree(IntPtr engine)
        {
        }

        public uint EngineLiveCount() => 0;

        public int EngineRecognize(
            IntPtr engine,
            byte[] image,
            double pageConfidence,
            double wordConfidence,
            double maxRejectedWordRatio,
            out NativeBuffer json,
            out IntPtr error)
        {
            error = IntPtr.Zero;
            json = Document("{\"status\":\"recognized\"}");
            return 0;
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
            error = IntPtr.Zero;
            json = Document("{\"pages\":[]}");
            return 0;
        }

        /// <summary>The error document a faked call returns, when a test needs one.</summary>
        internal string? ErrorDocument { get; init; }

        public NativeBuffer ErrorJson(IntPtr error)
            => ErrorDocument is null ? default : Document(ErrorDocument);

        public void ErrorFree(IntPtr error)
        {
        }

        public void BufferFree(NativeBuffer buffer)
        {
            if (buffer.Pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer.Pointer);
            }
        }

        protected static NativeBuffer Document(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);

            return new NativeBuffer { Pointer = pointer, Length = (nuint)bytes.Length };
        }
    }

    /// <summary>The same double, with the limited entry points the ceilings need.</summary>
    private sealed class RecordingNative : BareNative, IOcrNative
    {
        internal bool LimitedCallSeen { get; private set; }

        internal bool PlainCallSeen { get; private set; }

        internal bool PreprocessedLimitedSeen { get; private set; }

        internal bool LanguageLimitedSeen { get; private set; }

        internal bool ThrowOnLimited { get; init; }

        internal ulong MaximumPixels { get; private set; }

        internal nuint MaximumBytes { get; private set; }

        public new int EngineRecognize(
            IntPtr engine,
            byte[] image,
            double pageConfidence,
            double wordConfidence,
            double maxRejectedWordRatio,
            out NativeBuffer json,
            out IntPtr error)
        {
            PlainCallSeen = true;
            error = IntPtr.Zero;
            json = Document("{\"status\":\"recognized\"}");
            return 0;
        }

        public int EngineRecognizeLimited(
            IntPtr engine,
            byte[] image,
            ulong maximumPixels,
            nuint maximumBytes,
            double pageConfidence,
            double wordConfidence,
            double maxRejectedWordRatio,
            out NativeBuffer json,
            out IntPtr error)
        {
            ThrowIfAsked();

            LimitedCallSeen = true;
            MaximumPixels = maximumPixels;
            MaximumBytes = maximumBytes;
            error = IntPtr.Zero;
            json = Document("{\"status\":\"recognized\"}");
            return 0;
        }

        public int EngineRecognizePreprocessedLimited(
            IntPtr engine,
            byte[] image,
            ulong maximumPixels,
            nuint maximumBytes,
            double pageConfidence,
            double wordConfidence,
            double maxRejectedWordRatio,
            bool detectRotation,
            bool deskew,
            bool denoise,
            out NativeBuffer json,
            out IntPtr error)
        {
            ThrowIfAsked();

            PreprocessedLimitedSeen = true;
            MaximumPixels = maximumPixels;
            MaximumBytes = maximumBytes;
            error = IntPtr.Zero;
            json = Document("{\"status\":\"recognized\"}");
            return 0;
        }

        public int EngineRecognizeLanguageLimited(
            IntPtr engine,
            byte[] image,
            ulong maximumPixels,
            nuint maximumBytes,
            double pageConfidence,
            double wordConfidence,
            double maxRejectedWordRatio,
            byte[] language,
            bool detectRotation,
            bool deskew,
            bool denoise,
            out NativeBuffer json,
            out IntPtr error)
        {
            ThrowIfAsked();

            LanguageLimitedSeen = true;
            MaximumPixels = maximumPixels;
            MaximumBytes = maximumBytes;
            error = IntPtr.Zero;
            json = Document("{\"status\":\"recognized\"}");
            return 0;
        }

        private void ThrowIfAsked()
        {
            if (ThrowOnLimited)
            {
                throw new EntryPointNotFoundException("the library predates this export");
            }
        }
    }

    /// <summary>
    /// A PNG whose <c>IHDR</c> announces <paramref name="width"/> x <paramref name="height"/>
    /// and which carries no image data.
    /// </summary>
    /// <remarks>
    /// The chunks are written by hand: a 40 000 x 40 000 PNG would be gigabytes, and the
    /// ceiling exists precisely so the decoder never gets that far. An empty <c>IDAT</c> is
    /// what lets a decoder read the header, because a PNG reader consumes chunks up to the
    /// first one to learn the size.
    /// </remarks>
    private static byte[] PngDeclaring(int width, int height)
    {
        var bytes = new System.Collections.Generic.List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // truecolour

        PushChunk(bytes, "IHDR"u8, header);
        PushChunk(bytes, "IDAT"u8, []);
        PushChunk(bytes, "IEND"u8, []);

        return [.. bytes];
    }

    private static void PushChunk(System.Collections.Generic.List<byte> bytes, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        bytes.AddRange(length);

        var crcInput = new byte[kind.Length + data.Length];
        kind.CopyTo(crcInput);
        data.CopyTo(crcInput.AsSpan(kind.Length));

        bytes.AddRange(kind.ToArray());
        bytes.AddRange(data.ToArray());

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        bytes.AddRange(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in bytes)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                var mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xEDB88320u & mask);
            }
        }

        return ~crc;
    }
}
