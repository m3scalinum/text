using M3scalinum.Text.Typst.Compilation;

namespace M3scalinum.Text.Typst.DependencyInjection;

/// <summary>
/// Creates <see cref="TypstWorldBuilder"/> instances already configured from
/// <see cref="TypstOptions"/>.
/// </summary>
/// <remarks>
/// Inject this rather than <see cref="TypstWorldBuilder"/> when you need a builder inside a loop or
/// a parallel body: a transient builder captured by a singleton is a captive dependency, a factory
/// call is not.
/// </remarks>
public interface ITypstWorldFactory
{
    /// <summary>Returns a new builder carrying the configured limits, packages and clock.</summary>
    TypstWorldBuilder Create();
}
