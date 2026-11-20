using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using M3scalinum.Text.SharpTypst.Native;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// Stands in for the native library so the public API can be exercised without loading it.
/// Records what reached the boundary instead of asserting on interop internals.
/// </summary>
internal sealed class FakeTypstNative : ITypstNative
{
    public string TypstVersion => "0.15.1-fake";

    public string? WorldConfigJson { get; private set; }

    public string? CompileOptionsJson { get; private set; }

    public List<string> CompileOptions { get; } = [];

    public List<(string Format, string? Options)> Exports { get; } = [];

    public List<(string Format, string? Options)> StreamedExports { get; } = [];

    /// <summary>Chunk size the streaming export is written with, so a test can see several writes.</summary>
    public int StreamChunkSize { get; set; } = 4096;

    public Dictionary<string, byte[]> ExportResults { get; } = [];

    public Dictionary<int, byte[]> BundleAssets { get; } = [];

    public List<int> BundleAssetRequests { get; } = [];

    public string EvaluationJson { get; set; } = """{"pageCount":0,"outline":[],"labels":[],"metadata":[]}""";

    public string FontFamiliesJson { get; set; } = """["Libertinus Serif"]""";

    public List<(string Path, string Text)> Sources { get; } = [];

    public List<(string Path, int Length)> Assets { get; } = [];

    public List<int> Fonts { get; } = [];

    public int ClearSourcesCalls { get; private set; }

    public byte[] ExportBytes { get; set; } = [0x25, 0x50, 0x44, 0x46];

    public string DiagnosticsJson { get; set; } =
        "{\"code\":\"ok\",\"message\":\"\",\"diagnostics\":[]}";

    public NativeCallException? CompileFailure { get; set; }

    public NativeCallException? ExportFailure { get; set; }

    public TypstWorldHandle CreateWorld(string? configJson)
    {
        WorldConfigJson = configJson;
        return new TypstWorldHandle();
    }

    public void AddSource(TypstWorldHandle world, string path, ReadOnlySpan<byte> utf8)
    {
        // The native side inserts by path, so a second call replaces rather than appends.
        var text = System.Text.Encoding.UTF8.GetString(utf8);
        var index = Sources.FindIndex(source => source.Path == path);

        if (index >= 0)
        {
            Sources[index] = (path, text);
        }
        else
        {
            Sources.Add((path, text));
        }
    }

    public void AddAsset(TypstWorldHandle world, string path, ReadOnlySpan<byte> bytes)
        => Assets.Add((path, bytes.Length));

    public void AddFont(TypstWorldHandle world, ReadOnlySpan<byte> bytes, uint faceIndex)
        => Fonts.Add(bytes.Length);

    public void ClearSources(TypstWorldHandle world)
    {
        ClearSourcesCalls++;
        Sources.Clear();
    }

    /// <summary>How long a compile takes, so a timeout can be observed without a heavy document.</summary>
    public TimeSpan CompileDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// When set, a compilation blocks here until the expected number of them have arrived. A test
    /// that asserts two compilations overlap uses this instead of comparing elapsed time: the
    /// thread pool may start the second one late on a small runner, which says nothing about the
    /// gate under test.
    /// </summary>
    public Barrier? CompileBarrier { get; set; }

    public TypstDocumentHandle Compile(TypstWorldHandle world, string optionsJson, out string diagnosticsJson)
    {
        if (CompileBarrier is not null && !CompileBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
        {
            throw new InvalidOperationException(
                "the compilations never overlapped: the gate let only one of them run");
        }

        if (CompileDelay > TimeSpan.Zero)
        {
            Thread.Sleep(CompileDelay);
        }

        CompileOptionsJson = optionsJson;
        CompileOptions.Add(optionsJson);

        if (CompileFailure is not null)
        {
            throw CompileFailure;
        }

        diagnosticsJson = DiagnosticsJson;
        return new TypstDocumentHandle();
    }

    public byte[] Export(TypstDocumentHandle document, string format, string? optionsJson)
    {
        Exports.Add((format, optionsJson));

        if (ExportFailure is not null)
        {
            throw ExportFailure;
        }

        return ExportResults.TryGetValue(format, out var bytes) ? bytes : ExportBytes;
    }

    public void ExportTo(TypstDocumentHandle document, string format, string? optionsJson, Stream destination)
    {
        StreamedExports.Add((format, optionsJson));

        if (ExportFailure is not null)
        {
            throw ExportFailure;
        }

        var bytes = ExportResults.TryGetValue(format, out var custom) ? custom : ExportBytes;

        for (var offset = 0; offset < bytes.Length; offset += StreamChunkSize)
        {
            var count = Math.Min(StreamChunkSize, bytes.Length - offset);
            destination.Write(bytes.AsSpan(offset, count));
        }
    }

    public byte[] BundleAsset(TypstDocumentHandle document, int index)
    {
        BundleAssetRequests.Add(index);

        if (ExportFailure is not null)
        {
            throw ExportFailure;
        }

        return BundleAssets.TryGetValue(index, out var bytes) ? bytes : [];
    }

    public byte[] Evaluate(TypstDocumentHandle document)
    {
        if (ExportFailure is not null)
        {
            throw ExportFailure;
        }

        return Encoding.UTF8.GetBytes(EvaluationJson);
    }

    public byte[] FontFamilies(TypstWorldHandle world)
        => Encoding.UTF8.GetBytes(FontFamiliesJson);

    public TypstWorldHandle CloneWorld(TypstWorldHandle world)
    {
        CloneCount++;
        return new TypstWorldHandle(IntPtr.Zero);
    }

    public int CloneCount { get; private set; }

    public string GetDiagnostics(TypstDocumentHandle document) => DiagnosticsJson;
}
