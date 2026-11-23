using System;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// Where <c>@namespace/name:version</c> packages come from.
/// </summary>
/// <remarks>
/// Offline by default: a compile never touches the network unless <see cref="AllowDownload"/> is
/// set. A package already present in <see cref="CacheDirectory"/> or <see cref="DataDirectory"/>
/// resolves either way, so a host can pre-seed the cache and stay offline.
/// </remarks>
public sealed class TypstPackages
{
    /// <summary>
    /// Directory for downloaded packages. <see langword="null"/> uses the system cache directory
    /// (<c>%LOCALAPPDATA%\typst\packages</c>, <c>~/.cache/typst/packages</c>, …).
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Directory for local packages. <see langword="null"/> uses the system data directory
    /// (<c>%APPDATA%\typst\packages</c>, <c>~/.local/share/typst/packages</c>, …).
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>
    /// Whether a package missing from both directories may be fetched from the registry.
    /// Default <see langword="false"/>.
    /// </summary>
    public bool AllowDownload { get; set; }

    /// <summary>
    /// Registry base URL. <see langword="null"/> uses the official Typst Universe
    /// (<c>https://packages.typst.org</c>).
    /// </summary>
    public string? Registry { get; set; }

    /// <summary>
    /// Directory of a package mirror: the packages themselves plus a <c>mirror.json</c> listing
    /// every file's SHA-256.
    /// </summary>
    /// <remarks>
    /// Setting it puts the world in cache-only mode. The mirror becomes both package directories,
    /// downloads are refused, and the native layer verifies the manifest before the world is built,
    /// refusing a file whose digest is not the one recorded — naming the package, the file and the
    /// mirror. A document that imports a package the mirror does not hold fails with a message
    /// naming the package and the mirror.
    /// <para>
    /// The manifest proves integrity, not authenticity: a mirror that can rewrite a file can
    /// rewrite the manifest, so it has to come from somewhere this deployment trusts. Fill one with
    /// <c>tools/typst-mirror.py</c> and verify it with <c>--verify</c>.
    /// </para>
    /// </remarks>
    public string? MirrorDirectory { get; set; }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when a setting is malformed.
    /// </summary>
    /// <remarks>Called automatically by <c>WithPackages</c>; exposed so a host can validate its own configuration.</remarks>
    public void Validate()
    {
        if (CacheDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(CacheDirectory, nameof(CacheDirectory));
        }

        if (DataDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(DataDirectory, nameof(DataDirectory));
        }

        if (MirrorDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(MirrorDirectory, nameof(MirrorDirectory));

            if (AllowDownload)
            {
                throw new ArgumentException(
                    "a package mirror is cache-only: AllowDownload must stay false",
                    nameof(AllowDownload));
            }
        }

        if (Registry is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Registry, nameof(Registry));

            if (!Uri.TryCreate(Registry, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new ArgumentException(
                    $"'{Registry}' is not an absolute http(s) URL",
                    nameof(Registry));
            }
        }
    }
}
