using System.Collections.Generic;

namespace M3scalinum.Text.Typst.Internal;

internal sealed class FontsDto
{
    public List<string> Directories { get; set; } = [];

    public bool System { get; set; }

    /// <summary>Whether the native side scans again instead of reusing its process index.</summary>
    public bool Refresh { get; set; }

    /// <summary>Whether the persistent index is read and written.</summary>
    public bool Cache { get; set; } = true;

    /// <summary>Where the persistent index lives; <c>null</c> means the user's cache directory.</summary>
    public string? CacheDir { get; set; }
}