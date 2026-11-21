using System;
using System.Collections.Generic;
using System.Linq;
using M3scalinum.Text.SharpTypst.Compilation;

namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>
/// One layout of the pack: its identity, the template already loaded from the assembly's embedded
/// resources, and the languages its labels ship in.
/// </summary>
public sealed class TemplateDescriptor
{
    private readonly TemplateLanguage[] _languages;

    /// <summary>Describes a template.</summary>
    /// <param name="id">Catalog id, for example <see cref="TemplateIds.Invoice"/>.</param>
    /// <param name="template">The loaded template.</param>
    /// <param name="resourcePrefix">
    /// Resource prefix the template was loaded from, so a caller can load the typed variant of the
    /// same layout with <c>TypstTemplate&lt;TOptions&gt;.LoadEmbedded</c>.
    /// </param>
    /// <param name="languages">Languages <see cref="TemplateLabels.For"/> can supply labels in.</param>
    /// <exception cref="ArgumentException">
    /// An id or a prefix is blank, or no language is declared.
    /// </exception>
    public TemplateDescriptor(
        string id,
        TypstTemplate template,
        string resourcePrefix,
        IEnumerable<TemplateLanguage> languages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);
        ArgumentNullException.ThrowIfNull(languages);

        _languages = languages.Distinct().ToArray();
        if (_languages.Length == 0)
        {
            throw new ArgumentException("a template descriptor declares at least one language", nameof(languages));
        }

        Id = id;
        Template = template;
        ResourcePrefix = resourcePrefix;
    }

    /// <summary>Catalog id.</summary>
    public string Id { get; }

    /// <summary>Version declared by the template's manifest, independent of the package version.</summary>
    public string Version => Template.Manifest.Version;

    /// <summary>Description declared by the manifest, when it has one.</summary>
    public string? Description => Template.Manifest.Description;

    /// <summary>The loaded template: its manifest, its source and its assets.</summary>
    public TypstTemplate Template { get; }

    /// <summary>The template's parsed manifest.</summary>
    public TypstTemplateManifest Manifest => Template.Manifest;

    /// <summary>Embedded-resource prefix the layout was loaded from.</summary>
    public string ResourcePrefix { get; }

    /// <summary>Languages the layout's labels ship in.</summary>
    public IReadOnlyList<TemplateLanguage> Languages => _languages;

    /// <summary>Renders this layout to PDF.</summary>
    /// <param name="compiler">The compiler to render with.</param>
    /// <param name="parameters">Values keyed by manifest name.</param>
    public byte[] Render(TypstCompiler compiler, IReadOnlyDictionary<string, object?> parameters)
        => Template.Render(compiler, parameters);

    /// <summary>Loads this layout again behind its typed options record.</summary>
    /// <typeparam name="TOptions">
    /// The record generated for this layout. It must live in the assembly the layout is embedded in,
    /// which is the case for the records the pack ships: <see cref="InvoiceOptions"/>,
    /// <see cref="QuotationOptions"/>, <see cref="StatementOptions"/> and
    /// <see cref="AttestationOptions"/>.
    /// </typeparam>
    public TypstTemplate<TOptions> LoadTyped<TOptions>()
        where TOptions : ITypstTemplateOptions
        => TypstTemplate<TOptions>.LoadEmbedded(typeof(TOptions).Assembly, ResourcePrefix);
}
