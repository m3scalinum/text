using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using M3scalinum.Text.SharpTypst.Compilation;
using M3scalinum.Text.Pdf;

// Compiles a document, signs it with PAdES, and writes both files so a validator can check them.

var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "samples");
Directory.CreateDirectory(outputDirectory);

const string Source = """
    = Facture signée

    Une facture qui porte une signature PAdES, produite par M3scalinum.Text.SharpTypst.

    #table(
      columns: 3,
      [Désignation], [Qté], [Total],
      [Prestation], [1], [1 200,00 €],
    )
    """;

Console.WriteLine("M3scalinum.Text.Pdf");
Console.WriteLine();

// A path signs an existing document; no argument compiles the sample one.
byte[] pdf;

if (args.Length > 0 && File.Exists(args[0]))
{
    pdf = File.ReadAllBytes(args[0]);
}
else
{
    using var world = new TypstWorldBuilder().AddSource("main.typ", Source).Build();
    using var compiled = new TypstCompiler().Compile(world);
    pdf = compiled.ExportPdf();
}

var unsignedPath = Path.Combine(outputDirectory, "signed-unsigned.pdf");
File.WriteAllBytes(unsignedPath, pdf);
Console.WriteLine($"  wrote {Path.GetFileName(unsignedPath)} ({pdf.Length} bytes)");

// A self-signed certificate stands in for the signer's: the point of the sample is the PDF
// structure, and a real deployment loads its own from a .pfx or a key store.
using var key = RSA.Create(2048);
var request = new CertificateRequest(
    "CN=M3scalinum.Text.SharpTypst Sample, O=M3scalinum.Text.SharpTypst, C=FR",
    key,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1);

request.CertificateExtensions.Add(
    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));

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
    Reason = "Facture émise",
    Location = "Paris",
    SignedAt = new DateTimeOffset(2026, 2, 1, 10, 30, 0, TimeSpan.Zero),
});

var signedPath = Path.Combine(outputDirectory, "signed.pdf");
File.WriteAllBytes(signedPath, signed);
Console.WriteLine($"  wrote {Path.GetFileName(signedPath)} ({signed.Length} bytes)");

// The certificate, so a validator can build the trust anchor.
var certificatePath = Path.Combine(outputDirectory, "signed-certificate.der");
File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Cert));
Console.WriteLine($"  wrote {Path.GetFileName(certificatePath)}");
