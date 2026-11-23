using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using M3scalinum.Text.Typst.Internal;
using M3scalinum.Text.Typst.Native;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// Collects the inputs of a world, then freezes them into a <see cref="TypstWorld"/>.
/// </summary>
public sealed class TypstWorldBuilder
{
    private readonly ITypstNative _native;
    private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);
    private readonly List<(string Path, byte[] Bytes)> _assets = [];
    private readonly List<(byte[] Bytes, uint FaceIndex)> _fonts = [];
    private readonly List<string> _fontDirectories = [];
    private bool _systemFonts;
    private bool _refreshFonts;
    private bool _fontCache = true;
    private string? _fontCacheDirectory;
    private readonly TypstLimits _limits = new();
    private readonly TypstPackages _packages = new();
    private DateTimeOffset _fixedClock = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private bool _systemClock;

    /// <summary>The caps this builder will apply. Exposed for tests.</summary>
    internal TypstLimits Limits => _limits;

    /// <summary>The package settings this builder will apply. Exposed for tests.</summary>
    internal TypstPackages Packages => _packages;

    /// <summary>The fixed instant this builder will use. Exposed for tests.</summary>
    internal DateTimeOffset FixedClock => _fixedClock;

    /// <summary>Whether the builder follows the system clock. Exposed for tests.</summary>
    internal bool UsesSystemClock => _systemClock;

    /// <summary>Creates a builder backed by the native library.</summary>
    public TypstWorldBuilder()
        : this(TypstNative.Instance)
    {
    }

    internal TypstWorldBuilder(ITypstNative native)
    {
        _native = native;
    }

    /// <summary>Registers a Typst source under a virtual path.</summary>
    public TypstWorldBuilder AddSource(string path, string markup)
    {
        TypstPath.Validate(path);
        ArgumentNullException.ThrowIfNull(markup);
        _sources[path] = markup;
        return this;
    }

    /// <summary>Registers a Typst source already encoded as UTF-8.</summary>
    public TypstWorldBuilder AddSource(string path, ReadOnlySpan<byte> utf8)
        => AddSource(path, Encoding.UTF8.GetString(utf8));

    /// <summary>Registers a file the document can read: image, JSON, CSV, bibliography.</summary>
    public TypstWorldBuilder AddAsset(string path, ReadOnlySpan<byte> bytes)
    {
        TypstPath.Validate(path);
        _assets.Add((path, bytes.ToArray()));
        return this;
    }

    /// <summary>Registers a font. Duplicates are deduplicated by the native layer.</summary>
    public TypstWorldBuilder AddFont(ReadOnlySpan<byte> bytes, uint faceIndex = 0)
    {
        _fonts.Add((bytes.ToArray(), faceIndex));
        return this;
    }

    /// <summary>Makes <c>datetime.today()</c> deterministic. This is the default.</summary>
    public TypstWorldBuilder WithFixedClock(DateTimeOffset utc)
    {
        _fixedClock = utc;
        _systemClock = false;
        return this;
    }

    /// <summary>Uses the real clock. The output is no longer reproducible.</summary>
    public TypstWorldBuilder WithSystemClock()
    {
        _systemClock = true;
        return this;
    }

    /// <summary>
    /// Scans a directory recursively for font files and registers every face found.
    /// </summary>
    /// <param name="path">The directory.</param>
    /// <param name="mode">
    /// Whether to reuse the process-wide index for this directory or scan it again. The default
    /// reuses it, so a server that builds one world per request pays the scan once.
    /// </param>
    /// <remarks>
    /// This is one of the two places the world reads the disk, and it is opt-in: nothing is
    /// scanned unless you call this or <see cref="AddSystemFonts(FontScanMode)"/>.
    /// </remarks>
    public TypstWorldBuilder AddFontDirectory(string path, FontScanMode mode = FontScanMode.Shared)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _fontDirectories.Add(path);
        _refreshFonts |= mode == FontScanMode.Refresh;
        return this;
    }

    /// <summary>
    /// Registers the operating system's installed fonts.
    /// </summary>
    /// <param name="mode">
    /// Whether to reuse the process-wide index or scan again. The default reuses it.
    /// </param>
    /// <remarks>
    /// Convenient, but it makes the output depend on the machine: two hosts with different fonts
    /// installed can lay the same document out differently. Prefer <see cref="AddFont"/> or
    /// <see cref="AddFontDirectory"/> when the output must be reproducible.
    /// <para>
    /// The metadata scan is not per world: every world that asks for the same font sources shares
    /// one index, and its faces are parsed when a compilation first uses them.
    /// <see cref="FontScanMode.Refresh"/> scans again and replaces that index, which is how a font
    /// installed after the process started becomes visible to the worlds that follow.
    /// </para>
    /// </remarks>
    public TypstWorldBuilder AddSystemFonts(FontScanMode mode = FontScanMode.Shared)
    {
        _systemFonts = true;
        _refreshFonts |= mode == FontScanMode.Refresh;
        return this;
    }

    /// <summary>
    /// Puts the persistent font index in <paramref name="directory"/> instead of the user's cache
    /// directory.
    /// </summary>
    /// <remarks>
    /// The index records each host font's path, family and fingerprint, so the first world of a
    /// new process reads it instead of parsing every font file again. It is a cache: it is
    /// rewritten when a font file changed and it is safe to delete.
    /// </remarks>
    public TypstWorldBuilder WithFontIndexCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _fontCache = true;
        _fontCacheDirectory = directory;
        return this;
    }

    /// <summary>Turns the persistent font index off: every process scans the fonts itself.</summary>
    public TypstWorldBuilder WithoutFontIndexCache()
    {
        _fontCache = false;
        _fontCacheDirectory = null;
        return this;
    }

    /// <summary>Adjusts the hard caps applied to registered inputs.</summary>
    public TypstWorldBuilder WithLimits(Action<TypstLimits> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_limits);
        return this;
    }

    /// <summary>
    /// Configures where <c>@namespace/name:version</c> packages come from. Offline by default.
    /// </summary>
    public TypstWorldBuilder WithPackages(Action<TypstPackages> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_packages);
        _packages.Validate();
        return this;
    }

    /// <summary>Creates the world and registers everything collected so far.</summary>
    public TypstWorld Build()
    {
        // A mirror is checked here, before the native world exists: a package the document imports
        // and the mirror does not hold fails with the mirror named, which is the difference between
        // "check the import" and "fill the mirror".
        if (_packages.MirrorDirectory is { } mirror)
        {
            TypstPackageMirror.EnsureSatisfied(mirror, _sources.Values);
        }

        var config = new WorldConfigDto
        {
            Clock = _systemClock
                ? new ClockDto { Kind = "system" }
                : new ClockDto
                {
                    Kind = "fixed",
                    Year = _fixedClock.Year,
                    Month = _fixedClock.Month,
                    Day = _fixedClock.Day,
                    Hour = _fixedClock.Hour,
                    Minute = _fixedClock.Minute,
                    Second = _fixedClock.Second,
                },
            Limits = new LimitsDto
            {
                MaxSourceBytes = _limits.MaxSourceBytes,
                MaxAssetBytes = _limits.MaxAssetBytes,
                MaxFontBytes = _limits.MaxFontBytes,
                MaxSources = _limits.MaxSources,
                MaxFonts = _limits.MaxFonts,
            },
            Packages = new PackagesDto
            {
                CacheDir = _packages.CacheDirectory,
                DataDir = _packages.DataDirectory,
                Download = _packages.AllowDownload,
                Registry = _packages.Registry,
                Mirror = _packages.MirrorDirectory,
            },
            Fonts = new FontsDto
            {
                Directories = [.. _fontDirectories],
                System = _systemFonts,
                Refresh = _refreshFonts,
                Cache = _fontCache,
                CacheDir = _fontCacheDirectory,
            },
        };

        var json = JsonSerializer.Serialize(config, TypstJsonContext.Default.WorldConfigDto);

        try
        {
            var handle = _native.CreateWorld(json);

            try
            {
                foreach (var (path, text) in _sources)
                {
                    _native.AddSource(handle, path, Encoding.UTF8.GetBytes(text));
                }

                foreach (var (path, bytes) in _assets)
                {
                    _native.AddAsset(handle, path, bytes);
                }

                foreach (var (bytes, faceIndex) in _fonts)
                {
                    _native.AddFont(handle, bytes, faceIndex);
                }
            }
            catch
            {
                handle.Dispose();
                throw;
            }

            return new TypstWorld(_native, handle);
        }
        catch (NativeCallException exception)
        {
            throw NativeErrorTranslator.Translate(exception);
        }
    }
}
