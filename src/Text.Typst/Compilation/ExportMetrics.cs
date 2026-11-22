using System;
using M3scalinum.Text.Typst.Export;

namespace M3scalinum.Text.Typst.Compilation;

/// <summary>What one export of a <see cref="CompilationResult"/> cost.</summary>
/// <remarks>
/// Separate from <see cref="CompilationMetrics"/> because the size cannot exist before the encoding
/// runs: a document's byte count is a property of the export, not of the layout. A caller that
/// wants the size reads it here, after the export, instead of forcing every compilation to encode
/// a PDF it may never ask for.
/// </remarks>
/// <param name="Format">The export that produced these numbers.</param>
/// <param name="Bytes">Bytes written to the caller: the array length, or what the stream received.</param>
/// <param name="Duration">How long the encoding took, from the call to the bytes being ready.</param>
public sealed record ExportMetrics(ExportFormat Format, long Bytes, TimeSpan Duration);
