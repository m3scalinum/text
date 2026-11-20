using System;

namespace M3scalinum.Text.SharpTypst.Compilation;

/// <summary>
/// Validates virtual paths before they reach the native layer, so a bad path fails with a
/// managed exception naming the argument.
/// </summary>
internal static class TypstPath
{
    internal static void Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"path '{path}' must use forward slashes", nameof(path));
        }

        if (path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"path '{path}' must be relative", nameof(path));
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0)
            {
                throw new ArgumentException(
                    $"path '{path}' contains an empty segment", nameof(path));
            }

            if (segment == "..")
            {
                throw new ArgumentException(
                    $"path '{path}' must not contain '..'", nameof(path));
            }
        }
    }
}
