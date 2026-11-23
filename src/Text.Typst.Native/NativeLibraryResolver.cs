using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace M3scalinum.Text.Typst.Native;

/// <summary>
/// Resolves <c>sharp_typst</c> from the NuGet <c>runtimes/&lt;rid&gt;/native/</c> layout, the
/// application directory, or an explicit override (Spec 02 §9).
/// </summary>
internal static class NativeLibraryResolver
{
    private const string OverrideVariable = "SHARPTYPST_NATIVE_PATH";

    /// <summary>
    /// The satellite package that carries each RID's library (Spec 08 §6). A host with no library is
    /// told which package installs the one it needs, and that answer has to be the package id the
    /// release actually publishes.
    /// </summary>
    private static readonly Dictionary<string, string> PackagesByRid = new(StringComparer.Ordinal)
    {
        ["win-x64"] = "M3scalinum.Text.Typst.Native.WinX64",
        ["win-arm64"] = "M3scalinum.Text.Typst.Native.WinArm64",
        ["linux-x64"] = "M3scalinum.Text.Typst.Native.LinuxX64",
        ["linux-arm64"] = "M3scalinum.Text.Typst.Native.LinuxArm64",
        ["linux-musl-x64"] = "M3scalinum.Text.Typst.Native.LinuxMuslX64",
        ["osx-arm64"] = "M3scalinum.Text.Typst.Native.OsxArm64",
        ["osx-x64"] = "M3scalinum.Text.Typst.Native.OsxX64",
    };

    private static string? _loadedPath;

    /// <summary>
    /// The file that was loaded, or <see langword="null"/> when the runtime's default probe found
    /// it. An ABI mismatch quotes it: a path is what tells the caller which copy of the library is
    /// in play (Spec 02 §11).
    /// </summary>
    internal static string? LoadedPath => Volatile.Read(ref _loadedPath);

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
            return Load(overridePath);
        }

        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate))
            {
                return Load(candidate);
            }
        }

        // The candidates are the packaged layout; the default probing rules also cover a library a
        // host put on the loader's own search path (LD_LIBRARY_PATH, DYLD_LIBRARY_PATH, the
        // application directory, a system install). Try those before refusing to load anything.
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var handle))
        {
            return handle;
        }

        throw new TypstNativeLibraryNotFoundException(MissingLibraryMessage());
    }

    /// <summary>
    /// The message a host sees when no library could be loaded: the RID, the package that carries
    /// it, the paths that were probed and the two supported ways to supply one.
    /// </summary>
    /// <remarks>
    /// The managed package used to carry all seven natives, so a missing one could only mean a
    /// broken install; now it means "the satellite package is not referenced", which is a line in a
    /// project file. A bare <c>DllNotFoundException</c> names the library and nothing else, so the
    /// reader has to know the packaging to act on it (Spec 08 §6).
    /// </remarks>
    internal static string MissingLibraryMessage()
    {
        var rid = RuntimeIdentifier();
        var message = new StringBuilder();

        message.Append("The native Typst library is not installed for this process (RID ").Append(rid).AppendLine(").");
        message.AppendLine();
        message.AppendLine(
            "M3scalinum.Text.Typst carries the managed compiler only: the native library lives in a package of its own,");
        message.AppendLine("and this project does not reference the one for this runtime.");

        if (PackagesByRid.TryGetValue(rid, out var package))
        {
            message.AppendLine();
            message.Append("    dotnet add package ").AppendLine(package);
        }
        else
        {
            message.AppendLine();
            message.Append("No native package is published for ").Append(rid).AppendLine(". The supported RIDs are:");
            message.Append("  ").AppendLine(string.Join(", ", PackagesByRid.Keys));
        }

        var overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrEmpty(overridePath))
        {
            message.AppendLine();
            message.Append(OverrideVariable).Append(" is set to '").Append(overridePath)
                .AppendLine("', and no file is there.");
        }

        message.AppendLine();
        message.AppendLine("Probed:");

        // A non-musl host probes the same path twice (its RID is its portable RID), and a list that
        // says so twice reads like a bug in the resolver.
        foreach (var candidate in Candidates().Distinct(StringComparer.Ordinal))
        {
            message.Append("  ").AppendLine(candidate);
        }

        message.AppendLine();
        message.Append("Set ").Append(OverrideVariable).AppendLine(
            " to the full path of a library you built yourself (cargo build --release in native/sharp-typst)");
        message.AppendLine("to load that instead.");

        return message.ToString();
    }

    /// <summary>
    /// Loads <paramref name="path"/> and records it, so a later ABI mismatch can name the file.
    /// </summary>
    private static IntPtr Load(string path)
    {
        var handle = NativeLibrary.Load(path);
        Volatile.Write(ref _loadedPath, path);
        return handle;
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
