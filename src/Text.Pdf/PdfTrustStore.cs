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
/// <para>
/// A trust list is a statement of qualification, not of anchorhood, so it does not become a store on
/// its own. The one thing it does name that a store can hold is who signs the national lists: see
/// <see cref="ForNationalList"/>.
/// </para>
/// </remarks>
public sealed class PdfTrustStore
{
    /// <summary>An empty store: no anchor, so no chain judgement.</summary>
    public static PdfTrustStore Empty { get; } = new();

    /// <summary>The certificates to treat as roots. Anything else is not a trust anchor.</summary>
    public IReadOnlyList<X509Certificate2> Anchors { get; init; } = [];

    /// <summary>Certificates to offer the chain builder on the way to an anchor.</summary>
    public IReadOnlyList<X509Certificate2> Intermediates { get; init; } = [];

    /// <summary>
    /// The anchors a list-of-lists publishes for the national list of <paramref name="territory"/>:
    /// the certificates its <c>OtherTSLPointer</c> binds to that list's operator.
    /// </summary>
    /// <param name="listOfLists">The LOTL, as read by <see cref="PdfLotl.Read(ReadOnlySpan{byte}, string)"/>.</param>
    /// <param name="territory">The two-letter territory of the national list, as the pointer states it.</param>
    /// <returns>A store anchored on those certificates; <see cref="Empty"/> when the list points at
    /// no list for that territory, or its pointer names no certificate.</returns>
    /// <remarks>
    /// This is how a caller judges a national list's own signature: read the LOTL, build the store
    /// this method returns, and check the national list's XAdES signature against it. The chain
    /// judgement is the only half this package performs — the XAdES signature over the XML is the
    /// caller's XML-DSig tooling, because this reader does not verify signatures it did not find in
    /// a PDF.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="listOfLists"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="territory"/> is empty.</exception>
    public static PdfTrustStore ForNationalList(PdfTrustedList listOfLists, string territory)
    {
        ArgumentNullException.ThrowIfNull(listOfLists);
        ArgumentException.ThrowIfNullOrEmpty(territory);

        var anchors = new List<X509Certificate2>();

        foreach (var pointer in listOfLists.NationalLists)
        {
            if (string.Equals(pointer.Territory, territory, StringComparison.OrdinalIgnoreCase))
            {
                anchors.AddRange(pointer.Certificates);
            }
        }

        return anchors.Count == 0
            ? Empty
            : new PdfTrustStore { Anchors = anchors };
    }
}
