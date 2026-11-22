using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace M3scalinum.Text.Pdf.Internal;

/// <summary>
/// Resolves <c>sharp_text_pdf</c> from an explicit override, or from the NuGet
/// <c>runtimes/&lt;rid&gt;/native/</c> layout and the application directory.
/// </summary>
/// <remarks>
/// Unlike <c>M3scalinum.Text.Ocr</c>'s resolver this one honours a single variable. The OCR one also
/// accepts <c>SHARPTYPST_NATIVE_PATH</c> because its hosts export one variable per library and it was
/// written before that mattered; loading a different library under this name would resolve no
/// <c>tp_</c> entry point at all, so the fallback is not repeated here.
/// </remarks>
internal static class NativeLibraryResolver
{
    /// <summary>The override a host sets to point at a locally built library.</summary>
    internal const string OverrideVariable = "SHARPTEXTPDF_NATIVE_PATH";

    private static int _installed;

    internal static void Initialize()
    {
        // The runtime throws if a second resolver is installed for the assembly, and every entry
        // point reaches this method.
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    internal static IntPtr Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
        => Resolve(libraryName, OverridePaths(), Candidates());

    /// <summary>
    /// The probe itself, with the override paths and the layout candidates supplied by the caller:
    /// split out so the fallback rules can be exercised without a native library in the test output.
    /// </summary>
    internal static IntPtr Resolve(
        string libraryName,
        IReadOnlyList<string> overrides,
        IReadOnlyList<string> candidates)
    {
        if (!string.Equals(libraryName, PdfRasterNativeMethods.LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        foreach (var overridePath in overrides)
        {
            if (File.Exists(overridePath))
            {
                return NativeLibrary.Load(overridePath);
            }
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return NativeLibrary.Load(candidate);
            }
        }

        // Fall back to the default probing rules; if that fails, the runtime raises
        // DllNotFoundException naming the library, which is the clearest signal we can give.
        return IntPtr.Zero;
    }

    /// <summary>Every override path that is set, in the order they are honoured.</summary>
    internal static IReadOnlyList<string> OverridePaths()
    {
        var path = Environment.GetEnvironmentVariable(OverrideVariable);

        return string.IsNullOrEmpty(path) ? [] : [path];
    }

    internal static string[] Candidates()
        => Candidates(RuntimeIdentifier(), PortableRuntimeIdentifier(), OperatingSystemName(), AppContext.BaseDirectory);

    internal static string[] Candidates(string rid, string portableRid, string os, string baseDirectory)
    {
        var fileName = FileName();

        return
        [
            Path.Combine(baseDirectory, "runtimes", rid, "native", fileName),
            Path.Combine(baseDirectory, "runtimes", portableRid, "native", fileName),
            Path.Combine(baseDirectory, "runtimes", os, "native", fileName),
            Path.Combine(baseDirectory, fileName),
        ];
    }

    internal static string OperatingSystemName()
        => OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : "linux";

    internal static string FileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "sharp_text_pdf.dll";
        }

        return OperatingSystem.IsMacOS() ? "libsharp_text_pdf.dylib" : "libsharp_text_pdf.so";
    }

    internal static string RuntimeIdentifier()
        => IsMusl() ? MuslRuntimeIdentifier() : PortableRuntimeIdentifier();

    internal static string PortableRuntimeIdentifier() => $"{OperatingSystemName()}-{ArchitectureName()}";

    internal static string MuslRuntimeIdentifier() => $"linux-musl-{ArchitectureName()}";

    /// <summary>
    /// Whether this process runs on musl libc, where a glibc binary cannot be loaded.
    /// </summary>
    internal static bool IsMusl()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        var loader = ArchitectureName() switch
        {
            "arm64" => "/lib/ld-musl-aarch64.so.1",
            _ => "/lib/ld-musl-x86_64.so.1",
        };

        return File.Exists(loader) || File.Exists("/etc/alpine-release");
    }

    private static string ArchitectureName() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "unknown",
    };
}

/// <summary>
/// The managed face of the native library: the probe, the error objects, and the out-buffer
/// lifetime. Every call into Rust goes through here.
/// </summary>
internal static class PdfRasterNative
{
    private static int _initialized;

    /// <summary>
    /// Installs the library resolver once, before any P/Invoke runs, so an override path or the
    /// <c>runtimes/&lt;rid&gt;/native/</c> layout is found without a probe call.
    /// </summary>
    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            NativeLibraryResolver.Initialize();
        }
    }

    /// <summary>The build information, or an empty string when the library cannot be loaded.</summary>
    internal static string BuildInfo()
    {
        try
        {
            return PdfRasterNativeMethods.ReadUtf8(PdfRasterNativeMethods.BuildInfo());
        }
        catch (DllNotFoundException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Turns an error object into an exception and frees it.
    /// </summary>
    /// <remarks>
    /// The status code returned alongside the error is a convenience for a C host that has no JSON
    /// reader. The <c>code</c> inside the error document is what <see cref="PdfRasterException.Error"/>
    /// reports, so the two halves can gain a status without the managed side learning a status table.
    /// </remarks>
    internal static PdfRasterException ToException(IntPtr error)
    {
        var code = "unknown";
        var message = "the native library failed without an error object";

        if (error != IntPtr.Zero)
        {
            try
            {
                var buffer = PdfRasterNativeMethods.ErrorJson(error);
                try
                {
                    var text = Encoding.UTF8.GetString(buffer.ToArray());
                    using var document = JsonDocument.Parse(text);
                    var root = document.RootElement;

                    if (root.TryGetProperty("code", out var codeElement))
                    {
                        code = codeElement.GetString() ?? code;
                    }

                    if (root.TryGetProperty("message", out var messageElement))
                    {
                        message = messageElement.GetString() ?? message;
                    }
                }
                finally
                {
                    PdfRasterNativeMethods.BufferFree(buffer);
                }
            }
            finally
            {
                PdfRasterNativeMethods.ErrorFree(error);
            }
        }

        return new PdfRasterException(Map(code), message);
    }

    /// <summary>The managed enum for the code a native error document names.</summary>
    internal static PdfRasterError Map(string code) => code switch
    {
        "not_a_pdf" => PdfRasterError.NotAPdf,
        "encrypted" => PdfRasterError.Encrypted,
        "page_out_of_range" => PdfRasterError.PageOutOfRange,
        "limit" => PdfRasterError.Limit,
        "unsupported" => PdfRasterError.Unsupported,
        "invalid_argument" => PdfRasterError.InvalidArgument,
        "internal" => PdfRasterError.Internal,
        "panic" => PdfRasterError.Panic,
        _ => PdfRasterError.Internal,
    };
}
