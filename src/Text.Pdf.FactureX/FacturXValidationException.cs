using System;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>An invoice that breaks at least one EN 16931 rule.</summary>
public sealed class FacturXValidationException : Exception
{
    /// <summary>Creates the exception from the validation result.</summary>
    public FacturXValidationException(FacturXValidationResult result)
        : base($"{result.Issues.Count} rule(s) violated:{Environment.NewLine}{result.Summary}")
    {
        Result = result;
    }

    /// <summary>The full result, so a caller can report every issue at once.</summary>
    public FacturXValidationResult Result { get; }
}