using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Encryption, both directions, and the refusal every other operation owes a document it cannot
/// read.
/// </summary>
/// <remarks>
/// The round trip is asserted on a real document read back, not on the bytes being different: a
/// rewriter that encrypts everything and decrypts nothing also produces different bytes. The
/// AES-128 case is a second implementation of the specification (see <see cref="TestEncryption"/>),
/// and the AES-256 case a file qpdf wrote, so neither direction is only checked against itself.
/// </remarks>
public sealed class PdfEncryptionTests
{
    private const string Marker = "Rapport";

    /// <summary>A two-page document the compiler produced, with subset fonts and /ToUnicode maps.</summary>
    private static byte[] Ua1()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", "ua1.pdf");

        Assert.True(File.Exists(path), $"the fixture ua1.pdf is missing from {path}");

        return File.ReadAllBytes(path);
    }

    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "encryption", name);

        Assert.True(File.Exists(path), $"the fixture {name} is missing from {path}");

        return File.ReadAllBytes(path);
    }

    private static string Text(byte[] pdf) => PdfTextProbe.Page(pdf, 0);

    [Fact]
    public void Encrypting_then_opening_with_the_user_password_keeps_the_pages_and_the_text()
    {
        var encrypted = PdfEncryption.Encrypt(Ua1(), "correct horse", PdfPermissions.All);
        var opened = PdfEncryption.Open(encrypted, "correct horse");

        Assert.True(opened.IsEncrypted);
        Assert.Equal(6, opened.Revision);
        Assert.False(opened.OpenedWithOwnerPassword);

        Assert.True(PdfFile.TryOpen(opened.Pdf, out var file), "the decrypted bytes were not read as a PDF");
        Assert.Empty(file!.Problems);
        Assert.False(file.IsEncrypted);
        Assert.Equal(2, file.Pages().Count);
        Assert.Contains(Marker, Text(opened.Pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void The_encrypted_bytes_do_not_carry_the_text_in_the_clear()
    {
        var plain = Ua1();
        var encrypted = PdfEncryption.Encrypt(plain, "secret", PdfPermissions.All);

        var text = Encoding.Latin1.GetString(encrypted);
        Assert.DoesNotContain(Marker, text, StringComparison.Ordinal);
        Assert.Contains("/Encrypt", text, StringComparison.Ordinal);
        Assert.Contains("/AESV3", text, StringComparison.Ordinal);

        // The encrypted file still parses and still knows it is encrypted.
        Assert.True(PdfFile.TryOpen(encrypted, out var file));
        Assert.True(file!.IsEncrypted);
        Assert.Equal(2, file.Pages().Count);
    }

    [Fact]
    public void A_wrong_password_is_a_typed_error_and_not_a_corrupt_document()
    {
        var encrypted = PdfEncryption.Encrypt(Ua1(), "right", PdfPermissions.All);

        var wrong = Assert.Throws<PdfEncryptionException>(() => PdfEncryption.Open(encrypted, "wrong"));
        Assert.Contains("password", wrong.Message, StringComparison.Ordinal);

        Assert.Throws<PdfEncryptionException>(() => PdfEncryption.Open(encrypted));
    }

    [Fact]
    public void An_empty_password_opens_without_one()
    {
        var encrypted = PdfEncryption.Encrypt(Ua1(), string.Empty, PdfPermissions.Print);
        var opened = PdfEncryption.Open(encrypted);

        Assert.Equal(PdfPermissions.Print, opened.Permissions);
        Assert.Contains(Marker, Text(opened.Pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void The_permission_bits_read_back_and_the_owner_password_is_recognised()
    {
        var permissions = PdfPermissions.Print | PdfPermissions.Copy;
        var encrypted = PdfEncryption.Encrypt(Ua1(), "reader", permissions, "manager");

        var user = PdfEncryption.Open(encrypted, "reader");
        Assert.Equal(permissions, user.Permissions);
        Assert.False(user.OpenedWithOwnerPassword);

        var owner = PdfEncryption.Open(encrypted, "manager");
        Assert.Equal(permissions, owner.Permissions);
        Assert.True(owner.OpenedWithOwnerPassword);
        Assert.Contains(Marker, Text(owner.Pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void The_owner_password_defaults_to_the_user_password()
    {
        var encrypted = PdfEncryption.Encrypt(Ua1(), "same", PdfPermissions.None);

        var opened = PdfEncryption.Open(encrypted, "same");

        // The two roles are one password here, so the user check answers first; what matters is that
        // the document opens and the permissions it declared came back.
        Assert.Equal(PdfPermissions.None, opened.Permissions);
        Assert.Contains(Marker, Text(opened.Pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_a_document_that_is_not_encrypted_returns_it_unchanged()
    {
        var plain = Ua1();
        var opened = PdfEncryption.Open(plain);

        Assert.False(opened.IsEncrypted);
        Assert.Equal(0, opened.Revision);
        Assert.Equal(PdfPermissions.All, opened.Permissions);
        Assert.Equal(plain, opened.Pdf);
    }

    [Fact]
    public void Encrypting_an_encrypted_document_is_refused()
    {
        var encrypted = PdfEncryption.Encrypt(Ua1(), "one", PdfPermissions.All);

        var again = Assert.Throws<PdfEncryptionException>(
            () => PdfEncryption.Encrypt(encrypted, "two", PdfPermissions.All));

        Assert.Contains("already encrypted", again.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_encrypted_file_below_PDF_2_0_declares_the_extension_that_defines_AES_256()
    {
        // The header of the source is kept, so the catalog has to name the Adobe extension level
        // that makes /AESV3 legal in a 1.7 file, as qpdf does.
        var encrypted = PdfEncryption.Encrypt(TestPdf.OnePage(), "x", PdfPermissions.All);
        var text = Encoding.Latin1.GetString(encrypted);

        Assert.StartsWith("%PDF-1.7", text, StringComparison.Ordinal);
        Assert.Contains("/ExtensionLevel 8", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Page 1", text, StringComparison.Ordinal);

        var opened = PdfEncryption.Open(encrypted, "x");
        Assert.Contains("Page 1", Encoding.Latin1.GetString(opened.Pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void A_permission_set_with_bits_the_format_does_not_define_is_refused()
    {
        var refusal = Assert.Throws<ArgumentException>(
            () => PdfEncryption.Encrypt(TestPdf.OnePage(), "x", (PdfPermissions)0x4000));

        Assert.Contains("permissions", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_that_are_not_a_PDF_are_refused_by_both_directions()
    {
        var bytes = Encoding.ASCII.GetBytes("not a pdf");

        Assert.Throws<PdfEncryptionException>(() => PdfEncryption.Open(bytes, "x"));
        Assert.Throws<PdfEncryptionException>(() => PdfEncryption.Encrypt(bytes, "x", PdfPermissions.All));
    }

    [Fact]
    public void Public_key_encryption_is_refused_by_the_handler_it_names()
    {
        // The certificate handlers wrap the file key with /Adobe.PubSec rather than a password.
        var certificate = PdfTestDocument.Document(
            [
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Count 1/Kids[3 0 R]>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            ],
            "/Encrypt<</Filter/Adobe.PubSec/SubFilter/adbe.pkcs7.s5/V 4/R 4/O<00>/U<00>/P -1>>");

        var refusal = Assert.Throws<PdfEncryptionException>(() => PdfEncryption.Open(certificate, string.Empty));

        Assert.Contains("Adobe.PubSec", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("public-key", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_security_handler_revision_the_library_does_not_implement_is_refused_by_name()
    {
        // Revision 2 is the 40-bit RC4 handler of PDF 1.1: readable in principle, out of scope here.
        var legacy = PdfTestDocument.Document(
            [
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Count 1/Kids[3 0 R]>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            ],
            "/Encrypt<</Filter/Standard/V 1/R 2/O<00>/U<00>/P -1>>");

        var refusal = Assert.Throws<PdfEncryptionException>(() => PdfEncryption.Open(legacy, string.Empty));

        Assert.Contains("V=1/R=2", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("AES-256", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_revision_4_document_with_an_RC4_crypt_filter_is_refused()
    {
        var zeros = new string('0', 64);
        var user = new string('0', 32);

        var rc4 = PdfTestDocument.Document(
            [
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Count 1/Kids[3 0 R]>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]>>",
            ],
            "/Encrypt<</Filter/Standard/V 4/R 4/Length 128/CF<</StdCF<</CFM/V2/Length 16>>>>"
                + "/StmF/StdCF/StrF/StdCF/O<" + zeros + ">/U<" + user + ">/P -1>>");

        var refusal = Assert.Throws<PdfEncryptionException>(() => PdfEncryption.Open(rc4, "x"));

        Assert.Contains("/V2", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("RC4", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_AES_128_document_written_from_the_specification_is_read_with_both_passwords()
    {
        var fixture = TestEncryption.Aes128();

        var user = PdfEncryption.Open(fixture, TestEncryption.UserPassword);
        Assert.Equal(4, user.Revision);
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Copy, user.Permissions);
        Assert.False(user.OpenedWithOwnerPassword);
        Assert.Contains(TestEncryption.ContentText, Text(user.Pdf), StringComparison.Ordinal);

        var owner = PdfEncryption.Open(fixture, TestEncryption.OwnerPassword);
        Assert.True(owner.OpenedWithOwnerPassword);
        Assert.Contains(TestEncryption.ContentText, Text(owner.Pdf), StringComparison.Ordinal);

        // Strings are encrypted in R4 too, and the information dictionary is where that shows.
        Assert.True(PdfFile.TryOpen(user.Pdf, out var decrypted));
        var info = decrypted!.ResolveDictionary(decrypted.Trailer["Info"]);
        var title = info?["Title"] as PdfString;
        Assert.Equal(TestEncryption.Title, title?.Decode());

        var wrong = Assert.Throws<PdfEncryptionException>(
            () => PdfEncryption.Open(fixture, "not-the-password"));
        Assert.Contains("password", wrong.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_encrypted_by_another_producer_is_read()
    {
        // Written by qpdf with 256-bit AES and the user password "asdfzxcv"; see the fixture README.
        var opened = PdfEncryption.Open(Fixture("qpdf-r6-user-password.pdf"), "asdfzxcv");

        Assert.True(opened.IsEncrypted);
        Assert.Equal(6, opened.Revision);

        Assert.True(PdfFile.TryOpen(opened.Pdf, out var file), "the decrypted bytes were not read as a PDF");
        Assert.Single(file!.Pages());

        // The page draws its password hint, which is a marker no unencrypted read could produce.
        var text = Text(opened.Pdf);
        Assert.Contains("pdfencryptiontestpassword:asdfzxcv", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_and_form_operations_refuse_an_encrypted_document_and_point_at_Open()
    {
        var encrypted = PdfEncryption.Encrypt(Ua1(), "x", PdfPermissions.All);

        var extract = Assert.Throws<PdfOperationException>(() => PdfPages.Extract(encrypted, "1"));
        Assert.Contains("PdfEncryption.Open", extract.Message, StringComparison.Ordinal);

        Assert.Throws<PdfOperationException>(() => PdfPages.Merge(encrypted));
        Assert.Throws<PdfOperationException>(() => PdfPages.Rotate(encrypted, 90));
        Assert.Throws<PdfOperationException>(
            () => PdfForm.Fill(encrypted, new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.Throws<PdfOperationException>(() => PdfForm.Flatten(encrypted));
    }

    [Fact]
    public void Signing_and_verifying_refuse_an_encrypted_document()
    {
        var encrypted = PdfEncryption.Encrypt(Ua1(), "x", PdfPermissions.All);
        using var certificate = TestCertificates.Signer();

        var refusal = Assert.Throws<PdfSignatureException>(() => PdfSigner.Sign(encrypted, certificate));
        Assert.Contains("encrypted", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("PdfEncryption.Open", refusal.Message, StringComparison.Ordinal);

        Assert.Throws<PdfSignatureException>(() => PdfSignatureVerifier.Verify(encrypted));
    }

    [Fact]
    public void The_conformance_checker_reports_an_encrypted_document_and_clears_it_once_decrypted()
    {
        var encrypted = PdfEncryption.Encrypt(Ua1(), "x", PdfPermissions.All);

        var report = PdfConformance.Check(encrypted, PdfConformanceStandard.PdfUa1);
        Assert.True(report.Has(PdfConformanceCodes.Encrypted));

        var opened = PdfEncryption.Open(encrypted, "x");
        var decrypted = PdfConformance.Check(opened.Pdf, PdfConformanceStandard.PdfUa1);

        Assert.False(decrypted.Has(PdfConformanceCodes.Encrypted));
        Assert.False(decrypted.Has(PdfConformanceCodes.NotAPdf));
        Assert.False(decrypted.Has(PdfConformanceCodes.XrefInvalid));
    }

    [Theory]
    [InlineData("objstm.pdf")]
    [InlineData("xref-stream.pdf")]
    public void A_document_that_stores_its_objects_in_a_stream_survives_encryption_and_decryption(string name)
    {
        // The output of both directions has a classic table and no object stream: the members of an
        // object stream are written as objects of their own, and the xref stream that described the
        // old offsets is dropped rather than carried into a file it no longer describes.
        var plain = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", name));

        Assert.True(PdfFile.TryOpen(plain, out var before), $"the fixture {name} was not read as a PDF");
        Assert.Single(before!.Pages());

        var opened = PdfEncryption.Open(PdfEncryption.Encrypt(plain, "objects", PdfPermissions.All), "objects");

        Assert.True(PdfFile.TryOpen(opened.Pdf, out var after), "the decrypted bytes were not read as a PDF");
        Assert.Empty(after!.Problems);
        Assert.Single(after.Pages());

        var text = Encoding.Latin1.GetString(opened.Pdf);
        Assert.DoesNotContain("/ObjStm", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/Type/XRef", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decrypted_document_can_be_merged_and_signed()
    {
        var encrypted = PdfEncryption.Encrypt(Ua1(), "x", PdfPermissions.All);
        var opened = PdfEncryption.Open(encrypted, "x");

        var merged = PdfPages.Merge(opened.Pdf, opened.Pdf);
        Assert.True(PdfFile.TryOpen(merged, out var file));
        Assert.Equal(4, file!.Pages().Count);
        Assert.Contains(Marker, PdfTextProbe.Page(merged, 2), StringComparison.Ordinal);

        using var certificate = TestCertificates.Signer();
        var signed = PdfSigner.Sign(opened.Pdf, certificate);
        Assert.NotEmpty(signed);
    }
}
