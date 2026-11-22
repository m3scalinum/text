using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// The model refusals end to end: a real directory, real files, and the native
/// library deciding.
/// </summary>
/// <remarks>
/// These need <c>sharp_text_ocr</c> to load and nothing else — no weights. That is
/// the point of the feature: a file that is not a model of the right role is
/// refused by its container and its header, which is checkable without the forty
/// megabytes the real models weigh. What is *not* proved here is that a real model
/// still loads: that needs the weights and lives in <see cref="ModelBackedTests"/>.
/// </remarks>
public sealed class NativeModelTests
{
    private const string DetectionName = "detection.rten";
    private const string RecognitionName = "recognition.rten";

    [Fact]
    public void A_directory_missing_the_detection_model_names_the_file_it_expected()
    {
        RequireNative();

        var directory = CreateDirectory("missing-detection");
        try
        {
            File.WriteAllBytes(Path.Combine(directory, RecognitionName), RtenContainer(8192));

            var exception = Assert.Throws<OcrException>(
                () => OcrModelSet.LoadFromDirectory(directory));

            Assert.Equal(OcrModelDefect.MissingFile, exception.Defect);
            Assert.Equal(OcrModelRole.Detection, exception.Role);
            Assert.Equal(Path.Combine(directory, DetectionName), exception.FilePath);
            Assert.Contains(DetectionName, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_file_that_is_not_a_model_is_refused_with_its_role_and_its_first_bytes()
    {
        RequireNative();

        var directory = CreateDirectory("junk");
        try
        {
            File.WriteAllBytes(Path.Combine(directory, DetectionName), Encoding.ASCII.GetBytes("not a model"));
            File.WriteAllBytes(Path.Combine(directory, RecognitionName), RtenContainer(8192));

            var exception = Assert.Throws<OcrException>(
                () => OcrModelSet.LoadFromDirectory(directory));

            Assert.Equal(OcrModelDefect.UnrecognizedFormat, exception.Defect);
            Assert.Equal(OcrModelRole.Detection, exception.Role);
            Assert.Equal(Path.Combine(directory, DetectionName), exception.FilePath);
            Assert.Contains("6e 6f 74 20", exception.Message, StringComparison.Ordinal);
            Assert.Contains("RTEN magic", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_truncated_download_is_refused_by_its_declared_length_before_the_loader()
    {
        RequireNative();

        var directory = CreateDirectory("truncated");
        try
        {
            var whole = RtenContainer(8192);
            File.WriteAllBytes(Path.Combine(directory, DetectionName), whole[..100]);
            File.WriteAllBytes(Path.Combine(directory, RecognitionName), RtenContainer(8192));

            var exception = Assert.Throws<OcrException>(
                () => OcrModelSet.LoadFromDirectory(directory));

            Assert.Equal(OcrModelDefect.InconsistentLayout, exception.Defect);
            Assert.Contains("truncated", exception.Message, StringComparison.Ordinal);
            Assert.Contains("100-byte file", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_complete_directory_passes_the_preflight_and_its_unreadable_graph_is_refused_by_the_loader()
    {
        RequireNative();

        var directory = CreateDirectory("well-formed-container");
        try
        {
            File.WriteAllBytes(Path.Combine(directory, DetectionName), RtenContainer(8192));
            File.WriteAllBytes(Path.Combine(directory, RecognitionName), RtenContainer(16384));

            var set = OcrModelSet.LoadFromDirectory(directory);

            Assert.Equal(OcrModelRole.Detection, set.Identity.Detection.Role);
            Assert.Equal("rten", set.Identity.Detection.Format);
            Assert.Equal(64, set.Identity.Detection.Sha256.Length);
            Assert.Equal(64, set.Identity.Recognition.Sha256.Length);
            Assert.NotEqual(set.Identity.Detection.Sha256, set.Identity.Recognition.Sha256);

            // The header is right but the graph inside is zeros. The loader refuses
            // it, and the refusal is typed and named — not a panic under a page.
            var exception = Assert.Throws<OcrException>(() => OcrEngine.FromDirectory(directory));

            Assert.Equal(OcrModelDefect.UnparsableGraph, exception.Defect);
            Assert.Equal(OcrModelRole.Detection, exception.Role);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_directory_that_does_not_exist_is_refused_by_its_path()
    {
        RequireNative();

        var absent = Path.Combine(Path.GetTempPath(), "sharp-text-ocr-" + Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<OcrException>(() => OcrEngine.FromDirectory(absent));

        Assert.Equal(OcrModelDefect.MissingDirectory, exception.Defect);
        Assert.Contains(absent, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_named_as_a_directory_is_refused_as_not_a_directory()
    {
        RequireNative();

        var directory = CreateDirectory("a-file");
        try
        {
            var file = Path.Combine(directory, "models.txt");
            File.WriteAllText(file, "hello");

            var exception = Assert.Throws<OcrException>(
                () => OcrModelSet.LoadFromDirectory(file));

            Assert.Equal(OcrModelDefect.NotADirectory, exception.Defect);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void RequireNative()
    {
        if (!NativeProbe.IsAvailable)
        {
            Assert.Skip(NativeProbe.SkipReason);
        }
    }

    private static string CreateDirectory(string label)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharp-text-ocr-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// A file whose header is a well-formed <c>RTEN</c> v2 header for a 1 KB graph,
    /// padded to <paramref name="length"/>. The graph is zeros, so the container
    /// check passes and the loader is the one that refuses it.
    /// </summary>
    private static byte[] RtenContainer(int length)
    {
        var bytes = new byte[length];

        Encoding.ASCII.GetBytes("RTEN").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 2u);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8, 8), 32u);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16, 8), 1024u);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24, 8), 32u + 1024u);

        return bytes;
    }
}
