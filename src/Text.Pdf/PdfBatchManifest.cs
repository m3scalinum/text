using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>How one piece of an archived batch compares with the manifest sealed for it.</summary>
public enum PdfBatchEntryState
{
    /// <summary>The file is exactly the one the manifest sealed.</summary>
    Current = 0,

    /// <summary>The file is present under a sealed name but its bytes are not the sealed ones.</summary>
    Modified = 1,

    /// <summary>The manifest seals a name that the batch no longer carries.</summary>
    Missing = 2,

    /// <summary>The batch carries a file under a name the manifest does not seal.</summary>
    Added = 3,
}

/// <summary>How thorough a <see cref="PdfBatchManifest.Verify"/> pass is, and what it costs.</summary>
public enum PdfBatchVerificationMode
{
    /// <summary>
    /// Hash every file and compare the fingerprints. The only mode that can see a change that kept
    /// the file's length and its last-write time; it reads every byte of the batch.
    /// </summary>
    Full = 0,

    /// <summary>
    /// Trust length and last-write time first: a file whose two still match the manifest is reported
    /// <see cref="PdfBatchEntryState.Current"/> without being read. Only the files that moved are
    /// hashed. A byte replaced inside a file of the same length, with the timestamp put back, is not
    /// seen — see <see cref="PdfBatchManifest.Verify"/>.
    /// </summary>
    Fast = 1,
}

/// <summary>One piece of a sealed batch: what it is called, how big it is, and its fingerprint.</summary>
/// <remarks>
/// <see cref="Id"/> is the file's path under the batch root, written with <c>/</c> separators, so two
/// files that share a name in two sub-directories are two distinct entries. Names are compared
/// ordinally, which is also how the manifest is sorted.
/// </remarks>
public sealed class PdfBatchManifestEntry
{
    internal PdfBatchManifestEntry(string id, long size, string sha256, DateTimeOffset addedAt, DateTimeOffset lastWriteUtc)
    {
        Id = id;
        Size = size;
        Sha256 = sha256;
        AddedAt = addedAt;
        LastWriteUtc = lastWriteUtc;
    }

    /// <summary>The file's path under the batch root, with <c>/</c> separators, such as <c>2026/invoice.pdf</c>.</summary>
    public string Id { get; }

    /// <summary>The file's length in bytes when the manifest was sealed.</summary>
    public long Size { get; }

    /// <summary>The SHA-256 of the file's bytes, as lowercase hexadecimal.</summary>
    public string Sha256 { get; }

    /// <summary>When the file was added to the batch: the instant the caller sealed this manifest.</summary>
    public DateTimeOffset AddedAt { get; }

    /// <summary>
    /// The file's last-write time, in UTC, when the manifest was sealed. Fast verification compares it
    /// before deciding whether the file has to be read.
    /// </summary>
    public DateTimeOffset LastWriteUtc { get; }
}

/// <summary>
/// A sealed manifest over a batch of files: one entry per piece, and the SHA-256 of the manifest's own
/// canonical bytes.
/// </summary>
/// <remarks>
/// The seal covers every byte of the manifest before the seal line, so a single changed byte in an
/// entry is visible; a changed byte in the seal line itself no longer matches the entries. The
/// manifest carries fingerprints, never file contents: a thousand invoices cost a thousand hashes,
/// not a thousand copies.
/// </remarks>
public sealed class PdfBatchManifestDocument
{
    internal PdfBatchManifestDocument(IReadOnlyList<PdfBatchManifestEntry> entries, string seal)
    {
        Entries = entries;
        Seal = seal;
    }

    /// <summary>The entries, in ascending ordinal order of <see cref="PdfBatchManifestEntry.Id"/>.</summary>
    public IReadOnlyList<PdfBatchManifestEntry> Entries { get; }

    /// <summary>The SHA-256 of the canonical bytes before the seal line, as lowercase hexadecimal.</summary>
    public string Seal { get; }

    /// <summary>
    /// The manifest's canonical bytes: UTF-8 without a byte-order mark, LF line endings, tab-separated
    /// fields, entries sorted by identifier. Writing these bytes and loading them back yields the same
    /// seal, which is what makes a re-sealed batch of the same files the same manifest.
    /// </summary>
    /// <returns>The sealed manifest, ready to be stored next to the batch.</returns>
    public byte[] ToBytes() => PdfBatchManifest.Render(Entries, Seal);
}

/// <summary>What verification found for one name: the manifest's entry, or the file that has none.</summary>
public sealed class PdfBatchEntryResult
{
    internal PdfBatchEntryResult(
        string id,
        PdfBatchEntryState state,
        string? expectedSha256,
        string? actualSha256,
        bool wasHashed)
    {
        Id = id;
        State = state;
        ExpectedSha256 = expectedSha256;
        ActualSha256 = actualSha256;
        WasHashed = wasHashed;
    }

    /// <summary>The file's path under the batch root, with <c>/</c> separators.</summary>
    public string Id { get; }

    /// <summary>Whether the file is current, modified, missing or new.</summary>
    public PdfBatchEntryState State { get; }

    /// <summary>The fingerprint the manifest sealed for this name, or <see langword="null"/> for an added file.</summary>
    public string? ExpectedSha256 { get; }

    /// <summary>
    /// The file's fingerprint now, or <see langword="null"/> when it is missing or when a fast pass
    /// trusted its length and last-write time instead of reading it.
    /// </summary>
    public string? ActualSha256 { get; }

    /// <summary>Whether the file was read and hashed during this pass.</summary>
    public bool WasHashed { get; }
}

/// <summary>The outcome of verifying a batch against its manifest, one result per name.</summary>
public sealed class PdfBatchVerification
{
    internal PdfBatchVerification(IReadOnlyList<PdfBatchEntryResult> entries)
    {
        Entries = entries;
        Breaks = Describe(entries);
        Hashed = entries.Count(entry => entry.WasHashed);
    }

    /// <summary>
    /// The results: the manifest's entries in manifest order, then the files the batch carries that the
    /// manifest does not seal, in ascending ordinal order of their identifiers.
    /// </summary>
    public IReadOnlyList<PdfBatchEntryResult> Entries { get; }

    /// <summary>
    /// One sentence per piece that is not <see cref="PdfBatchEntryState.Current"/>. Empty when the
    /// batch is exactly the one the manifest sealed.
    /// </summary>
    public IReadOnlyList<string> Breaks { get; }

    /// <summary>Whether every sealed file is still the one that was sealed, and nothing was added.</summary>
    public bool IsIntact => Breaks.Count == 0;

    /// <summary>How many files this pass read and hashed: the cost of the verification it ran.</summary>
    public int Hashed { get; }

    private static List<string> Describe(IReadOnlyList<PdfBatchEntryResult> entries)
    {
        var breaks = new List<string>();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case PdfBatchEntryState.Modified:
                    breaks.Add(
                        $"{entry.Id} is modified: the manifest seals sha256 {entry.ExpectedSha256} "
                        + $"and the file now hashes to {entry.ActualSha256}");
                    break;
                case PdfBatchEntryState.Missing:
                    breaks.Add($"{entry.Id} is missing: the manifest seals it and the batch no longer carries it");
                    break;
                case PdfBatchEntryState.Added:
                    breaks.Add($"{entry.Id} is new: the batch carries it and the manifest does not seal it");
                    break;
                default:
                    break;
            }
        }

        return breaks;
    }
}

/// <summary>
/// Seals a batch of files as a manifest of SHA-256 fingerprints, and checks a batch against one.
/// </summary>
/// <remarks>
/// <para>
/// A batch is a directory tree. Every regular file under the root gets one entry — name, size,
/// fingerprint, the file's last-write time and the instant the manifest was sealed — and an
/// identifier that is the file's path relative to the root with <c>/</c> separators. Two files named
/// <c>invoice.pdf</c>, one in <c>2026-01/</c> and one in <c>2026-02/</c>, are therefore two entries
/// with two identifiers; nothing is compared by bare name.
/// </para>
/// <para>
/// The manifest does not carry the files. Verifying it proves that no sealed file changed, that none
/// went missing and that nothing was added — it does not prove who sealed it. A caller that needs a
/// proof it can show to somebody else signs the manifest's bytes with <see cref="PdfSigner"/> or any
/// other signature over those bytes; see <c>docs/specifications/33-archive-integrity.md</c>.
/// </para>
/// </remarks>
public static class PdfBatchManifest
{
    /// <summary>The first line of a canonical manifest: the format's name and its version.</summary>
    internal const string Header = "M3SCALINUM-BATCH-MANIFEST 1";

    private const string EntryTag = "entry";
    private const string SealTag = "seal";
    private const char Separator = '\t';
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    /// <summary>
    /// Reads every regular file under <paramref name="root"/>, recursively, and seals a manifest over
    /// them.
    /// </summary>
    /// <param name="root">The batch's directory: the lot of invoices a caller archived together.</param>
    /// <param name="addedAt">
    /// The instant to record as every entry's addition time, or <see langword="null"/> for the current
    /// time. Passing a fixed instant makes two constructions over the same files produce the same
    /// manifest, byte for byte.
    /// </param>
    /// <returns>The sealed manifest, holding one entry per file sorted by identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is empty.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="root"/> is not a directory.</exception>
    /// <remarks>
    /// The cost is one read of the whole batch: every byte of every file is hashed once. Sealing a
    /// thousand invoices reads a thousand invoices. The manifest file itself must be stored outside
    /// the batch, or the next build seals the manifest too and the next verification reports it as an
    /// added file.
    /// </remarks>
    public static PdfBatchManifestDocument Build(string root, DateTimeOffset? addedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var directory = Path.GetFullPath(root);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"the batch root '{directory}' is not a directory");
        }

        var stamp = (addedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var entries = new List<PdfBatchManifestEntry>();

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            entries.Add(Measure(directory, path, stamp));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

        return SealEntries(entries);
    }

    /// <summary>
    /// Parses a manifest and checks its seal: an entry line altered by one byte is refused, and so is a
    /// seal line that no longer describes the entries above it.
    /// </summary>
    /// <param name="manifest">The bytes of a manifest, as <see cref="PdfBatchManifestDocument.ToBytes"/> wrote them.</param>
    /// <returns>The entries and the seal the manifest carries.</returns>
    /// <exception cref="PdfOperationException">
    /// The bytes are not a canonical manifest of a format version this reader knows, an entry is
    /// malformed or names a path outside the batch, or the seal does not match the entries — the last
    /// of which is what one changed byte produces.
    /// </exception>
    public static PdfBatchManifestDocument Load(ReadOnlySpan<byte> manifest)
    {
        if (manifest.Length == 0)
        {
            throw new PdfOperationException("the manifest is empty");
        }

        if (manifest[manifest.Length - 1] != (byte)'\n')
        {
            throw new PdfOperationException(
                "the manifest does not end with a line feed, so it is not in the canonical form this reader seals");
        }

        var sealedEnd = manifest[..^1].LastIndexOf((byte)'\n') + 1;

        if (sealedEnd <= 0 || sealedEnd >= manifest.Length)
        {
            throw new PdfOperationException("the manifest carries no seal line");
        }

        var sealedBytes = manifest[..sealedEnd];
        var sealLine = Decode(manifest[sealedEnd..^1], "the seal line");

        var expected = SealOf(sealLine);
        var actual = Convert.ToHexString(SHA256.HashData(sealedBytes)).ToLowerInvariant();

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new PdfOperationException(
                $"the manifest's seal does not match its entries: the seal line says {expected} and the "
                + $"entries hash to {actual}. One byte of the manifest has changed since it was sealed");
        }

        var text = Decode(sealedBytes, "the manifest");
        var lines = text.Split('\n');

        // The header line and the empty element after the last line feed.
        if (lines.Length < 2 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
        {
            throw new PdfOperationException(
                $"the manifest does not begin with '{Header}': it is a different format, or a version this reader does not know");
        }

        var entries = new List<PdfBatchManifestEntry>(lines.Length - 2);

        for (var index = 1; index < lines.Length - 1; index++)
        {
            entries.Add(ParseEntry(lines[index]));
        }

        for (var index = 1; index < entries.Count; index++)
        {
            if (string.CompareOrdinal(entries[index - 1].Id, entries[index].Id) >= 0)
            {
                throw new PdfOperationException(
                    $"the manifest lists '{entries[index].Id}' after '{entries[index - 1].Id}': entries must be sorted by identifier, with no duplicate");
            }
        }

        return new PdfBatchManifestDocument(entries, expected);
    }

    /// <summary>
    /// Compares a batch, as it stands on disk, with a sealed manifest.
    /// </summary>
    /// <param name="root">The batch's directory. Its absolute path is not part of the manifest, so the batch can move.</param>
    /// <param name="manifest">The manifest to verify against.</param>
    /// <param name="mode">
    /// <see cref="PdfBatchVerificationMode.Full"/>, which hashes every file and costs one read of the
    /// whole batch, or <see cref="PdfBatchVerificationMode.Fast"/>, which hashes only the files whose
    /// length or last-write time moved. Fast is what makes a thousand-file check affordable; its blind
    /// spot is a same-length edit with the timestamp restored, which only Full sees.
    /// </param>
    /// <returns>One result per sealed name, then one per file the batch carries that nothing seals.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="root"/> is not a directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    public static PdfBatchVerification Verify(
        string root,
        PdfBatchManifestDocument manifest,
        PdfBatchVerificationMode mode = PdfBatchVerificationMode.Full)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(manifest);

        if (mode is not (PdfBatchVerificationMode.Full or PdfBatchVerificationMode.Fast))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "the verification mode is not defined");
        }

        var directory = Path.GetFullPath(root);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"the batch root '{directory}' is not a directory");
        }

        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            found[Identify(directory, path)] = path;
        }

        var results = new List<PdfBatchEntryResult>(manifest.Entries.Count + found.Count);
        var sealedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest.Entries)
        {
            sealedIds.Add(entry.Id);
            results.Add(VerifyEntry(entry, found, mode));
        }

        foreach (var (id, path) in found.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (sealedIds.Contains(id))
            {
                continue;
            }

            var hashed = mode == PdfBatchVerificationMode.Full
                ? Hash(path)
                : null;

            results.Add(new PdfBatchEntryResult(id, PdfBatchEntryState.Added, expectedSha256: null, hashed, hashed is not null));
        }

        return new PdfBatchVerification(results);
    }

    /// <summary>Renders the entries and the seal as the canonical bytes, recomputing nothing.</summary>
    internal static byte[] Render(IReadOnlyList<PdfBatchManifestEntry> entries, string seal)
    {
        var sealedBytes = RenderEntries(entries);
        var sealLine = Encoding.UTF8.GetBytes(SealTag + Separator + seal + '\n');

        return [.. sealedBytes, .. sealLine];
    }

    /// <summary>The bytes the seal covers: the header and every entry line, each ending in one line feed.</summary>
    private static byte[] RenderEntries(IReadOnlyList<PdfBatchManifestEntry> entries)
    {
        var text = new StringBuilder();

        text.Append(Header).Append('\n');

        foreach (var entry in entries)
        {
            text.Append(EntryTag).Append(Separator)
                .Append(entry.Sha256).Append(Separator)
                .Append(entry.Size.ToString(CultureInfo.InvariantCulture)).Append(Separator)
                .Append(Timestamp(entry.LastWriteUtc)).Append(Separator)
                .Append(Timestamp(entry.AddedAt)).Append(Separator)
                .Append(Escape(entry.Id)).Append('\n');
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>Hashes the entries' canonical lines and appends the seal that covers them.</summary>
    private static PdfBatchManifestDocument SealEntries(List<PdfBatchManifestEntry> entries)
    {
        var sealedBytes = RenderEntries(entries);
        var seal = Convert.ToHexString(SHA256.HashData(sealedBytes)).ToLowerInvariant();

        return new PdfBatchManifestDocument(entries, seal);
    }

    private static PdfBatchManifestEntry Measure(string root, string path, DateTimeOffset stamp)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var write = File.GetLastWriteTimeUtc(path);

        return new PdfBatchManifestEntry(
            Identify(root, path),
            stream.Length,
            hash,
            stamp,
            new DateTimeOffset(write, TimeSpan.Zero));
    }

    private static PdfBatchEntryResult VerifyEntry(
        PdfBatchManifestEntry entry,
        Dictionary<string, string> found,
        PdfBatchVerificationMode mode)
    {
        if (!found.TryGetValue(entry.Id, out var path))
        {
            return new PdfBatchEntryResult(entry.Id, PdfBatchEntryState.Missing, entry.Sha256, actualSha256: null, wasHashed: false);
        }

        if (mode == PdfBatchVerificationMode.Fast
            && new FileInfo(path).Length == entry.Size
            && File.GetLastWriteTimeUtc(path).Ticks == entry.LastWriteUtc.UtcTicks)
        {
            return new PdfBatchEntryResult(entry.Id, PdfBatchEntryState.Current, entry.Sha256, actualSha256: null, wasHashed: false);
        }

        var actual = Hash(path)
            ?? throw new PdfOperationException($"the file '{entry.Id}' disappeared while the batch was being verified");

        var state = string.Equals(actual, entry.Sha256, StringComparison.Ordinal)
            ? PdfBatchEntryState.Current
            : PdfBatchEntryState.Modified;

        return new PdfBatchEntryResult(entry.Id, state, entry.Sha256, actual, wasHashed: true);
    }

    /// <summary>The file's fingerprint, or <see langword="null"/> when it is not there to read.</summary>
    private static string? Hash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The entry's identifier: its path under the root, with <c>/</c> separators whatever the platform writes.</summary>
    private static string Identify(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static PdfBatchManifestEntry ParseEntry(string line)
    {
        var fields = line.Split(Separator);

        if (fields.Length != 6 || !string.Equals(fields[0], EntryTag, StringComparison.Ordinal))
        {
            throw new PdfOperationException(
                $"the manifest carries a line that is not an entry: '{line}'. Each entry has six tab-separated fields");
        }

        var id = Unescape(fields[5]);

        if (!IsSafeId(id))
        {
            throw new PdfOperationException($"the manifest names '{id}', which is not a path inside the batch");
        }

        if (fields[1].Length != 64 || !fields[1].All(char.IsAsciiHexDigitLower))
        {
            throw new PdfOperationException($"the manifest entry '{id}' does not carry a lowercase SHA-256 fingerprint");
        }

        if (!long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size))
        {
            throw new PdfOperationException($"the manifest entry '{id}' does not carry a file length");
        }

        return new PdfBatchManifestEntry(
            id,
            size,
            fields[1],
            ParseTimestamp(fields[4], id),
            ParseTimestamp(fields[3], id));
    }

    private static DateTimeOffset ParseTimestamp(string text, string id)
    {
        if (!DateTimeOffset.TryParseExact(
                text,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value))
        {
            throw new PdfOperationException($"the manifest entry '{id}' carries '{text}', which is not a canonical UTC timestamp");
        }

        return value;
    }

    /// <summary>The seal a seal line declares.</summary>
    private static string SealOf(string line)
    {
        var fields = line.Split(Separator);

        if (fields.Length != 2
            || !string.Equals(fields[0], SealTag, StringComparison.Ordinal)
            || fields[1].Length != 64
            || !fields[1].All(char.IsAsciiHexDigitLower))
        {
            throw new PdfOperationException("the manifest's last line is not a lowercase SHA-256 seal");
        }

        return fields[1];
    }

    /// <summary>Whether an identifier is a relative path with no segment that climbs out of the batch.</summary>
    private static bool IsSafeId(string id)
    {
        if (id.Length == 0 || id.Contains('\\', StringComparison.Ordinal) || Path.IsPathRooted(id))
        {
            return false;
        }

        foreach (var segment in id.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    private static string Decode(ReadOnlySpan<byte> bytes, string what)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new PdfOperationException($"{what} is not valid UTF-8", exception);
        }
    }

    private static string Timestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapes the four characters that would break a tab-separated line, and the escape character
    /// itself. Every other character, non-ASCII included, is written as it is.
    /// </summary>
    private static string Escape(string id)
        => id.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\t", "%09", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal);

    private static string Unescape(string text)
    {
        if (!text.Contains('%', StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '%' && index + 2 < text.Length && int.TryParse(
                    text.AsSpan(index + 1, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                builder.Append((char)value);
                index += 3;
                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }
}
