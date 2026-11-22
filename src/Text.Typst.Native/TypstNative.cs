using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace M3scalinum.Text.Typst.Native;

/// <summary>Default <see cref="ITypstNative"/>: thin, allocation-conscious marshalling.</summary>
internal sealed class TypstNative : ITypstNative
{
    private static readonly Lazy<TypstNative> LazyInstance = new(() => new TypstNative());

    private readonly string _typstVersion;

    static TypstNative()
    {
        NativeLibraryResolver.Initialize();
    }

    public TypstNative()
    {
        // The load-time contract: a native library outside the accepted range is refused here, with
        // a message that names both revisions, the range and the file, instead of failing later as
        // an EntryPointNotFoundException on whichever symbol changed first (Spec 02 §11).
        AbiContract.Ensure(NativeMethods.AbiVersion(), NativeLibraryResolver.LoadedPath, AbiWarning.Warn);

        _typstVersion = NativeMethods.ReadUtf8(NativeMethods.TypstVersion());
    }

    public static TypstNative Instance => LazyInstance.Value;

    public string TypstVersion => _typstVersion;

    /// <summary>World handles the native library holds right now.</summary>
    /// <remarks>
    /// Diagnostic: a host that builds and disposes worlds can assert that the count returns to what
    /// it was. The process working set cannot answer that question — it is process-wide, and each
    /// platform retains freed native blocks differently.
    /// </remarks>
    public static int LiveWorldCount => (int)NativeMethods.WorldLiveCount();

    /// <summary>Document handles the native library holds right now. See <see cref="LiveWorldCount"/>.</summary>
    public static int LiveDocumentCount => (int)NativeMethods.DocumentLiveCount();

    public TypstWorldHandle CreateWorld(string? configJson)
    {
        var handle = NativeMethods.WorldCreate(configJson, out var error);
        if (handle == IntPtr.Zero)
        {
            NativeCallException.ThrowFromError(error, "create world");
        }

        return new TypstWorldHandle(handle);
    }

    public unsafe void AddSource(TypstWorldHandle world, string path, ReadOnlySpan<byte> utf8)
    {
        var added = false;

        try
        {
            world.DangerousAddRef(ref added);

            fixed (byte* data = utf8)
            {
                var status = NativeMethods.WorldAddSource(
                    world.DangerousGetHandle(), path, data, (nuint)utf8.Length, out var error);
                NativeCallException.ThrowIfFailed(status, error, $"add source '{path}'");
            }
        }
        finally
        {
            if (added)
            {
                world.DangerousRelease();
            }
        }
    }

    public unsafe void AddAsset(TypstWorldHandle world, string path, ReadOnlySpan<byte> bytes)
    {
        var added = false;

        try
        {
            world.DangerousAddRef(ref added);

            fixed (byte* data = bytes)
            {
                var status = NativeMethods.WorldAddAsset(
                    world.DangerousGetHandle(), path, data, (nuint)bytes.Length, out var error);
                NativeCallException.ThrowIfFailed(status, error, $"add asset '{path}'");
            }
        }
        finally
        {
            if (added)
            {
                world.DangerousRelease();
            }
        }
    }

    public unsafe void AddFont(TypstWorldHandle world, ReadOnlySpan<byte> bytes, uint faceIndex)
    {
        var added = false;

        try
        {
            world.DangerousAddRef(ref added);

            fixed (byte* data = bytes)
            {
                var status = NativeMethods.WorldAddFont(
                    world.DangerousGetHandle(), data, (nuint)bytes.Length, faceIndex, out var error);
                NativeCallException.ThrowIfFailed(status, error, "add font");
            }
        }
        finally
        {
            if (added)
            {
                world.DangerousRelease();
            }
        }
    }

    public void ClearSources(TypstWorldHandle world)
        => WithHandle(world, pointer =>
        {
            var status = NativeMethods.WorldClearSources(pointer, out var error);
            NativeCallException.ThrowIfFailed(status, error, "clear sources");
        });

    public TypstDocumentHandle Compile(TypstWorldHandle world, string optionsJson, out string diagnosticsJson)
    {
        var handle = WithHandle(world, pointer =>
        {
            var compiled = NativeMethods.Compile(pointer, optionsJson, out var error);
            if (compiled == IntPtr.Zero)
            {
                NativeCallException.ThrowFromError(error, "compile");
            }

            return compiled;
        });

        var document = new TypstDocumentHandle(handle);
        diagnosticsJson = GetDiagnostics(document);
        return document;
    }

    public byte[] Export(TypstDocumentHandle document, string format, string? optionsJson)
        => WithHandle(document, pointer =>
        {
            var status = NativeMethods.DocumentExport(pointer, format, optionsJson, out var output, out var error);
            NativeCallException.ThrowIfFailed(status, error, $"export '{format}'");
            return NativeCallException.ReadAndFree(output);
        });

    public void ExportTo(TypstDocumentHandle document, string format, string? optionsJson, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // The document is referenced for the whole copy: the reader only borrows the encoded bytes,
        // and a concurrent Dispose must not free the layout under a half-written export.
        WithHandle(document, pointer =>
        {
            var status = NativeMethods.DocumentExportReader(
                pointer, format, optionsJson, out var reader, out var error);
            NativeCallException.ThrowIfFailed(status, error, $"open the export reader for '{format}'");

            try
            {
                CopyChunks(reader, format, destination);
            }
            finally
            {
                NativeMethods.ExportReaderFree(reader);
            }
        });
    }

    private static unsafe void CopyChunks(IntPtr reader, string format, Stream destination)
    {
        const int ChunkSize = 64 * 1024;
        Span<byte> buffer = stackalloc byte[ChunkSize];

        while (true)
        {
            nuint written;

            fixed (byte* data = buffer)
            {
                var status = NativeMethods.ExportReaderRead(
                    reader, data, (nuint)buffer.Length, out written, out var error);
                NativeCallException.ThrowIfFailed(status, error, $"read an export chunk for '{format}'");
            }

            if (written == 0)
            {
                return;
            }

            destination.Write(buffer[..(int)written]);
        }
    }

    public byte[] BundleAsset(TypstDocumentHandle document, int index)
        => WithHandle(document, pointer =>
        {
            var status = NativeMethods.DocumentBundleAsset(pointer, (uint)index, out var output, out var error);
            NativeCallException.ThrowIfFailed(status, error, $"read bundle asset {index}");
            return NativeCallException.ReadAndFree(output);
        });

    public byte[] Evaluate(TypstDocumentHandle document)
        => WithHandle(document, pointer =>
        {
            var status = NativeMethods.DocumentEvaluate(pointer, out var output, out var error);
            NativeCallException.ThrowIfFailed(status, error, "evaluate");
            return NativeCallException.ReadAndFree(output);
        });

    public byte[] Metrics(TypstDocumentHandle document)
        => WithHandle(document, pointer =>
        {
            var status = NativeMethods.DocumentMetrics(pointer, out var output, out var error);
            NativeCallException.ThrowIfFailed(status, error, "read metrics");
            return NativeCallException.ReadAndFree(output);
        });

    public byte[] PageText(TypstDocumentHandle document, int page)
        => WithHandle(document, pointer =>
        {
            var status = NativeMethods.DocumentPageText(pointer, (uint)page, out var output, out var error);
            NativeCallException.ThrowIfFailed(status, error, $"read the text of page {page}");
            return NativeCallException.ReadAndFree(output);
        });

    public byte[] FontFamilies(TypstWorldHandle world)
        => WithHandle(world, pointer =>
        {
            var status = NativeMethods.WorldFontFamilies(pointer, out var output, out var error);
            NativeCallException.ThrowIfFailed(status, error, "read font families");
            return NativeCallException.ReadAndFree(output);
        });

    public TypstWorldHandle CloneWorld(TypstWorldHandle world)
        => WithHandle(world, pointer =>
        {
            var status = NativeMethods.WorldClone(pointer, out var clone, out var error);
            NativeCallException.ThrowIfFailed(status, error, "clone world");
            return new TypstWorldHandle(clone);
        });

    public string GetDiagnostics(TypstDocumentHandle document)
        => WithHandle(document, pointer =>
        {
            var status = NativeMethods.DocumentDiagnostics(pointer, out var json, out var error);
            NativeCallException.ThrowIfFailed(status, error, "read diagnostics");
            return Encoding.UTF8.GetString(NativeCallException.ReadAndFree(json));
        });

    /// <summary>
    /// Runs <paramref name="action"/> with the handle pinned.
    /// </summary>
    /// <remarks>
    /// <see cref="SafeHandle.DangerousGetHandle"/> alone is a use-after-free waiting to happen: a
    /// concurrent <c>Dispose</c> frees the native object while the call is still running. Holding a
    /// reference for the duration of the call makes the handle's own lifetime the only thing that
    /// decides when the native object goes away.
    /// </remarks>
    private static void WithHandle(SafeHandle handle, Action<IntPtr> action)
    {
        var added = false;

        try
        {
            handle.DangerousAddRef(ref added);
            action(handle.DangerousGetHandle());
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }

    /// <inheritdoc cref="WithHandle(SafeHandle, Action{IntPtr})"/>
    private static T WithHandle<T>(SafeHandle handle, Func<IntPtr, T> action)
    {
        var added = false;

        try
        {
            handle.DangerousAddRef(ref added);
            return action(handle.DangerousGetHandle());
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }
}
