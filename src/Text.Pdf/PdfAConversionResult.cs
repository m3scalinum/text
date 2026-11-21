using System;
using System.Collections.Generic;
using System.Linq;

namespace M3scalinum.Text.Pdf;

/// <summary>What the converter changed, or what it refused to change.</summary>
public sealed class PdfAConversionChange
{
    internal PdfAConversionChange(string code, string target, string message)
    {
        Code = code;
        Target = target;
        Message = message;
    }

    /// <summary>A stable code, one of <see cref="PdfAConversionCodes"/>.</summary>
    public string Code { get; }

    /// <summary>What the change concerns: <c>header</c>, <c>catalog</c>, <c>xmp</c>, <c>obj 43</c>.</summary>
    public string Target { get; }

    /// <summary>What was done, in one sentence.</summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Code} ({Target}): {Message}";
}

/// <summary>A step the conversion could not take, and the bytes it therefore did not produce.</summary>
public sealed class PdfAConversionRefusal
{
    internal PdfAConversionRefusal(string code, string target, string message)
    {
        Code = code;
        Target = target;
        Message = message;
    }

    /// <summary>A stable code, one of <see cref="PdfAConversionCodes"/>.</summary>
    public string Code { get; }

    /// <summary>What the refusal concerns: <c>trailer</c>, <c>page 3</c>, <c>obj 12</c>.</summary>
    public string Target { get; }

    /// <summary>Why the step could not be taken, in one sentence.</summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Code} ({Target}): {Message}";
}

/// <summary>The outcome of <see cref="PdfAConverter.Convert"/>.</summary>
/// <remarks>
/// <para>
/// <see cref="IsConformant"/> is not a claim the converter makes about its own work: it is the
/// verdict of <see cref="PdfConformance.Check"/> on <see cref="Bytes"/>, exactly as a caller would
/// run it. A file that was rewritten but still breaks a rule comes back with a report naming the
/// rules and with <see cref="IsConformant"/> false.
/// </para>
/// <para>
/// When a step could not be taken <see cref="Bytes"/> is empty and <see cref="Report"/> is
/// <see langword="null"/>: nothing was written, and <see cref="Refusals"/> says why.
/// </para>
/// </remarks>
public sealed class PdfAConversionResult
{
    internal PdfAConversionResult(
        PdfConformanceStandard target,
        byte[] bytes,
        PdfConformanceReport? report,
        IReadOnlyList<PdfAConversionChange> changes,
        IReadOnlyList<PdfAConversionRefusal> refusals)
    {
        Target = target;
        Bytes = bytes;
        Report = report;
        Changes = changes;
        Refusals = refusals;
    }

    /// <summary>The standard the conversion was asked for.</summary>
    public PdfConformanceStandard Target { get; }

    /// <summary>The converted document, or an empty array when the conversion refused to write one.</summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// The conformance report for <see cref="Bytes"/>, or <see langword="null"/> when no bytes were
    /// produced.
    /// </summary>
    public PdfConformanceReport? Report { get; }

    /// <summary>What the converter changed, in the order it changed it.</summary>
    public IReadOnlyList<PdfAConversionChange> Changes { get; }

    /// <summary>Every step the converter could not take. Non-empty means nothing was written.</summary>
    public IReadOnlyList<PdfAConversionRefusal> Refusals { get; }

    /// <summary>Whether a document was produced.</summary>
    public bool HasBytes => Bytes.Length > 0;

    /// <summary>
    /// Whether <see cref="PdfConformance.Check"/> found no error in the bytes produced under
    /// <see cref="Target"/>. False when the conversion refused, and false when it wrote a file that
    /// still breaks a rule.
    /// </summary>
    public bool IsConformant => Report is { IsConformant: true };

    /// <summary>The errors the produced file still has, empty when it conforms.</summary>
    public IEnumerable<PdfConformanceIssue> Errors
        => Report?.Errors ?? [];

    /// <summary>Throws when the result is not conformant, carrying the report.</summary>
    /// <exception cref="PdfConformanceException">
    /// The conversion refused, or the produced file still breaks a rule. The exception's message
    /// names the refusals when there is no report to name them.
    /// </exception>
    public void EnsureConformant()
    {
        if (Report is null || !IsConformant)
        {
            throw new PdfConformanceException(Report ?? RefusalReport());
        }
    }

    private PdfConformanceReport RefusalReport()
    {
        var issues = Refusals
            .Select(refusal => new PdfConformanceIssue(
                refusal.Code,
                PdfConformanceSeverity.Error,
                refusal.Target,
                refusal.Message))
            .ToList();

        return new PdfConformanceReport(Target, null, issues);
    }

    /// <inheritdoc />
    public override string ToString()
        => HasBytes
            ? $"{Target}: {Changes.Count} change(s), {(IsConformant ? "conformant" : $"{Errors.Count()} error(s)")}"
            : $"{Target}: refused, {Refusals.Count} reason(s)";
}
