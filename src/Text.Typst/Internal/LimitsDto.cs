namespace M3scalinum.Text.Typst.Internal;

internal sealed class LimitsDto
{
    public int MaxSourceBytes { get; set; } = 16 * 1024 * 1024;

    public int MaxAssetBytes { get; set; } = 64 * 1024 * 1024;

    public int MaxFontBytes { get; set; } = 64 * 1024 * 1024;

    public int MaxSources { get; set; } = 1024;

    public int MaxFonts { get; set; } = 256;
}