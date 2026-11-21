using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace M3scalinum.Text.Pdf;

/// <summary>What a signature says about itself, beyond the certificate.</summary>
public sealed class PdfSignatureOptions
{
    /// <summary>The field name a reader shows. Default <c>Signature1</c>.</summary>
    public string FieldName { get; init; } = "Signature1";

    /// <summary>Why the document is signed, as the signer declares it. Default: none.</summary>
    public string? Reason { get; init; }

    /// <summary>Where the signature was made. Default: none.</summary>
    public string? Location { get; init; }

    /// <summary>
    /// The moment the signature claims, in UTC. Default: the current time.
    /// </summary>
    /// <remarks>
    /// Set it when the signature must be reproducible: the same document, certificate and timestamp
    /// give the same bytes, which a test can then assert on.
    /// </remarks>
    public DateTimeOffset? SignedAt { get; init; }

    /// <summary>
    /// An RFC 3161 authority to timestamp the signature value with, turning PAdES-B into PAdES-B-T.
    /// Default: no timestamp.
    /// </summary>
    /// <remarks>
    /// The library never picks a TSA and never opens a connection on its own: <see cref="TimestampClient"/>
    /// must be supplied too. The token is stored as the <c>id-aa-signatureTimeStampToken</c> unsigned
    /// attribute of the signer, which is what a verifier reads to establish <em>when</em> the
    /// signature existed.
    /// </remarks>
    public Uri? TimestampAuthority { get; init; }

    /// <summary>The client used to reach <see cref="TimestampAuthority"/>. Default: none.</summary>
    /// <remarks>
    /// The caller owns and disposes it. Supplying one keeps the request on a socket the caller
    /// controls — with a proxy, a test handler, or a pinned certificate — instead of whatever the
    /// ambient configuration would pick.
    /// </remarks>
    public HttpClient? TimestampClient { get; init; }

    /// <summary>
    /// A visible signature: the page and the rectangle the reader draws. Default: an invisible
    /// signature, with a zero rectangle.
    /// </summary>
    public PdfSignatureAppearance? Appearance { get; init; }

    /// <summary>
    /// A document security store to embed, turning PAdES-B-T into PAdES-B-LT. Default: none.
    /// </summary>
    /// <remarks>
    /// The store is what lets a reader validate the document years later, when the certificates have
    /// expired and the OCSP responder is gone. It carries the chain and the revocation data the
    /// caller supplies; this library does not fetch either.
    /// </remarks>
    public PdfDssOptions? Dss { get; init; }
}

/// <summary>
/// A visible signature appearance: where it sits on the page, and what it draws.
/// </summary>
/// <remarks>
/// The appearance is written as a form XObject and referenced from the widget annotation's
/// <c>/AP</c>, which is what a reader renders. It is added in the same incremental update as the
/// signature, so the page's earlier revision is untouched.
/// </remarks>
public sealed class PdfSignatureAppearance
{
    /// <summary>The page the signature is drawn on, counted from zero. Default: the first page.</summary>
    public int PageIndex { get; init; }

    /// <summary>The left edge of the rectangle, in PDF points, from the left of the page.</summary>
    public double X { get; init; }

    /// <summary>The bottom edge of the rectangle, in PDF points, from the bottom of the page.</summary>
    public double Y { get; init; }

    /// <summary>The width of the rectangle, in PDF points.</summary>
    public double Width { get; init; }

    /// <summary>The height of the rectangle, in PDF points.</summary>
    public double Height { get; init; }

    /// <summary>
    /// The lines of text to draw, top to bottom. Default: none.
    /// </summary>
    /// <remarks>
    /// Text is written with the Helvetica standard font and the WinAnsi encoding, so the characters
    /// outside that set are replaced by <c>?</c>. Set <see cref="Image"/> instead to draw a picture.
    /// </remarks>
    public IReadOnlyList<string>? TextLines { get; init; }

    /// <summary>
    /// A PNG or JPEG image to draw, as the bytes of the file. Default: none.
    /// </summary>
    /// <remarks>
    /// A JPEG is embedded as it is, with <c>/DCTDecode</c>. A PNG is decoded — 8-bit, non-interlaced,
    /// grey, RGB, indexed or RGBA — and stored flate-compressed; the alpha channel of an RGBA image
    /// becomes a soft mask.
    /// </remarks>
    public ReadOnlyMemory<byte>? Image { get; init; }

    /// <summary>The size of the text, in points. Default 10.</summary>
    public double FontSize { get; init; } = 10;

    /// <summary>The space left between the rectangle's edge and what it draws, in points. Default 4.</summary>
    public double Padding { get; init; } = 4;

    /// <summary>Checks that the appearance can be drawn, and says what is wrong when it cannot.</summary>
    /// <exception cref="PdfSignatureException">The geometry or the content is not usable.</exception>
    internal void Validate()
    {
        if (PageIndex < 0)
        {
            throw new PdfSignatureException("the appearance page index is negative; pages are counted from zero");
        }

        if (Width <= 0 || Height <= 0)
        {
            throw new PdfSignatureException(
                $"the appearance rectangle is {Width} by {Height} points; both sides must be positive");
        }

        if (double.IsNaN(X) || double.IsNaN(Y) || double.IsInfinity(X) || double.IsInfinity(Y))
        {
            throw new PdfSignatureException("the appearance rectangle starts at a coordinate that is not a number");
        }

        if (FontSize <= 0)
        {
            throw new PdfSignatureException($"the appearance font size is {FontSize} points; it must be positive");
        }

        if (Padding < 0)
        {
            throw new PdfSignatureException($"the appearance padding is {Padding} points; it cannot be negative");
        }

        var hasText = TextLines is { Count: > 0 };
        var hasImage = Image is { Length: > 0 };

        if (hasText && hasImage)
        {
            throw new PdfSignatureException(
                "the appearance carries both text lines and an image; set one or the other");
        }

        if (!hasText && !hasImage)
        {
            throw new PdfSignatureException(
                "the appearance carries neither text lines nor an image, so it would draw nothing");
        }
    }
}

/// <summary>
/// The material a document security store carries, so a signature can be validated long after it
/// was made: the certificates of the chain, and the revocation data for them.
/// </summary>
/// <remarks>
/// Every list is caller-supplied on purpose. Fetching a CRL or an OCSP response means naming a URL
/// the document chose, over a network the library does not own; a signature library that does that
/// silently is a signature library that leaks where its documents are opened. Collect the bytes
/// wherever the caller already has them, and pass them here.
/// </remarks>
public sealed class PdfDssOptions
{
    /// <summary>
    /// The certificates to embed, DER-encoded. Add the whole chain, and the timestamp authority's
    /// certificate when the signature carries a timestamp.
    /// </summary>
    public IReadOnlyList<X509Certificate2> Certificates { get; init; } = [];

    /// <summary>The OCSP responses to embed, as the responder returned them (DER).</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> OcspResponses { get; init; } = [];

    /// <summary>The certificate revocation lists to embed, as DER.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Crls { get; init; } = [];
}
