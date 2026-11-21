using Microsoft.Extensions.Options;
using M3scalinum.Text.Typst.Compilation;

namespace M3scalinum.Text.Typst.DependencyInjection;

/// <summary>Creates builders from the container's <see cref="TypstOptions"/>.</summary>
internal sealed class TypstWorldFactory(IOptions<TypstOptions> options) : ITypstWorldFactory
{
    public TypstWorldBuilder Create() => TypstWorldBuilderFactory.Create(options.Value);
}
