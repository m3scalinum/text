using M3scalinum.Text.SharpTypst.Compilation;

namespace M3scalinum.Text.SharpTypst.DependencyInjection;

/// <summary>Builds a <see cref="TypstWorldBuilder"/> from a <see cref="TypstOptions"/> snapshot.</summary>
internal static class TypstWorldBuilderFactory
{
    public static TypstWorldBuilder Create(TypstOptions options)
    {
        var builder = new TypstWorldBuilder();

        builder.WithLimits(limits =>
        {
            limits.MaxSourceBytes = options.Limits.MaxSourceBytes;
            limits.MaxAssetBytes = options.Limits.MaxAssetBytes;
            limits.MaxFontBytes = options.Limits.MaxFontBytes;
            limits.MaxSources = options.Limits.MaxSources;
            limits.MaxFonts = options.Limits.MaxFonts;
        });

        builder.WithPackages(packages =>
        {
            packages.CacheDirectory = options.Packages.CacheDirectory;
            packages.DataDirectory = options.Packages.DataDirectory;
            packages.AllowDownload = options.Packages.AllowDownload;
            packages.Registry = options.Packages.Registry;
        });

        if (options.Clock.UseSystemClock)
        {
            builder.WithSystemClock();
        }
        else
        {
            builder.WithFixedClock(options.Clock.FixedInstant);
        }

        return builder;
    }
}
