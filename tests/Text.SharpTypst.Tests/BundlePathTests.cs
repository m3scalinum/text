using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3scalinum.Text.SharpTypst.Compilation;
using Xunit;

namespace M3scalinum.Text.SharpTypst.Tests;

/// <summary>
/// A bundle path comes from the document. Typst rejects a `#asset` that would escape the bundle
/// root, but <see cref="BundleResult"/> must not rely on that: it writes to the file system.
/// </summary>
public sealed class BundlePathTests
{
    private static BundleResult Bundle(params string[] paths)
        => new(string.Empty, paths.Select(path => new BundleFile(path, [1, 2, 3])).ToList());

    private static string TempDirectory()
        => Path.Combine(Path.GetTempPath(), "sharp-typst-bundle-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("nested/../../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/windows.txt")]
    [InlineData("")]
    public void WriteTo_rejects_a_path_that_escapes_the_directory(string path)
    {
        var directory = TempDirectory();

        try
        {
            Assert.Throws<InvalidOperationException>(() => Bundle(path).WriteTo(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/windows.txt")]
    public void WriteToZip_rejects_a_path_that_escapes_the_archive(string path)
    {
        var archive = Path.Combine(Path.GetTempPath(), "sharp-typst-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            Assert.Throws<InvalidOperationException>(() => Bundle(path).WriteToZip(archive));
        }
        finally
        {
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }
        }
    }

    [Fact]
    public void WriteTo_accepts_a_nested_relative_path()
    {
        var directory = TempDirectory();

        try
        {
            Bundle("nested/ok.txt").WriteTo(directory);

            Assert.True(File.Exists(Path.Combine(directory, "nested", "ok.txt")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteToZip_accepts_a_nested_relative_path()
    {
        var archive = Path.Combine(Path.GetTempPath(), "sharp-typst-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            Bundle("nested/ok.txt").WriteToZip(archive);

            using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
            Assert.Contains(zip.Entries, entry => entry.FullName == "nested/ok.txt");
        }
        finally
        {
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }
        }
    }
}
