namespace M3scalinum.Text.Typst.Internal;

internal sealed class PackagesDto
{
    public string? CacheDir { get; set; }

    public string? DataDir { get; set; }

    public bool Download { get; set; }

    public string? Registry { get; set; }

    public string? Mirror { get; set; }
}