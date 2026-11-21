using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace M3scalinum.Text.Docx.Internal;

/// <summary>
/// Resolves <c>sharp_text_docx</c> from the NuGet <c>runtimes/&lt;rid&gt;/native/</c> layout, the
/// application directory, or an explicit override.
/// </summary>
/// <remarks>
/// The same probing order as <c>M3scalinum.Text.Typst.Native</c>, for the same reason: a
/// framework-dependent application cannot rely on the runtime to find a library the package puts
/// under a RID folder. The override variable is separate (<c>SHARPTEXTDOCX_NATIVE_PATH</c>) because
/// this library and the compiler are two different files.
/// </remarks>
internal static class NativeLibraryResolver
{
    private const string OverrideVariable = "SHARPTEXTDOCX_NATIVE_PATH";

    /// <summary>
    /// Installs the resolver. Called from <see cref="DocxNative"/>'s static constructor, which every
    /// entry point goes through, so no module initializer is needed.
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
        => Candidates(RuntimeIdentifier(), OperatingSystemName(), AppContext.BaseDirectory);

    /// <summary>The probing order, with every input explicit so a test can pin it on any host.</summary>
    internal static string[] Candidates(string rid, string os, string baseDirectory)
    {
        var fileName = FileName();

        return
        [
            Path.Combine(baseDirectory, "runtimes", rid, "native", fileName),
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
            return "sharp_text_docx.dll";
        }

        return OperatingSystem.IsMacOS() ? "libsharp_text_docx.dylib" : "libsharp_text_docx.so";
    }

    internal static string RuntimeIdentifier()
        => $"{OperatingSystemName()}-{ArchitectureName()}";

    private static string ArchitectureName() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "unknown",
    };
}
