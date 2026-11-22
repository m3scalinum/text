using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Embedded files: the name tree, the association array, and the incremental update that changes
/// them. The fixtures are written by hand so the bytes are visible: a test that failed here can say
/// whether it was the name, the filter or the update that went wrong.
/// </summary>
public sealed class PdfAttachmentTests
{
    private const string InvoiceXml = "<?xml version=\"1.0\"?><Invoice>F-2026-0042</Invoice>";

    private static byte[] Xml() => Encoding.UTF8.GetBytes(InvoiceXml);

    [Fact]
    public void A_document_without_an_attachment_has_an_empty_collection()
    {
        Assert.Empty(PdfAttachments.Read(PdfTestDocument.Minimal()));
        Assert.Empty(PdfAttachments.Read(TestPdf.OnePage()));
    }

    [Fact]
    public void An_uncompressed_attachment_reports_its_name_type_size_and_bytes()
    {
        var pdf = WithAttachments(("invoice.xml", Xml(), false));

        var attachment = Assert.Single(PdfAttachments.Read(pdf));

        Assert.Equal("invoice.xml", attachment.Name);
        Assert.Equal("invoice.xml", attachment.FileName);
        Assert.True(attachment.HasFileName);
        Assert.Equal("text/xml", attachment.MimeType);
        Assert.Equal(PdfAttachmentRole.Data, attachment.Role);
        Assert.Equal(Xml().Length, attachment.Size);
        Assert.Equal(Xml(), attachment.Content);
        Assert.False(attachment.IsStoredEncoded);
    }

    [Fact]
    public void A_flate_compressed_attachment_comes_back_as_the_file_it_was()
    {
        var content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(InvoiceXml, 40)));
        var pdf = WithAttachments(("invoice.xml", content, true));

        Assert.Contains("/FlateDecode", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);

        var attachment = Assert.Single(PdfAttachments.Read(pdf));

        Assert.False(attachment.IsStoredEncoded);
        Assert.Equal(content.Length, attachment.Size);
        Assert.Equal(content, attachment.Content);
    }

    [Fact]
    public void The_sha256_is_the_hash_of_exactly_the_attachment_bytes()
    {
        var pdf = WithAttachments(("invoice.xml", Xml(), false));

        var attachment = Assert.Single(PdfAttachments.Read(pdf));

        // SHA-256 of "abc", the one vector everybody remembers.
        var abc = WithAttachments(("abc.txt", Encoding.ASCII.GetBytes("abc"), false));
        var hashed = Assert.Single(PdfAttachments.Read(abc));

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hashed.Sha256);
        Assert.Equal(64, attachment.Sha256.Length);
        Assert.Equal(attachment.Sha256, Assert.Single(PdfAttachments.Read(pdf)).Sha256);
    }

    [Fact]
    public void Several_attachments_keep_the_order_of_the_name_tree()
    {
        var pdf = WithAttachments(
            ("zeta.txt", Encoding.ASCII.GetBytes("z"), false),
            ("alpha.txt", Encoding.ASCII.GetBytes("a"), false));

        var names = PdfAttachments.Read(pdf).Select(attachment => attachment.Name);

        Assert.Equal(new[] { "zeta.txt", "alpha.txt" }, names);
    }

    [Fact]
    public void A_name_tree_with_intermediate_nodes_is_walked_to_its_leaves()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 5 0 R>>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/EF<</F 3 0 R>>>>",
            "<</Kids[6 0 R]>>",
            "<</Names[(invoice.xml) 4 0 R]>>",
        ]);

        var attachment = Assert.Single(PdfAttachments.Read(pdf));

        Assert.Equal("invoice.xml", attachment.Name);
        Assert.Equal(Xml(), attachment.Content);
    }

    [Fact]
    public void The_unicode_name_is_preferred_and_the_raw_F_entry_still_reported()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 5 0 R>>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/UF" + UnicodeHex("Facturé.xml")
                + "/AFRelationship/Alternative/EF<</F 3 0 R>>>>",
            "<</Names[(invoice.xml) 4 0 R]>>",
        ]);

        var attachment = Assert.Single(PdfAttachments.Read(pdf));

        Assert.Equal("Facturé.xml", attachment.Name);
        Assert.Equal("invoice.xml", attachment.FileName);
        Assert.Equal(PdfAttachmentRole.Alternative, attachment.Role);
    }

    [Fact]
    public void A_file_listed_only_in_the_association_array_is_still_an_attachment()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/AF[4 0 R]>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/UF" + UnicodeHex("invoice.xml")
                + "/AFRelationship/Supplement/EF<</F 3 0 R>>/Subtype/text#2Fxml>>",
        ]);

        var attachment = Assert.Single(PdfAttachments.Read(pdf));

        Assert.Equal("invoice.xml", attachment.Name);
        Assert.Equal(PdfAttachmentRole.Supplement, attachment.Role);
        Assert.Equal(Xml(), attachment.Content);
    }

    [Fact]
    public void A_file_in_both_the_name_tree_and_the_association_array_appears_once()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 5 0 R>>/AF[4 0 R]>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/EF<</F 3 0 R>>>>",
            "<</Names[(invoice.xml) 4 0 R]>>",
        ]);

        Assert.Single(PdfAttachments.Read(pdf));
    }

    [Fact]
    public void An_attachment_whose_filter_the_reader_cannot_undo_is_reported_as_stored()
    {
        var stored = Encoding.ASCII.GetBytes("\u0080stored-bytes");

        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 5 0 R>>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            "<</Type/EmbeddedFile/Length " + stored.Length.ToString(CultureInfo.InvariantCulture)
                + "/Filter/LZWDecode>>\nstream\n" + Encoding.Latin1.GetString(stored) + "\nendstream",
            "<</Type/Filespec/F(odd.bin)/EF<</F 3 0 R>>>>",
            "<</Names[(odd.bin) 4 0 R]>>",
        ]);

        var attachment = Assert.Single(PdfAttachments.Read(pdf));

        Assert.True(attachment.IsStoredEncoded);
        Assert.Equal(stored, attachment.Content);
    }

    [Fact]
    public void Extracting_returns_the_bytes_the_sender_embedded()
    {
        var content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(InvoiceXml, 10)));
        var pdf = WithAttachments(("invoice.xml", content, true));

        var extracted = PdfAttachments.Extract(pdf, "invoice.xml");

        Assert.Equal(content, extracted);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(extracted)).ToLowerInvariant(),
            Assert.Single(PdfAttachments.Read(pdf)).Sha256);
    }

    [Fact]
    public void Extracting_a_name_the_document_does_not_carry_is_refused()
    {
        var exception = Assert.Throws<PdfOperationException>(
            () => PdfAttachments.Extract(TestPdf.OnePage(), "missing.xml"));

        Assert.Contains("missing.xml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_an_attachment_appends_an_update_that_reads_back()
    {
        var original = TestPdf.OnePage();

        var updated = PdfAttachments.Add(
            original,
            "factur-x.xml",
            Xml(),
            "text/xml",
            PdfAttachmentRole.Data,
            "Invoice data");

        // An incremental update: every byte of the original is still there, unchanged.
        Assert.True(updated.AsSpan(0, original.Length).SequenceEqual(original));

        var attachment = Assert.Single(PdfAttachments.Read(updated));
        Assert.Equal("factur-x.xml", attachment.Name);
        Assert.Equal("text/xml", attachment.MimeType);
        Assert.Equal(PdfAttachmentRole.Data, attachment.Role);
        Assert.Equal(Xml(), attachment.Content);
        Assert.Equal(Xml().Length, attachment.Size);

        Assert.Single(PdfTextProbe.Pages(updated));
    }

    [Fact]
    public void An_added_attachment_is_compressed_and_declares_no_relationship_when_none_is_asked_for()
    {
        var updated = PdfAttachments.Add(TestPdf.OnePage(), "notes.bin", Encoding.ASCII.GetBytes("plain note"));

        var text = Encoding.Latin1.GetString(updated);
        Assert.Contains("/EmbeddedFile", text, StringComparison.Ordinal);
        Assert.Contains("/FlateDecode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(plain note)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/AFRelationship", text, StringComparison.Ordinal);

        var attachment = Assert.Single(PdfAttachments.Read(updated));
        Assert.Equal(PdfAttachmentRole.Unspecified, attachment.Role);
        Assert.Equal("application/octet-stream", attachment.MimeType);
        Assert.Equal(Encoding.ASCII.GetBytes("plain note"), attachment.Content);
    }

    [Theory]
    [InlineData("data.csv", "text/csv")]
    [InlineData("payload.json", "application/json")]
    [InlineData("scan.PDF", "application/pdf")]
    [InlineData("invoice.xml", "text/xml")]
    [InlineData("readme.txt", "text/plain")]
    [InlineData("page.html", "text/html")]
    [InlineData("logo.png", "image/png")]
    [InlineData("photo.JPEG", "image/jpeg")]
    [InlineData("bundle.zip", "application/zip")]
    [InlineData("sheet.unknown", "application/octet-stream")]
    public void The_mime_type_is_guessed_from_the_extension_when_the_caller_names_none(string name, string expected)
    {
        var updated = PdfAttachments.Add(TestPdf.OnePage(), name, Encoding.ASCII.GetBytes("x"));

        Assert.Equal(expected, Assert.Single(PdfAttachments.Read(updated)).MimeType);
    }

    [Fact]
    public void An_indirect_name_dictionary_is_updated_in_place_of_the_catalog()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names 5 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/EF<</F 3 0 R>>>>",
            "<</EmbeddedFiles 6 0 R>>",
            "<</Names[(invoice.xml) 4 0 R]>>",
        ]);

        var updated = PdfAttachments.Add(pdf, "second.xml", Xml(), "text/xml");
        var names = PdfAttachments.Read(updated).Select(attachment => attachment.Name).OrderBy(name => name);

        Assert.Equal(new[] { "invoice.xml", "second.xml" }, names);

        var removed = PdfAttachments.Remove(updated, "invoice.xml");
        Assert.Equal("second.xml", Assert.Single(PdfAttachments.Read(removed)).Name);
    }

    [Fact]
    public void A_name_dictionary_that_carries_other_trees_keeps_them_when_an_attachment_is_added()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</Dests 3 0 R>>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            "<</Dests[]>>",
        ]);

        var updated = PdfAttachments.Add(pdf, "invoice.xml", Xml(), "text/xml");
        var text = Encoding.Latin1.GetString(updated);

        Assert.Single(PdfAttachments.Read(updated));
        Assert.Contains("/Dests 3 0 R", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_names_entry_that_is_not_a_dictionary_is_refused()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names/Foo>>",
            "<</Type/Pages/Count 0/Kids[]>>",
        ]);

        var exception = Assert.Throws<PdfOperationException>(
            () => PdfAttachments.Add(pdf, "invoice.xml", Xml(), "text/xml"));

        Assert.Contains("/Names", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_whose_name_dictionary_holds_no_embedded_files_reads_as_empty()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</Dests 3 0 R>>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            "<</Dests[]>>",
        ]);

        Assert.Empty(PdfAttachments.Read(pdf));
    }

    [Fact]
    public void A_name_tree_key_written_as_a_name_is_read_as_the_attachment_name()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 5 0 R>>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/EF<</F 3 0 R>>>>",
            "<</Names[/invoice.xml 4 0 R]>>",
        ]);

        Assert.Equal("invoice.xml", Assert.Single(PdfAttachments.Read(pdf)).Name);
    }

    [Fact]
    public void A_source_relationship_is_reported()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/AF[4 0 R]>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/AFRelationship/Source/EF<</F 3 0 R>>>>",
        ]);

        Assert.Equal(PdfAttachmentRole.Source, Assert.Single(PdfAttachments.Read(pdf)).Role);
    }

    [Fact]
    public void An_association_array_that_lists_something_which_is_not_an_attachment_is_skipped()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/AF[3 0 R 4 0 R]>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            "/JustAName",
            "<</Type/Filespec/F(elsewhere.xml)>>",
        ]);

        Assert.Empty(PdfAttachments.Read(pdf));
    }

    [Fact]
    public void Two_file_specifications_of_the_same_name_are_reported_once()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 6 0 R>>/AF[4 0 R 5 0 R]>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/EF<</F 3 0 R>>>>",
            "<</Type/Filespec/F(invoice.xml)/EF<</F 3 0 R>>>>",
            "<</Names[(invoice.xml) 4 0 R]>>",
        ]);

        Assert.Single(PdfAttachments.Read(pdf));
    }

    [Fact]
    public void A_single_file_specification_outside_an_array_is_associated_and_removed()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/AF 4 0 R>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/EF<</F 3 0 R>>>>",
        ]);

        var added = PdfAttachments.Add(pdf, "second.xml", Xml(), "text/xml");
        Assert.Equal(2, PdfAttachments.Read(added).Count);

        var removed = PdfAttachments.Remove(pdf, "invoice.xml");
        Assert.Empty(PdfAttachments.Read(removed));
    }

    [Fact]
    public void Removing_one_entry_from_a_shared_association_array_keeps_the_other()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 6 0 R>>/AF[ 4 0 R 5 0 R ]>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/AFRelationship/Data/EF<</F 3 0 R>>>>",
            "<</Type/Filespec/F(notes.txt)/AFRelationship/Supplement/EF<</F 3 0 R>>>>",
            "<</Names[(invoice.xml) 4 0 R (notes.txt) 5 0 R]>>",
        ]);

        var updated = PdfAttachments.Remove(pdf, "invoice.xml");
        var attachment = Assert.Single(PdfAttachments.Read(updated));

        Assert.Equal("notes.txt", attachment.Name);
        Assert.Equal(PdfAttachmentRole.Supplement, attachment.Role);
    }

    [Fact]
    public void An_inline_file_specification_keeps_every_value_it_carries_across_an_update()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 4 0 R>>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Names[(odd.bin) <</Type/Filespec/F(odd.bin)/EF<</F 3 0 R>>"
                + "/X-Custom[1 2]/X-Flag true/X-Count 3/X-Nothing null>>]>>",
        ]);

        var updated = PdfAttachments.Add(pdf, "second.xml", Xml(), "text/xml");
        var text = Encoding.Latin1.GetString(updated);

        Assert.Equal(2, PdfAttachments.Read(updated).Count);
        Assert.Contains("/X-Custom[1 2]", text, StringComparison.Ordinal);
        Assert.Contains("/X-Flag true", text, StringComparison.Ordinal);
        Assert.Equal(Xml(), PdfAttachments.Extract(updated, "odd.bin"));
    }

    [Fact]
    public void Adding_a_second_attachment_keeps_the_first()
    {
        var first = WithAttachments(("invoice.xml", Xml(), true));

        var updated = PdfAttachments.Add(first, "factur-x.xml", Xml(), "text/xml", PdfAttachmentRole.Data);

        var names = PdfAttachments.Read(updated).Select(attachment => attachment.Name).OrderBy(name => name);

        Assert.Equal(new[] { "factur-x.xml", "invoice.xml" }, names);
    }

    [Fact]
    public void Adding_to_a_document_that_already_has_an_association_array_extends_it()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/AF[4 0 R]>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/AFRelationship/Data/EF<</F 3 0 R>>>>",
        ]);

        var updated = PdfAttachments.Add(pdf, "second.xml", Xml(), "text/xml", PdfAttachmentRole.Data);
        var attachments = PdfAttachments.Read(updated);

        Assert.Equal(2, attachments.Count);
        Assert.Contains(attachments, attachment => attachment.Name == "invoice.xml");
        Assert.Contains(attachments, attachment => attachment.Name == "second.xml");
    }

    [Fact]
    public void Adding_to_a_document_whose_name_tree_lists_an_inline_file_specification_keeps_it()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles 4 0 R>>>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Names[(invoice.xml) <</Type/Filespec/F(invoice.xml)/EF<</F 3 0 R>>>>]>>",
        ]);

        var updated = PdfAttachments.Add(pdf, "second.xml", Xml(), "text/xml");
        var names = PdfAttachments.Read(updated).Select(attachment => attachment.Name).OrderBy(name => name);

        Assert.Equal(new[] { "invoice.xml", "second.xml" }, names);
        Assert.Equal(Xml(), PdfAttachments.Extract(updated, "invoice.xml"));
    }

    [Fact]
    public void Adding_a_name_the_document_already_carries_is_refused()
    {
        var pdf = WithAttachments(("invoice.xml", Xml(), false));

        var exception = Assert.Throws<PdfOperationException>(
            () => PdfAttachments.Add(pdf, "invoice.xml", Xml(), "text/xml"));

        Assert.Contains("invoice.xml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_one_attachment_leaves_the_others()
    {
        var pdf = WithAttachments(
            ("invoice.xml", Xml(), true),
            ("notes.txt", Encoding.ASCII.GetBytes("note"), false));

        var updated = PdfAttachments.Remove(pdf, "invoice.xml");
        var attachment = Assert.Single(PdfAttachments.Read(updated));

        Assert.Equal("notes.txt", attachment.Name);
        Assert.Equal(Encoding.ASCII.GetBytes("note"), attachment.Content);
        Assert.Throws<PdfOperationException>(() => PdfAttachments.Extract(updated, "invoice.xml"));
        Assert.True(updated.AsSpan(0, pdf.Length).SequenceEqual(pdf));
    }

    [Fact]
    public void Removing_the_last_attachment_leaves_a_document_that_carries_none()
    {
        var pdf = WithAttachments(("invoice.xml", Xml(), true));

        var updated = PdfAttachments.Remove(pdf, "invoice.xml");

        Assert.Empty(PdfAttachments.Read(updated));
        Assert.DoesNotContain("/AF[", Encoding.Latin1.GetString(updated), StringComparison.Ordinal);
        Assert.Empty(PdfTextProbe.Pages(updated));
    }

    [Fact]
    public void Removing_an_attachment_listed_only_in_the_association_array_works()
    {
        var pdf = PdfTestDocument.Document(
        [
            "<</Type/Catalog/Pages 2 0 R/AF[4 0 R]>>",
            "<</Type/Pages/Count 0/Kids[]>>",
            StreamBody(Xml(), compress: false),
            "<</Type/Filespec/F(invoice.xml)/AFRelationship/Data/EF<</F 3 0 R>>>>",
        ]);

        var updated = PdfAttachments.Remove(pdf, "invoice.xml");

        Assert.Empty(PdfAttachments.Read(updated));
    }

    [Fact]
    public void Removing_a_name_the_document_does_not_carry_is_refused()
    {
        var exception = Assert.Throws<PdfOperationException>(
            () => PdfAttachments.Remove(TestPdf.OnePage(), "missing.xml"));

        Assert.Contains("missing.xml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_encrypted_document_is_refused_rather_than_read_or_rewritten()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "encryption", "qpdf-r6-user-password.pdf");
        Assert.True(File.Exists(path), $"the fixture is missing from {path}");
        var encrypted = File.ReadAllBytes(path);

        Assert.Throws<PdfOperationException>(() => PdfAttachments.Read(encrypted));
        Assert.Throws<PdfOperationException>(
            () => PdfAttachments.Add(encrypted, "invoice.xml", Xml(), "text/xml"));
    }

    [Fact]
    public void A_document_whose_cross_reference_is_a_stream_cannot_take_an_update()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", "objstm.pdf");
        Assert.True(File.Exists(path), $"the fixture is missing from {path}");
        var compact = File.ReadAllBytes(path);

        // Reading still works: the reader follows the cross-reference stream.
        Assert.Empty(PdfAttachments.Read(compact));

        var exception = Assert.Throws<PdfOperationException>(
            () => PdfAttachments.Add(compact, "invoice.xml", Xml(), "text/xml"));

        Assert.Contains("incremental update", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_attachment_added_to_a_compiler_document_leaves_the_document_as_it_was()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pdfua", "ua1.pdf");
        Assert.True(File.Exists(path), $"the fixture is missing from {path}");
        var pdf = File.ReadAllBytes(path);

        var updated = PdfAttachments.Add(pdf, "factur-x.xml", Xml(), "text/xml", PdfAttachmentRole.Data);

        Assert.Equal(Xml(), PdfAttachments.Extract(updated, "factur-x.xml"));

        // The update appends: the page, its text and the trailer's /ID are the producer's, untouched.
        var before = PdfTextProbe.Pages(pdf);
        var after = PdfTextProbe.Pages(updated);

        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before[0], after[0]);
        Assert.Contains("/ID[", Encoding.Latin1.GetString(updated), StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_that_are_not_a_pdf_are_refused()
    {
        Assert.Throws<PdfOperationException>(() => PdfAttachments.Read("not a pdf"u8));
    }

    /// <summary>A document carrying <paramref name="attachments"/> as objects 3 upwards, in a name tree.</summary>
    private static byte[] WithAttachments(params (string Name, byte[] Content, bool Compress)[] attachments)
    {
        var bodies = new List<string> { string.Empty, "<</Type/Pages/Count 0/Kids[]>>" };
        var entries = new List<string>();
        var number = 3;

        foreach (var (name, content, compress) in attachments)
        {
            var streamNumber = number++;
            var specNumber = number++;

            bodies.Add(StreamBody(content, compress));
            bodies.Add("<</Type/Filespec/F(" + name + ")/EF<</F "
                + streamNumber.ToString(CultureInfo.InvariantCulture)
                + " 0 R>>/AFRelationship/Data>>");

            entries.Add("(" + name + ") " + specNumber.ToString(CultureInfo.InvariantCulture) + " 0 R");
        }

        var treeNumber = number;
        bodies.Add("<</Names[" + string.Join(" ", entries) + "]>>");
        bodies[0] = "<</Type/Catalog/Pages 2 0 R/Names<</EmbeddedFiles "
            + treeNumber.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>";

        return PdfTestDocument.Document(bodies);
    }

    private static string StreamBody(byte[] content, bool compress)
    {
        var stored = compress ? Deflate(content) : content;
        var filter = compress ? "/Filter/FlateDecode" : string.Empty;

        return "<</Type/EmbeddedFile/Subtype/text#2Fxml" + filter + "/Length "
            + stored.Length.ToString(CultureInfo.InvariantCulture)
            + ">>\nstream\n" + Encoding.Latin1.GetString(stored) + "\nendstream";
    }

    private static byte[] Deflate(byte[] content)
    {
        using var output = new MemoryStream();

        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(content);
        }

        return output.ToArray();
    }

    private static string UnicodeHex(string text)
        => "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text)) + ">";
}
