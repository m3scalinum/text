using System;

namespace M3scalinum.Text.Ocr;

/// <summary>
/// How a PDF is rasterised before it is read, and the thresholds that decide whether
/// a page is readable.
/// </summary>
/// <remarks>
/// 300 DPI is the usual scan resolution and the default: below roughly 150 DPI the
/// recogniser loses small text, and above roughly 600 DPI it costs time without
/// gaining accuracy. The confidence thresholds are <see cref="OcrOptions"/>'s, and
/// they apply to every page.
/// </remarks>
public sealed class OcrPdfOptions : OcrOptions
{
    /// <summary>The default resolution.</summary>
    public const int DefaultDpi = 300;

    /// <summary>The lowest accepted resolution.</summary>
    public const int MinimumDpi = 72;

    /// <summary>The highest accepted resolution.</summary>
    public const int MaximumDpi = 1200;

    /// <summary>
    /// The resolution pages are rendered at. Between <see cref="MinimumDpi"/> and
    /// <see cref="MaximumDpi"/>; defaults to <see cref="DefaultDpi"/>.
    /// </summary>
    public int Dpi { get; init; } = DefaultDpi;

    /// <summary>The zero-based first page to read. Defaults to the first page.</summary>
    public int FirstPage { get; init; }

    /// <summary>
    /// The zero-based last page to read, inclusive. <see langword="null"/> reads to
    /// the end of the document.
    /// </summary>
    public int? LastPage { get; init; }

    internal override void Validate()
    {
        base.Validate();

        if (Dpi is < MinimumDpi or > MaximumDpi)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Dpi), Dpi, $"The DPI must be between {MinimumDpi} and {MaximumDpi}.");
        }

        if (FirstPage < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FirstPage), FirstPage, "The first page must not be negative.");
        }

        if (LastPage is { } last && last < FirstPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LastPage), last, "The last page must not precede the first page.");
        }
    }
}

/// <summary>
/// Raised when the native engine refuses an input: a model it cannot load, an image
/// it cannot decode, or a PDF it cannot render.
/// </summary>
/// <remarks>
/// <para>
/// A model refusal fills <see cref="Defect"/>, <see cref="Role"/> and — when the
/// model came from a directory — <see cref="FilePath"/>. Those three are what a
/// caller acts on; <see cref="Exception.Message"/> says the same thing in a sentence,
/// with the numbers that decided it.
/// </para>
/// <para>
/// It is a single type rather than a subtype per failure so a caller writes one
/// catch, and so a library newer than this package cannot hand a host an exception
/// type it has never seen.
/// </para>
/// </remarks>
public class OcrException : Exception
{
    /// <summary>Creates the exception.</summary>
    public OcrException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public OcrException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public OcrException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal OcrException(string code, string message)
        : base(message) => Code = code;

    internal OcrException(
        string code,
        string message,
        OcrModelRole role,
        OcrModelDefect defect,
        string? filePath)
        : base(message)
    {
        Code = code;
        Role = role;
        Defect = defect;
        FilePath = filePath;
    }

    /// <summary>
    /// The native error code: <c>invalid_argument</c>, <c>model</c>, <c>image</c>,
    /// <c>pdf</c>, <c>ocr</c> or <c>panic</c>.
    /// </summary>
    public string Code { get; } = "unknown";

    /// <summary>
    /// Which model the refusal concerns, or <see cref="OcrModelRole.Unspecified"/>
    /// when the failure is not about a model.
    /// </summary>
    public OcrModelRole Role { get; } = OcrModelRole.Unspecified;

    /// <summary>
    /// What is wrong with the model, or <see cref="OcrModelDefect.Unspecified"/> when
    /// the failure is not a model refusal.
    /// </summary>
    public OcrModelDefect Defect { get; } = OcrModelDefect.Unspecified;

    /// <summary>
    /// The model file the refusal concerns, when it came from a directory;
    /// <see langword="null"/> when the caller passed bytes or the failure is not about
    /// a model.
    /// </summary>
    public string? FilePath { get; }

    /// <inheritdoc />
    public override string ToString()
        => Defect == OcrModelDefect.Unspecified
            ? base.ToString()
            : FilePath is null
                ? $"{base.ToString()} (role: {Role}, defect: {Defect})"
                : $"{base.ToString()} (role: {Role}, defect: {Defect}, file: {FilePath})";
}
