namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// Hard caps on what a world accepts. They bound memory in a host that renders
/// user-supplied templates; they are not a sandbox.
/// </summary>
public sealed class TypstLimits
{
    /// <summary>Maximum size of a single source, in bytes. Default 16 MiB.</summary>
    public int MaxSourceBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Maximum size of a single asset, in bytes. Default 64 MiB.</summary>
    public int MaxAssetBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>Maximum size of a single font, in bytes. Default 64 MiB.</summary>
    public int MaxFontBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>Maximum number of registered sources. Default 1024.</summary>
    public int MaxSources { get; set; } = 1024;

    /// <summary>Maximum number of registered fonts. Default 256.</summary>
    public int MaxFonts { get; set; } = 256;
}
