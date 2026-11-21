using System;
using System.IO;
using System.IO.Compression;
using M3scalinum.Text.Docx;
using Xunit;

namespace M3scalinum.Text.Docx.Tests;

/// <summary>
/// The committed fixture and the builder that writes it must not drift apart.
/// </summary>
public sealed class FixtureTests
{
    /// <summary>
    /// Where the fixture is read from: the test project copies <c>fixtures/</c> next to the binary.
    /// </summary>
    internal static string Path => System.IO.Path.Combine(
        AppContext.BaseDirectory, "fixtures", DocxFixture.FileName);

    /// <summary>
    /// Setting this writes the fixture the builder produces into the output directory, so a change to
    /// the builder can be turned into a new committed file. See <c>fixtures/README.md</c>.
    /// </summary>
    private const string RegenerateVariable = "M3SCALINUM_DOCX_REGENERATE";

    [Fact]
    public void The_committed_package_holds_the_parts_the_builder_writes()
    {
        if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1")
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllBytes(Path, DocxFixture.Build());
        }

        Assert.True(File.Exists(Path), $"the fixture is missing: {Path}");

        using var archive = ZipFile.OpenRead(Path);

        var parts = DocxFixture.Parts();

        Assert.Equal(parts.Count, archive.Entries.Count);

        foreach (var (name, content) in parts)
        {
            var entry = archive.GetEntry(name);

            Assert.NotNull(entry);
            Assert.Equal(content, Read(entry!));
        }
    }

    [Fact]
    public void The_committed_package_is_a_package_the_reader_opens()
    {
        var document = DocxReader.Read(File.ReadAllBytes(Path));

        Assert.NotEmpty(document.Blocks);
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
