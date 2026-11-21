using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using M3scalinum.Text.Templates;
using M3scalinum.Text.Typst.Templates;
using Xunit;

namespace M3scalinum.Text.Templates.Tests;

/// <summary>
/// The catalog itself: what it holds, how it resolves an id and a version, and how the embedded
/// layouts compare with the same files loaded from disk. No native library is involved.
/// </summary>
public sealed class TemplateCatalogTests
{
    private const string MinimalManifest = """
        name = "letter"
        version = "1.0.0"
        entry = "template.typ"

        [parameters]
        recipient = { type = "string", required = true }
        """;

    private const string MinimalSource = """
        #let render(p) = [
          #set page(paper: "a4")
          Letter for #p.recipient
        ]
        """;

    [Fact]
    public void The_default_catalog_holds_the_seven_layouts()
    {
        var ids = TemplateCatalog.Default.Templates.Select(descriptor => descriptor.Id).ToList();

        Assert.Equal(
            [
                TemplateIds.Invoice,
                TemplateIds.Quotation,
                TemplateIds.Statement,
                TemplateIds.Attestation,
                TemplateIds.Contract,
                TemplateIds.Payslip,
                TemplateIds.PurchaseOrder,
            ],
            ids);
        Assert.All(TemplateCatalog.Default.Templates, descriptor => Assert.Equal("1.0.0", descriptor.Version));
        Assert.All(
            TemplateCatalog.Default.Templates,
            descriptor => Assert.Equal(
                [TemplateLanguage.English, TemplateLanguage.French],
                descriptor.Languages));
    }

    [Fact]
    public void Every_layout_is_embedded_under_its_id()
    {
        foreach (var descriptor in TemplateCatalog.Default.Templates)
        {
            Assert.EndsWith("." + descriptor.Id, descriptor.ResourcePrefix, StringComparison.Ordinal);
            Assert.Equal(descriptor.Id, descriptor.Template.Manifest.Name);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
        }
    }

    [Fact]
    public void Get_returns_a_layout_by_id()
    {
        var descriptor = TemplateCatalog.Default.Get(TemplateIds.Invoice);

        Assert.Equal(TemplateIds.Invoice, descriptor.Id);
        Assert.Contains("facture", descriptor.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Get_returns_one_exact_version()
    {
        var descriptor = TemplateCatalog.Default.Get(TemplateIds.Statement, "1.0.0");

        Assert.Equal(TemplateIds.Statement, descriptor.Id);
    }

    [Fact]
    public void Get_ignores_the_case_of_an_id()
    {
        var descriptor = TemplateCatalog.Default.Get("INVOICE");

        Assert.Equal(TemplateIds.Invoice, descriptor.Id);
    }

    [Fact]
    public void Get_throws_the_typed_template_exception_for_an_unknown_id()
    {
        var exception = Assert.Throws<TypstTemplateException>(
            () => TemplateCatalog.Default.Get("delivery-note"));

        Assert.Equal("delivery-note", exception.TemplateName);
        Assert.Contains("delivery-note", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_throws_the_typed_template_exception_for_an_unknown_version()
    {
        var exception = Assert.Throws<TypstTemplateException>(
            () => TemplateCatalog.Default.Get(TemplateIds.Invoice, "9.9.9"));

        Assert.Equal(TemplateIds.Invoice, exception.TemplateName);
        Assert.Contains("9.9.9", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_rejects_a_blank_id()
    {
        Assert.ThrowsAny<ArgumentException>(() => TemplateCatalog.Default.Get(" "));
    }

    [Fact]
    public void Get_rejects_a_blank_version()
    {
        Assert.ThrowsAny<ArgumentException>(() => TemplateCatalog.Default.Get(TemplateIds.Invoice, ""));
    }

    [Fact]
    public void TryGet_reports_a_miss_without_throwing()
    {
        Assert.False(TemplateCatalog.Default.TryGet("certificate", null, out var descriptor));
        Assert.Null(descriptor);
    }

    [Fact]
    public void TryGet_returns_the_newest_version_when_none_is_named()
    {
        var catalog = new TemplateCatalog(
        [
            Letter("1.9.0"),
            Letter("1.10.0"),
            Letter("1.2.4"),
        ]);

        Assert.True(catalog.TryGet("letter", null, out var newest));
        Assert.Equal("1.10.0", newest!.Version);
        Assert.Equal("1.10.0", catalog.Get("letter").Version);
        Assert.Equal("1.2.4", catalog.Get("letter", "1.2.4").Version);
    }

    [Fact]
    public void The_newest_version_is_compared_numerically_segment_by_segment()
    {
        var catalog = new TemplateCatalog([Letter("1.9.0"), Letter("1.10.0")]);

        Assert.Equal("1.10.0", catalog.Get("letter").Version);
    }

    [Fact]
    public void A_version_segment_that_is_not_numeric_falls_back_to_an_ordinal_comparison()
    {
        var catalog = new TemplateCatalog([Letter("1.0.0-beta"), Letter("1.0.0")]);

        // '1.0.0-beta' sorts after '1.0.0' ordinally, so it is the newest by this rule.
        Assert.Equal("1.0.0-beta", catalog.Get("letter").Version);
    }

    [Fact]
    public void A_shorter_version_is_padded_with_zeroes()
    {
        var catalog = new TemplateCatalog([Letter("1.0"), Letter("1.0.1")]);

        Assert.Equal("1.0.1", catalog.Get("letter").Version);
        Assert.Equal("1.0", catalog.Get("letter", "1.0").Version);
    }

    [Fact]
    public void A_catalog_rejects_a_duplicated_id_and_version()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TemplateCatalog([Letter("1.0.0"), Letter("1.0.0")]));

        Assert.Contains("letter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catalog_rejects_a_null_descriptor()
    {
        Assert.Throws<ArgumentNullException>(() => new TemplateCatalog([null!]));
    }

    [Fact]
    public void A_catalog_rejects_a_null_sequence()
    {
        Assert.Throws<ArgumentNullException>(() => new TemplateCatalog(null!));
    }

    [Fact]
    public void A_descriptor_rejects_blank_identifiers_and_an_empty_language_list()
    {
        var template = InMemoryTemplate();

        Assert.ThrowsAny<ArgumentException>(() => new TemplateDescriptor(" ", template, "prefix", [TemplateLanguage.English]));
        Assert.ThrowsAny<ArgumentException>(() => new TemplateDescriptor("id", template, " ", [TemplateLanguage.English]));
        Assert.ThrowsAny<ArgumentException>(() => new TemplateDescriptor("id", template, "prefix", []));
        Assert.Throws<ArgumentNullException>(() => new TemplateDescriptor("id", null!, "prefix", [TemplateLanguage.English]));
        Assert.Throws<ArgumentNullException>(() => new TemplateDescriptor("id", template, "prefix", null!));
    }

    [Fact]
    public void A_descriptor_drops_a_duplicated_language()
    {
        var descriptor = new TemplateDescriptor(
            "id",
            InMemoryTemplate(),
            "prefix",
            [TemplateLanguage.French, TemplateLanguage.French, TemplateLanguage.English]);

        Assert.Equal([TemplateLanguage.French, TemplateLanguage.English], descriptor.Languages);
    }

    [Fact]
    public void The_embedded_layout_and_the_directory_agree_on_every_parameter()
    {
        foreach (var descriptor in TemplateCatalog.Default.Templates)
        {
            var onDisk = TypstTemplate.Load(Samples.Directory(descriptor.Id));
            var embedded = descriptor.Template.Manifest;

            Assert.Equal(onDisk.Manifest.Name, embedded.Name);
            Assert.Equal(onDisk.Manifest.Version, embedded.Version);
            Assert.Equal(onDisk.Manifest.Entry, embedded.Entry);
            Assert.Equal(onDisk.Manifest.Description, embedded.Description);
            Assert.Equal(onDisk.Manifest.Parameters.Keys, embedded.Parameters.Keys);

            foreach (var (name, parameter) in onDisk.Manifest.Parameters)
            {
                var other = embedded.Parameters[name];

                Assert.Equal(parameter.Type, other.Type);
                Assert.Equal(parameter.Required, other.Required);
                Assert.Equal(parameter.Default, other.Default);
                Assert.Equal(parameter.ItemType, other.ItemType);
            }
        }
    }

    private static TemplateDescriptor Letter(string version)
    {
        var manifest = MinimalManifest.Replace("1.0.0", version, StringComparison.Ordinal);
        return new TemplateDescriptor(
            "letter",
            InMemoryTemplate(manifest),
            "M3scalinum.Text.Templates.Tests.templates.letter",
            [TemplateLanguage.English]);
    }

    private static TypstTemplate InMemoryTemplate(string manifest = MinimalManifest)
        => TypstTemplate.Load(
            manifest,
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["template.typ"] = Encoding.UTF8.GetBytes(MinimalSource),
            });
}
