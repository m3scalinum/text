using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// The bundle target: every file the document produces. This is the only target in which
/// <c>#asset</c> and <c>#document</c> are valid.
/// </summary>
public sealed class BundleResult
{
    internal BundleResult(string html, IReadOnlyList<BundleFile> files)
    {
        Html = html;
        Files = files;
    }

    /// <summary>The main HTML document, as a string.</summary>
    public string Html { get; }

    /// <summary>Every file in the bundle, in bundle order.</summary>
    public IReadOnlyList<BundleFile> Files { get; }

    /// <summary>
    /// Writes every file to a directory, at its declared path. Intermediate directories are
    /// created.
    /// </summary>
    /// <param name="directory">The directory to write into. Created if missing.</param>
    /// <exception cref="InvalidOperationException">
    /// A file path would resolve outside <paramref name="directory"/>.
    /// </exception>
    public void WriteTo(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);

        foreach (var file in Files)
        {
            var target = ResolveInside(root, file.Path);
            var parent = Path.GetDirectoryName(target);

            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllBytes(target, file.Data);
        }
    }

    /// <summary>
    /// Writes every file into a zip archive, at its declared path. An existing file is replaced.
    /// </summary>
    /// <param name="path">The archive to create.</param>
    /// <exception cref="InvalidOperationException">
    /// A file path would resolve outside the archive root.
    /// </exception>
    public void WriteToZip(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var file in Files)
        {
            // A `..` entry is a zip-slip: the consumer's extractor would write outside its target.
            EnsureRelative(file.Path);

            var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);

            using var entryStream = entry.Open();
            entryStream.Write(file.Data);
        }
    }

    /// <summary>
    /// Resolves a bundle path against <paramref name="root"/>, refusing anything that escapes it.
    /// </summary>
    /// <remarks>
    /// Typst already rejects a `#asset` path that would escape the bundle root, so this is defence
    /// in depth: the check costs one string comparison and survives a change in the compiler's
    /// validation or a bug in the native layer.
    /// </remarks>
    private static string ResolveInside(string root, string path)
    {
        EnsureRelative(path);

        var target = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, target);

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                $"bundle path '{path}' resolves outside '{root}'");
        }

        return target;
    }

    private static void EnsureRelative(string path)
    {
        if (path.Length == 0
            || Path.IsPathRooted(path)
            || path.StartsWith('/')
            || path.StartsWith('\\')
            || path.Contains(':', StringComparison.Ordinal)
            || path.Split('/', '\\').Any(segment => segment == ".."))
        {
            throw new InvalidOperationException($"bundle path '{path}' is not a relative path");
        }
    }
}
