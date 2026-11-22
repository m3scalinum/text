using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Batch manifests: fingerprints per file, the seal over the manifest's own bytes, and the four states
/// verification reports. Each test writes a real temporary batch, so the filesystem — lengths,
/// last-write times, sub-directories — is part of what is asserted.
/// </summary>
public sealed class PdfBatchManifestTests
{
    private static readonly DateTimeOffset SealedAt = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_batch_of_three_files_seals_a_manifest_that_verifies_current()
    {
        using var batch = new Batch();
        batch.Write("invoice-1.pdf", "%PDF-1.7 one");
        batch.Write("invoice-2.pdf", "%PDF-1.7 two");
        batch.Write("invoice-3.pdf", "%PDF-1.7 three");

        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);
        var check = PdfBatchManifest.Verify(batch.Root, manifest);

        Assert.Equal(3, manifest.Entries.Count);
        Assert.All(manifest.Entries, entry => Assert.Equal(64, entry.Sha256.Length));
        Assert.Equal(64, manifest.Seal.Length);
        Assert.True(check.IsIntact);
        Assert.Empty(check.Breaks);
        Assert.All(check.Entries, entry => Assert.Equal(PdfBatchEntryState.Current, entry.State));
        Assert.All(check.Entries, entry => Assert.True(entry.WasHashed));
        Assert.Equal(3, check.Hashed);
    }

    [Fact]
    public void The_entries_are_sorted_by_identifier_and_carry_size_fingerprint_and_times()
    {
        using var batch = new Batch();
        batch.Write("b.pdf", "beta");
        var path = batch.Write("a.pdf", "alpha");

        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);
        var entry = manifest.Entries[0];

        Assert.Equal(["a.pdf", "b.pdf"], manifest.Entries.Select(item => item.Id));
        Assert.Equal("a.pdf", entry.Id);
        Assert.Equal(5, entry.Size);
        Assert.Equal(Sha256("alpha"), entry.Sha256);
        Assert.Equal(SealedAt, entry.AddedAt);
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero), entry.LastWriteUtc);
    }

    [Fact]
    public void One_byte_changed_in_a_file_is_detected_and_the_file_is_named()
    {
        using var batch = new Batch();
        batch.Write("invoice-1.pdf", "%PDF-1.7 one");
        batch.Write("invoice-2.pdf", "%PDF-1.7 two");

        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);
        batch.Write("invoice-2.pdf", "%PDF-1.7 twO");

        var check = PdfBatchManifest.Verify(batch.Root, manifest);

        Assert.False(check.IsIntact);

        var modified = Assert.Single(check.Entries, entry => entry.State == PdfBatchEntryState.Modified);

        Assert.Equal("invoice-2.pdf", modified.Id);
        Assert.Equal(manifest.Entries[1].Sha256, modified.ExpectedSha256);
        Assert.Equal(Sha256("%PDF-1.7 twO"), modified.ActualSha256);
        Assert.Contains(check.Breaks, line => line.Contains("invoice-2.pdf", StringComparison.Ordinal));
        Assert.All(check.Entries.Where(entry => entry.Id != "invoice-2.pdf"), entry => Assert.Equal(PdfBatchEntryState.Current, entry.State));
    }

    [Fact]
    public void A_file_removed_after_sealing_is_reported_missing()
    {
        using var batch = new Batch();
        batch.Write("invoice-1.pdf", "%PDF-1.7 one");
        var path = batch.Write("invoice-2.pdf", "%PDF-1.7 two");

        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);
        File.Delete(path);

        var check = PdfBatchManifest.Verify(batch.Root, manifest);
        var missing = Assert.Single(check.Entries, entry => entry.State == PdfBatchEntryState.Missing);

        Assert.Equal("invoice-2.pdf", missing.Id);
        Assert.Null(missing.ActualSha256);
        Assert.False(missing.WasHashed);
        Assert.Contains(check.Breaks, line => line.StartsWith("invoice-2.pdf is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_added_after_sealing_is_reported_added()
    {
        using var batch = new Batch();
        batch.Write("invoice-1.pdf", "%PDF-1.7 one");

        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);
        batch.Write("late/invoice-2.pdf", "%PDF-1.7 late");

        var check = PdfBatchManifest.Verify(batch.Root, manifest);

        Assert.False(check.IsIntact);

        var added = Assert.Single(check.Entries, entry => entry.State == PdfBatchEntryState.Added);

        Assert.Equal("late/invoice-2.pdf", added.Id);
        Assert.Null(added.ExpectedSha256);
        Assert.Equal(Sha256("%PDF-1.7 late"), added.ActualSha256);
        Assert.Contains(check.Breaks, line => line.StartsWith("late/invoice-2.pdf is new", StringComparison.Ordinal));
    }

    [Fact]
    public void One_byte_changed_in_the_manifest_is_refused_naming_the_seal()
    {
        using var batch = new Batch();
        batch.Write("invoice-1.pdf", "%PDF-1.7 one");

        var bytes = PdfBatchManifest.Build(batch.Root, SealedAt).ToBytes();
        var text = Encoding.UTF8.GetString(bytes);

        // Change one character of the entry's fingerprint: a single byte of the sealed part.
        var index = text.IndexOf("entry\t", StringComparison.Ordinal) + "entry\t".Length;
        text = string.Concat(
            text.AsSpan(0, index),
            text[index] == 'a' ? "b" : "a",
            text.AsSpan(index + 1));

        var exception = Assert.Throws<PdfOperationException>(
            () => PdfBatchManifest.Load(Encoding.UTF8.GetBytes(text)));

        Assert.Contains("seal", exception.Message, StringComparison.Ordinal);
        Assert.Contains("One byte of the manifest has changed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_seal_line_tampered_with_is_refused()
    {
        using var batch = new Batch();
        batch.Write("invoice-1.pdf", "%PDF-1.7 one");

        var text = Encoding.UTF8.GetString(PdfBatchManifest.Build(batch.Root, SealedAt).ToBytes());
        var sealStart = text.LastIndexOf("seal\t", StringComparison.Ordinal) + "seal\t".Length;
        text = string.Concat(
            text.AsSpan(0, sealStart),
            text[sealStart] == '0' ? "1" : "0",
            text.AsSpan(sealStart + 1));

        var exception = Assert.Throws<PdfOperationException>(
            () => PdfBatchManifest.Load(Encoding.UTF8.GetBytes(text)));

        Assert.Contains("seal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_files_with_the_same_name_in_two_sub_directories_are_two_entries()
    {
        using var batch = new Batch();
        batch.Write("2026-01/invoice.pdf", "%PDF-1.7 january");
        batch.Write("2026-02/invoice.pdf", "%PDF-1.7 february");

        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);

        Assert.Equal(["2026-01/invoice.pdf", "2026-02/invoice.pdf"], manifest.Entries.Select(entry => entry.Id));
        Assert.NotEqual(manifest.Entries[0].Sha256, manifest.Entries[1].Sha256);

        batch.Write("2026-02/invoice.pdf", "%PDF-1.7 februarY");

        var check = PdfBatchManifest.Verify(batch.Root, manifest);

        Assert.Equal("2026-01/invoice.pdf", check.Entries[0].Id);
        Assert.Equal(PdfBatchEntryState.Current, check.Entries[0].State);
        Assert.Equal("2026-02/invoice.pdf", check.Entries[1].Id);
        Assert.Equal(PdfBatchEntryState.Modified, check.Entries[1].State);
    }

    [Fact]
    public void Fast_mode_trusts_size_and_timestamp_and_misses_a_same_length_edit()
    {
        using var batch = new Batch();
        batch.Write("invoice-1.pdf", "%PDF-1.7 one");
        var path = batch.Write("invoice-2.pdf", "%PDF-1.7 two");

        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);
        var sealedWrite = File.GetLastWriteTimeUtc(path);

        // Same length, different byte, and the timestamp put back: only a hash can tell.
        File.WriteAllText(path, "%PDF-1.7 twO", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.SetLastWriteTimeUtc(path, sealedWrite);

        Assert.Equal(12, new FileInfo(path).Length);
        Assert.Equal(sealedWrite, File.GetLastWriteTimeUtc(path));

        var fast = PdfBatchManifest.Verify(batch.Root, manifest, PdfBatchVerificationMode.Fast);

        Assert.True(fast.IsIntact);
        Assert.Equal(0, fast.Hashed);
        Assert.All(fast.Entries, entry => Assert.False(entry.WasHashed));

        var full = PdfBatchManifest.Verify(batch.Root, manifest);

        Assert.False(full.IsIntact);
        Assert.Contains(full.Entries, entry => entry.State == PdfBatchEntryState.Modified && entry.Id == "invoice-2.pdf");
    }

    [Fact]
    public void Fast_mode_hashes_a_file_whose_timestamp_moved()
    {
        using var batch = new Batch();
        var path = batch.Write("invoice-1.pdf", "%PDF-1.7 one");

        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));

        var check = PdfBatchManifest.Verify(batch.Root, manifest, PdfBatchVerificationMode.Fast);
        var entry = Assert.Single(check.Entries);

        Assert.Equal(PdfBatchEntryState.Current, entry.State);
        Assert.True(entry.WasHashed);
        Assert.Equal(1, check.Hashed);
    }

    [Fact]
    public void Two_constructions_of_the_same_batch_render_the_same_manifest()
    {
        using var batch = new Batch();
        batch.Write("invoice-1.pdf", "%PDF-1.7 one");
        batch.Write("nested/invoice-2.pdf", "%PDF-1.7 two");

        var first = PdfBatchManifest.Build(batch.Root, SealedAt).ToBytes();
        var second = PdfBatchManifest.Build(batch.Root, SealedAt).ToBytes();

        Assert.Equal(first, second);
        Assert.Equal(PdfBatchManifest.Build(batch.Root, SealedAt).Seal, PdfBatchManifest.Load(first).Seal);
    }

    [Fact]
    public void The_manifest_round_trips_through_its_canonical_bytes()
    {
        using var batch = new Batch();
        batch.Write("invoice-1.pdf", "%PDF-1.7 one");
        batch.Write("invoice-2.pdf", "%PDF-1.7 two");

        var built = PdfBatchManifest.Build(batch.Root, SealedAt);
        var loaded = PdfBatchManifest.Load(built.ToBytes());

        Assert.Equal(built.Seal, loaded.Seal);
        Assert.Equal(
            built.Entries.Select(entry => (entry.Id, entry.Size, entry.Sha256, entry.AddedAt, entry.LastWriteUtc)),
            loaded.Entries.Select(entry => (entry.Id, entry.Size, entry.Sha256, entry.AddedAt, entry.LastWriteUtc)));
        Assert.Equal(built.ToBytes(), loaded.ToBytes());
    }

    [Fact]
    public void The_manifest_is_the_documented_canonical_text()
    {
        using var batch = new Batch();
        batch.Write("a.pdf", "alpha");

        var bytes = PdfBatchManifest.Build(batch.Root, SealedAt).ToBytes();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split('\n');

        Assert.Equal("M3SCALINUM-BATCH-MANIFEST 1", lines[0]);
        Assert.StartsWith("entry\t", lines[1], StringComparison.Ordinal);
        Assert.Equal(6, lines[1].Split('\t').Length);
        Assert.StartsWith("seal\t", lines[2], StringComparison.Ordinal);
        Assert.Equal(string.Empty, lines[3]);
        Assert.DoesNotContain('\r', text);

        // The seal is the SHA-256 of exactly the bytes before the seal line.
        var sealedEnd = bytes.Length - "seal\t".Length - 64 - 1;
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, sealedEnd))).ToLowerInvariant(),
            lines[2]["seal\t".Length..]);
    }

    [Fact]
    public void The_manifest_never_carries_the_files_it_seals()
    {
        using var batch = new Batch();
        const string Secret = "the whole invoice that must never be copied into the manifest";
        batch.Write("invoice-1.pdf", Secret);

        var text = Encoding.UTF8.GetString(PdfBatchManifest.Build(batch.Root, SealedAt).ToBytes());

        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        Assert.Contains(Sha256(Secret), text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_with_a_percent_sign_round_trips_through_the_manifest()
    {
        using var batch = new Batch();
        batch.Write("100% paid.pdf", "%PDF-1.7 paid");

        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);
        var text = Encoding.UTF8.GetString(manifest.ToBytes());

        Assert.Contains("%25", text, StringComparison.Ordinal);

        var loaded = Assert.Single(PdfBatchManifest.Load(manifest.ToBytes()).Entries);

        Assert.Equal("100% paid.pdf", loaded.Id);
        Assert.True(PdfBatchManifest.Verify(batch.Root, PdfBatchManifest.Load(manifest.ToBytes())).IsIntact);
    }

    [Fact]
    public void A_manifest_that_names_a_path_outside_the_batch_is_refused()
    {
        using var batch = new Batch();
        batch.Write("a.pdf", "alpha");

        var text = Encoding.UTF8.GetString(PdfBatchManifest.Build(batch.Root, SealedAt).ToBytes())
            .Replace("\ta.pdf\n", "\t../a.pdf\n", StringComparison.Ordinal);

        var exception = Assert.Throws<PdfOperationException>(() => PdfBatchManifest.Load(Reseal(text)));

        Assert.Contains("inside the batch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_manifest_that_is_not_in_the_canonical_form_is_refused()
    {
        Assert.Contains("empty", Assert.Throws<PdfOperationException>(
            () => PdfBatchManifest.Load([])).Message, StringComparison.Ordinal);

        Assert.Contains("line feed", Assert.Throws<PdfOperationException>(
            () => PdfBatchManifest.Load(Encoding.UTF8.GetBytes("M3SCALINUM-BATCH-MANIFEST 1"))).Message, StringComparison.Ordinal);

        Assert.Contains("does not begin with", Assert.Throws<PdfOperationException>(
            () => PdfBatchManifest.Load(Reseal("SOMETHING-ELSE\n"))).Message, StringComparison.Ordinal);

        Assert.Contains("not an entry", Assert.Throws<PdfOperationException>(
            () => PdfBatchManifest.Load(Reseal("M3SCALINUM-BATCH-MANIFEST 1\nentry\tbad\n"))).Message, StringComparison.Ordinal);

        var uppercase = "M3SCALINUM-BATCH-MANIFEST 1\nentry\t"
            + new string('A', 64)
            + "\t5\t2026-11-22T12:00:00.0000000Z\t2026-11-22T12:00:00.0000000Z\ta.pdf\n";

        Assert.Contains("lowercase SHA-256", Assert.Throws<PdfOperationException>(
            () => PdfBatchManifest.Load(Reseal(uppercase))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_that_is_not_a_directory_is_refused()
    {
        using var batch = new Batch();
        var manifest = PdfBatchManifest.Build(batch.Root, SealedAt);
        var missing = Path.Combine(batch.Root, "not-there");

        Assert.Throws<DirectoryNotFoundException>(() => PdfBatchManifest.Build(missing));
        Assert.Throws<DirectoryNotFoundException>(
            () => PdfBatchManifest.Verify(missing, manifest));
    }

    private static byte[] Reseal(string text)
    {
        // A forger who can write the files can recompute the seal: the format must still refuse a
        // path that climbs out of the batch, and §6 says only a signature stops the forgery itself.
        var sealStart = text.LastIndexOf("seal\t", StringComparison.Ordinal);
        var sealedPart = sealStart < 0 ? text : text[..sealStart];

        return Encoding.UTF8.GetBytes(sealedPart + "seal\t" + Sha256(sealedPart) + "\n");
    }

    private static string Sha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    /// <summary>A temporary batch directory, deleted when the test ends.</summary>
    private sealed class Batch : IDisposable
    {
        public Batch()
        {
            Root = Path.Combine(Path.GetTempPath(), "m3scalinum-batch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string relative, string content)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A file another process still holds must not turn a passing test into a failing one.
            }
        }
    }
}
