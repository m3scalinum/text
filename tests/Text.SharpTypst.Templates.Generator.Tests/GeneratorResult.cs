using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace M3scalinum.Text.SharpTypst.Templates.Generator.Tests;

internal sealed class GeneratorResult(
    IReadOnlyList<string> generated,
    ImmutableArray<Diagnostic> diagnostics,
    IReadOnlyList<string> errors)
{
    public IReadOnlyList<string> Generated { get; } = generated;

    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;

    public IReadOnlyList<string> Errors { get; } = errors;

    public string Single => Generated.Count == 1
        ? Generated[0]
        : throw new InvalidOperationException($"expected one generated file, got {Generated.Count}");

    public IReadOnlyList<string> DiagnosticIds => Diagnostics.Select(d => d.Id).ToList();
}