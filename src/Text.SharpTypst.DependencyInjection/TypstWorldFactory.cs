using Microsoft.Extensions.Options;
using M3scalinum.Text.SharpTypst.Compilation;

namespace M3scalinum.Text.SharpTypst.DependencyInjection;

/// <summary>Creates builders from the container's <see cref="TypstOptions"/>.</summary>
internal sealed class TypstWorldFactory(IOptions<TypstOptions> options) : ITypstWorldFactory
{
    public TypstWorldBuilder Create() => TypstWorldBuilderFactory.Create(options.Value);
}
