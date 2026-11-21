using M3scalinum.Text.Typst.Compilation;
using M3scalinum.Text.Typst.Export;

namespace M3scalinum.Text.Typst.DependencyInjection;

/// <summary>
/// Everything <c>AddTypst</c> can configure.
/// </summary>
/// <remarks>
/// The world settings (<see cref="Limits"/>, <see cref="Packages"/>, <see cref="Clock"/>) are
/// applied to every <see cref="TypstWorldBuilder"/> the container hands out. The export settings
/// (<see cref="Svg"/>, <see cref="Png"/>, <see cref="Html"/>) are defaults a host can read and pass
/// to <see cref="CompilationResult"/>; the library never applies them implicitly, because an export
/// is always an explicit call with explicit options.
/// </remarks>
public sealed class TypstOptions
{
    /// <summary>Hard caps applied to registered inputs.</summary>
    public TypstLimits Limits { get; } = new();

    /// <summary>Where <c>@namespace/name:version</c> packages come from. Offline by default.</summary>
    public TypstPackages Packages { get; } = new();

    /// <summary>What the compiler reports as the current date.</summary>
    public TypstClockOptions Clock { get; } = new();

    /// <summary>Defaults passed to <see cref="TypstCompiler.Compile"/> when the caller passes none.</summary>
    public TypstCompilationOptions Compilation { get; set; } = new();

    /// <summary>Default options for SVG export.</summary>
    public SvgExportOptions Svg { get; set; } = new();

    /// <summary>Default options for PNG export.</summary>
    public PngExportOptions Png { get; set; } = new();

    /// <summary>Default options for HTML export.</summary>
    public HtmlExportOptions Html { get; set; } = new();
}
