using System;
using System.Collections.Generic;
using System.Linq;

namespace M3scalinum.Text.Pdf.FactureX;

/// <summary>The outcome of validating an invoice against the EN 16931 business rules.</summary>
public sealed class FacturXValidationResult
{
    internal FacturXValidationResult(IReadOnlyList<FacturXIssue> issues)
    {
        Issues = issues;
    }

    /// <summary>Every rule the document breaks. Empty when it is valid.</summary>
    public IReadOnlyList<FacturXIssue> Issues { get; }

    /// <summary>Whether the document satisfies every rule.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>The issues as one line per rule, for a log or an exception message.</summary>
    public string Summary => string.Join(Environment.NewLine, Issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    /// <summary>Throws when the document is invalid.</summary>
    /// <exception cref="FacturXValidationException">At least one rule is broken.</exception>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new FacturXValidationException(this);
        }
    }
}