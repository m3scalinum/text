using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>
/// The managed half of a package mirror: it refuses, before the compiler starts, a document that
/// imports a package the mirror does not hold.
/// </summary>
/// <remarks>
/// The native layer verifies the mirror itself — every file, every digest — when the world is
/// created, so a compile only ever reads bytes the manifest describes. One failure is left over:
/// a package the document imports that the mirror does not hold at all does not fail verification,
/// because the manifest never mentioned it. The compiler's own diagnostic names the package but not
/// the mirror, and "package not found" reads like a typo in the import rather than like a mirror
/// that was not filled; this preflight names the mirror and the command that fills it.
///
/// It reads the sources for <c>@namespace/name:version</c> literals. That is a preflight, not a
/// parser: a spec built at run time still reaches the compiler, whose message names the package
/// from then on, with the mirror named here only when the literal is visible in a source.
/// </remarks>
internal static partial class TypstPackageMirror
{
    /// <summary>Checks that every package a source imports is in the mirror.</summary>
    /// <param name="mirror">The mirror directory the world will resolve packages from.</param>
    /// <param name="sources">The registered sources, as text.</param>
    /// <exception cref="TypstCompilationException">A package the sources import is absent.</exception>
    internal static void EnsureSatisfied(string mirror, IEnumerable<string> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mirror);
        ArgumentNullException.ThrowIfNull(sources);

        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            foreach (Match match in SpecPattern().Matches(source))
            {
                if (!reported.Add(match.Value))
                {
                    continue;
                }

                var directory = Path.Combine(
                    mirror,
                    match.Groups["namespace"].Value,
                    match.Groups["name"].Value,
                    match.Groups["version"].Value);

                if (Directory.Exists(directory))
                {
                    continue;
                }

                throw new TypstCompilationException(
                    $"package `{match.Value}` is not in the package mirror at `{mirror}`; add it with "
                    + $"`python3 tools/typst-mirror.py --mirror {mirror} --package {match.Value}`",
                    []);
            }
        }
    }

    /// <summary>The package specs a source mentions, in the order they appear.</summary>
    internal static IEnumerable<string> Specs(string source)
    {
        foreach (Match match in SpecPattern().Matches(source))
        {
            yield return match.Value;
        }
    }

    [GeneratedRegex(
        @"@(?<namespace>[a-z0-9][a-z0-9-]*)/(?<name>[a-z0-9][a-z0-9-]*):(?<version>\d+\.\d+\.\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SpecPattern();
}
