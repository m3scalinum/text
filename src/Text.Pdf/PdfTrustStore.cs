using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// The roots and intermediates a verification may rely on. Supplied always, never discovered.
/// </summary>
/// <remarks>
/// Build it from the certificates the caller already trusts — the ones their organisation published,
/// or the ones a test generated. An empty store is meaningful: it says "do not judge the chain", and
/// every signature then reports <see cref="PdfChainStatus.NotEvaluated"/>.
/// </remarks>
public sealed class PdfTrustStore
{
    /// <summary>An empty store: no anchor, so no chain judgement.</summary>
    public static PdfTrustStore Empty { get; } = new();

    /// <summary>The certificates to treat as roots. Anything else is not a trust anchor.</summary>
    public IReadOnlyList<X509Certificate2> Anchors { get; init; } = [];

    /// <summary>Certificates to offer the chain builder on the way to an anchor.</summary>
    public IReadOnlyList<X509Certificate2> Intermediates { get; init; } = [];
}
