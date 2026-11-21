using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace M3scalinum.Text.Ocr.Internal;

/// <summary>
/// Resolves <c>sharp_text_ocr</c> from the NuGet <c>runtimes/&lt;rid&gt;/native/</c>
/// layout, the application directory, or an explicit override.
/// </summary>
internal static class NativeLibraryResolver
{
    /// <summary>The override a host sets to point at a locally built library.</summary>
    internal const string OverrideVariable = "M3SCALINUM_OCR_NATIVE_PATH";

    /// <summary>
    /// The compiler crate's override, honoured as well: the suite's native test hosts
    /// export one variable per native library, and an OCR host built the same way sets
    /// <c>SHARPTYPST_NATIVE_PATH</c> for whichever library the test is exercising.
    /// </summary>
    internal const string CompanionOverrideVariable = "SHARPTYPST_NATIVE_PATH";

    private static int _installed;

    internal static void Initialize()
    {
        // The runtime throws if a second resolver is installed for the assembly, and
        // every handle and every probe reaches this method.
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
    /// The probe itself, with the two lists it searches supplied by the caller: the
    /// override paths first, then the layout candidates under the application
    /// directory. Split out so the fallback rules can be exercised without a native
    /// library sitting in the test output, in any order.
    /// </summary>
    internal static IntPtr Resolve(
        string libraryName,
        IReadOnlyList<string> overrides,
        IReadOnlyList<string> candidates)
    {
        if (!string.Equals(libraryName, NativeMethods.LibraryName, StringComparison.Ordinal))
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

    /// <summary>
    /// Every override path that is set, in the order they are honoured. A variable
    /// that is set but empty is not a path and is skipped.
    /// </summary>
    internal static IReadOnlyList<string> OverridePaths()
    {
        var paths = new List<string>();

        foreach (var variable in new[] { OverrideVariable, CompanionOverrideVariable })
        {
            var path = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        return paths;
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
            return "sharp_text_ocr.dll";
        }

        return OperatingSystem.IsMacOS() ? "libsharp_text_ocr.dylib" : "libsharp_text_ocr.so";
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
