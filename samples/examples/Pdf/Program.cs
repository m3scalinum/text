using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using M3scalinum.Text.Pdf;

// Signs a small PDF, verifies the signature and reads the text back out: the three entry points a
// consumer of M3scalinum.Text.Pdf needs, exercised on bytes the example builds itself.

const string Marker = "M3SCALINUM PDF SAMPLE";

var pdf = BuildPdf();
var extracted = PdfText.Extract(pdf);
var text = extracted.Text;

Report("read", $"pages={extracted.PageCount} chars={text.Length}");

if (!text.Contains(Marker, StringComparison.Ordinal))
{
    throw new InvalidOperationException($"the extracted text does not hold the marker: '{text.Trim()}'");
}

// A self-signed certificate stands in for the signer's: the point of the example is the PDF
// structure, and a deployment loads its own from a .pfx or a key store.
using var key = RSA.Create(2048);
var request = new CertificateRequest(
    "CN=M3scalinum.Text.Pdf Sample, O=M3scalinum.Text.Pdf, C=FR",
    key,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1);

request.CertificateExtensions.Add(
    new X509KeyUsageExtension(
        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));

using var created = request.CreateSelfSigned(
    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));

using var certificate = X509CertificateLoader.LoadPkcs12(
    created.Export(X509ContentType.Pkcs12),
    password: null,
    X509KeyStorageFlags.Exportable);

var signed = PdfSigner.Sign(pdf, certificate, new PdfSignatureOptions
{
    FieldName = "Signature1",
    Reason = "Example",
    Location = "Paris",
    SignedAt = new DateTimeOffset(2026, 2, 1, 10, 30, 0, TimeSpan.Zero),
});

Report("sign", $"{pdf.Length} bytes in, {signed.Length} bytes out");

var verification = PdfSignatureVerifier.Verify(signed);

if (!verification.IsSigned || !verification.AllIntact)
{
    throw new InvalidOperationException(
        $"the signature does not verify: signed={verification.IsSigned} intact={verification.AllIntact}");
}

var signature = verification.Signatures[0];

Report("verify",
    $"signer='{signature.SignerSubject}' level={signature.Level} chain={signature.ChainStatus}");

// The signed document is still a document: the appended revision must not have cost the text.
var signedText = PdfText.Extract(signed).Text;

if (!signedText.Contains(Marker, StringComparison.Ordinal))
{
    throw new InvalidOperationException("the text is no longer readable after signing");
}

Report("read signed", $"{signedText.Length} chars, marker present");
Console.WriteLine();
Console.WriteLine("M3scalinum.Text.Pdf: read, sign and verify all ran on bytes the package alone produced.");

static void Report(string step, string detail) => Console.WriteLine($"  {step,-11} {detail}");

// A one-page PDF with a Helvetica run, written by hand so the example depends on nothing.
static byte[] BuildPdf()
{
    const string Content = "BT /F1 24 Tf 72 700 Td (M3SCALINUM PDF SAMPLE) Tj ET";

    string[] bodies =
    [
        "<</Type/Catalog/Pages 2 0 R>>",
        "<</Type/Pages/Count 1/Kids[3 0 R]>>",
        "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Resources<</Font<</F1 4 0 R>>>>/Contents 5 0 R>>",
        "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>",
        $"<< /Length {Content.Length} >>\nstream\n{Content}\nendstream",
    ];

    var text = new StringBuilder("%PDF-1.7\n");
    var offsets = new List<int>();

    for (var index = 0; index < bodies.Length; index++)
    {
        offsets.Add(text.Length);
        text.Append((index + 1).ToString(CultureInfo.InvariantCulture))
            .Append(" 0 obj\n")
            .Append(bodies[index])
            .Append("\nendobj\n");
    }

    var xref = text.Length;
    text.Append("xref\n0 ").Append((bodies.Length + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
    text.Append("0000000000 65535 f \r\n");

    foreach (var offset in offsets)
    {
        text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \r\n");
    }

    text.Append("trailer\n<</Size ").Append((bodies.Length + 1).ToString(CultureInfo.InvariantCulture))
        .Append("/Root 1 0 R>>\nstartxref\n")
        .Append(xref.ToString(CultureInfo.InvariantCulture))
        .Append("\n%%EOF\n");

    return Encoding.Latin1.GetBytes(text.ToString());
}
