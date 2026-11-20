using System;

namespace M3scalinum.Text.SharpTypst.Compilation;

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
