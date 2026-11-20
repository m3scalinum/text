using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.SharpTypst.Export;

namespace M3scalinum.Text.SharpTypst.DependencyInjection;

/// <summary>Rejects a <see cref="TypstOptions"/> that would fail on first use.</summary>
internal sealed class TypstOptionsValidator : IValidateOptions<TypstOptions>
{
    public ValidateOptionsResult Validate(string? name, TypstOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        Check(failures, options.Limits.MaxSourceBytes, nameof(TypstLimits.MaxSourceBytes));
        Check(failures, options.Limits.MaxAssetBytes, nameof(TypstLimits.MaxAssetBytes));
        Check(failures, options.Limits.MaxFontBytes, nameof(TypstLimits.MaxFontBytes));
        Check(failures, options.Limits.MaxSources, nameof(TypstLimits.MaxSources));
        Check(failures, options.Limits.MaxFonts, nameof(TypstLimits.MaxFonts));

        if (options.Png.Ppi <= 0)
        {
            failures.Add($"{nameof(TypstOptions.Png)}.{nameof(PngExportOptions.Ppi)} must be positive.");
        }

        try
        {
            options.Packages.Validate();
        }
        catch (ArgumentException exception)
        {
            failures.Add(exception.Message);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Check(List<string> failures, int value, string property)
    {
        if (value <= 0)
        {
            failures.Add($"{nameof(TypstOptions.Limits)}.{property} must be positive.");
        }
    }
}
