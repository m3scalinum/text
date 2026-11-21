using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace M3scalinum.Text.Pdf;

/// <summary>How much an issue matters.</summary>
public enum PdfConformanceSeverity
{
    /// <summary>The document breaks a rule the claimed standard requires.</summary>
    Error,

    /// <summary>
    /// The document is not known to break a rule, but something cannot be established from the bytes.
    /// </summary>
    Warning,
}

/// <summary>One finding of <see cref="PdfConformance"/>.</summary>
public sealed class PdfConformanceIssue
{
    internal PdfConformanceIssue(string code, PdfConformanceSeverity severity, string target, string message)
    {
        Code = code;
        Severity = severity;
        Target = target;
        Message = message;
    }

    /// <summary>A stable code, one of <see cref="PdfConformanceCodes"/>.</summary>
    public string Code { get; }

    /// <summary>The severity.</summary>
    public PdfConformanceSeverity Severity { get; }

    /// <summary>
    /// What the issue concerns: <c>catalog</c>, <c>trailer</c>, <c>xref entry 12</c>,
    /// <c>obj 43</c>, <c>page 46</c>, or <c>xmp</c>.
    /// </summary>
    public string Target { get; }

    /// <summary>What is wrong, in one sentence.</summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString()
        => $"{Severity.ToString().ToUpperInvariant()} {Code} ({Target}): {Message}";
}

/// <summary>The PDF version declared by a file header.</summary>
public readonly record struct PdfVersion(int Major, int Minor) : IComparable<PdfVersion>
{
    /// <inheritdoc />
    public int CompareTo(PdfVersion other)
        => Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    /// <summary>Whether <paramref name="left"/> is lower than <paramref name="right"/>.</summary>
    public static bool operator <(PdfVersion left, PdfVersion right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is higher than <paramref name="right"/>.</summary>
    public static bool operator >(PdfVersion left, PdfVersion right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is lower than or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(PdfVersion left, PdfVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is higher than or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(PdfVersion left, PdfVersion right) => left.CompareTo(right) >= 0;
}

/// <summary>The result of <see cref="PdfConformance.Check"/>.</summary>
/// <remarks>
/// A report is a list of findings, not a verdict on every requirement of the standard: see
/// <c>docs/specifications/15-pdf-conformance.md</c> for what is established and what is not.
/// <see cref="IsConformant"/> means no <see cref="PdfConformanceSeverity.Error"/> was found.
/// </remarks>
public sealed class PdfConformanceReport
{
    internal PdfConformanceReport(
        PdfConformanceStandard standard,
        PdfVersion? version,
        IReadOnlyList<PdfConformanceIssue> issues)
    {
        Standard = standard;
        Version = version;
        Issues = issues;
    }

    /// <summary>The standard the document was checked against.</summary>
    public PdfConformanceStandard Standard { get; }

    /// <summary>The version read from the header, with the catalog override applied; <see langword="null"/> when unreadable.</summary>
    public PdfVersion? Version { get; }

    /// <summary>Every finding, errors first, then warnings.</summary>
    public IReadOnlyList<PdfConformanceIssue> Issues { get; }

    /// <summary>Whether no issue of severity <see cref="PdfConformanceSeverity.Error"/> was found.</summary>
    public bool IsConformant => !Issues.Any(issue => issue.Severity == PdfConformanceSeverity.Error);

    /// <summary>The errors only.</summary>
    public IEnumerable<PdfConformanceIssue> Errors
        => Issues.Where(issue => issue.Severity == PdfConformanceSeverity.Error);

    /// <summary>The warnings only.</summary>
    public IEnumerable<PdfConformanceIssue> Warnings
        => Issues.Where(issue => issue.Severity == PdfConformanceSeverity.Warning);

    /// <summary>Whether the report holds an issue with <paramref name="code"/>.</summary>
    public bool Has(string code)
        => Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString()
        => IsConformant
            ? $"{Standard}: conformant ({Issues.Count} warning(s))"
            : $"{Standard}: not conformant, {Errors.Count()} error(s)";
}

/// <summary>The document does not conform to the standard it was checked against.</summary>
public sealed class PdfConformanceException : Exception
{
    /// <summary>Creates the exception from the report that failed.</summary>
    public PdfConformanceException(PdfConformanceReport report)
        : base(Describe(report))
    {
        Report = report;
    }

    /// <summary>The report, with every issue.</summary>
    public PdfConformanceReport Report { get; }

    private static string Describe(PdfConformanceReport report)
    {
        var errors = report.Errors.ToList();
        var name = PdfStandardRequirements.Of(report.Standard).Name;
        var summary = string.Join(
            "; ",
            errors.Take(5).Select(issue => $"{issue.Code} at {issue.Target}: {issue.Message}"));

        return errors.Count > 5
            ? $"the document is not {name}: {summary}; and {errors.Count - 5} more"
            : $"the document is not {name}: {summary}";
    }
}
