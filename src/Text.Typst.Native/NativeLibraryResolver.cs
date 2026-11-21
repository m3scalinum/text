using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Typst.Native;

/// <summary>
/// Resolves <c>sharp_typst</c> from the NuGet <c>runtimes/&lt;rid&gt;/native/</c> layout, the
/// application directory, or an explicit override (Spec 02 §9).
/// </summary>
internal static class NativeLibraryResolver
{
    private const string OverrideVariable = "SHARPTYPST_NATIVE_PATH";

    /// <summary>
    /// Installs the resolver. Called from <see cref="TypstNative"/>'s static constructor,
    /// which every entry point goes through, so no module initializer is needed.
    /// </summary>
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, NativeMethods.LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
        {
            return NativeLibrary.Load(overridePath);
        }

        foreach (var candidate in Candidates())
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

    internal static string[] Candidates()
        => Candidates(RuntimeIdentifier(), PortableRuntimeIdentifier(), OperatingSystemName(), AppContext.BaseDirectory);

    /// <summary>
    /// The probing order, with every input explicit so the ordering can be pinned by a test on any
    /// platform. A musl host falls back to the glibc build through the RID graph, and the library
    /// then fails to load, so the musl artifact is probed before the portable one.
    /// </summary>
    internal static string[] Candidates(string rid, string portableRid, string os, string baseDirectory)
    {
        var fileName = FileName();

        return
        [
            Path.Combine(baseDirectory, "runtimes", rid, "native", fileName),
            Path.Combine(baseDirectory, "runtimes", portableRid, "native", fileName),
            // A universal macOS dylib ships under `osx`, not `osx-arm64`/`osx-x64`.
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
            return "sharp_typst.dll";
        }

        return OperatingSystem.IsMacOS() ? "libsharp_typst.dylib" : "libsharp_typst.so";
    }

    /// <summary>
    /// The RID of this process: <c>linux-musl-x64</c> on a musl libc host, <c>linux-x64</c>
    /// otherwise.
    /// </summary>
    internal static string RuntimeIdentifier()
        => IsMusl() ? MuslRuntimeIdentifier() : PortableRuntimeIdentifier();

    /// <summary>The portable RID: <c>linux-x64</c> whatever the libc.</summary>
    internal static string PortableRuntimeIdentifier() => $"{OperatingSystemName()}-{ArchitectureName()}";

    internal static string MuslRuntimeIdentifier() => $"linux-musl-{ArchitectureName()}";

    /// <summary>
    /// Whether this process runs on musl libc, where a glibc binary cannot be loaded.
    /// </summary>
    /// <remarks>
    /// A framework-dependent app published for <c>linux-x64</c> reports that portable RID even on
    /// Alpine, so the libc has to be detected rather than read off the runtime. The musl dynamic
    /// loader is present on every musl distribution at a fixed path; <c>/etc/alpine-release</c>
    /// covers the rest.
    /// </remarks>
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
