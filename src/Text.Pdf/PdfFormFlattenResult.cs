using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>What flattening did: the fields whose appearance was moved into the page, and the ones it refused.</summary>
/// <remarks>
/// A field is only flattened when its widget carries an appearance stream this writer can place on
/// the page. Anything it cannot draw is left interactive, and remains in the <c>/AcroForm</c>
/// dictionary: a form whose values disappeared would be worse than one that is still editable.
/// </remarks>
public sealed class PdfFormFlattenResult
{
    internal PdfFormFlattenResult(
        byte[] document,
        IReadOnlyList<string> flattened,
        IReadOnlyList<PdfFormFlattenSkip> skipped)
    {
        Document = document;
        Flattened = flattened;
        Skipped = skipped;
    }

    /// <summary>
    /// The flattened document. When no field could be flattened it is the input, unchanged: the
    /// operation never writes a file with fewer values than it started from.
    /// </summary>
    public byte[] Document { get; }

    /// <summary>The fully qualified names of the fields that were flattened, in tree order.</summary>
    public IReadOnlyList<string> Flattened { get; }

    /// <summary>The fields that were left alone, each with the reason.</summary>
    public IReadOnlyList<PdfFormFlattenSkip> Skipped { get; }

    /// <summary>Whether every field was flattened, which is also when <c>/AcroForm</c> was removed.</summary>
    public bool IsComplete => Skipped.Count == 0;
}

/// <summary>A field flattening refused, and why.</summary>
/// <param name="FieldName">The fully qualified field name, or a placeholder when it has none.</param>
/// <param name="Reason">What stopped the flatten, in a sentence a caller can log.</param>
public sealed record PdfFormFlattenSkip(string FieldName, string Reason);
