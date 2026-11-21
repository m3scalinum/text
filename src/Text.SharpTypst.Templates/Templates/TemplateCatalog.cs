using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace M3scalinum.Text.SharpTypst.Templates;

/// <summary>
/// The layouts of the pack, keyed by id and version. Every layout is embedded in this assembly, so
/// a consumer renders one without shipping a template directory.
/// </summary>
public sealed class TemplateCatalog
{
    /// <summary>Prefix every embedded layout sits under, followed by the template id.</summary>
    private const string ResourceRoot = "M3scalinum.Text.SharpTypst.Templates.templates";

    private static readonly Lazy<TemplateCatalog> DefaultCatalog = new(CreateDefault, isThreadSafe: true);

    private readonly TemplateDescriptor[] _templates;

    /// <summary>Builds a catalog over a set of descriptors.</summary>
    /// <param name="templates">The descriptors, one per id and version.</param>
    /// <exception cref="ArgumentException">Two descriptors share an id and a version.</exception>
    public TemplateCatalog(IEnumerable<TemplateDescriptor> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);

        var list = new List<TemplateDescriptor>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in templates)
        {
            ArgumentNullException.ThrowIfNull(descriptor);

            if (!keys.Add(descriptor.Id + "\u0000" + descriptor.Version))
            {
                throw new ArgumentException(
                    $"the catalog already holds '{descriptor.Id}' version '{descriptor.Version}'",
                    nameof(templates));
            }

            list.Add(descriptor);
        }

        _templates = [.. list];
    }

    /// <summary>The seven layouts shipped in this assembly, in <see cref="TemplateIds"/> order.</summary>
    public static TemplateCatalog Default => DefaultCatalog.Value;

    /// <summary>Every layout the catalog holds, newest version last within an id.</summary>
    public IReadOnlyList<TemplateDescriptor> Templates => _templates;

    /// <summary>Returns the newest version of a layout.</summary>
    /// <param name="id">Catalog id, for example <see cref="TemplateIds.Invoice"/>.</param>
    /// <exception cref="TypstTemplateException">No layout has that id.</exception>
    public TemplateDescriptor Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var matches = Matching(id);
        if (matches.Count == 0)
        {
            throw new TypstTemplateException($"no template '{id}' in the catalog", id);
        }

        var newest = matches[0];
        foreach (var candidate in matches)
        {
            if (TemplateVersion.Compare(candidate.Version, newest.Version) > 0)
            {
                newest = candidate;
            }
        }

        return newest;
    }

    /// <summary>Returns one exact version of a layout.</summary>
    /// <param name="id">Catalog id.</param>
    /// <param name="version">Version declared by the manifest.</param>
    /// <exception cref="TypstTemplateException">No layout has that id and version.</exception>
    public TemplateDescriptor Get(string id, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (!TryGet(id, version, out var descriptor))
        {
            throw new TypstTemplateException(
                $"no template '{id}' version '{version}' in the catalog", id);
        }

        return descriptor;
    }

    /// <summary>Looks a layout up without throwing.</summary>
    /// <param name="id">Catalog id.</param>
    /// <param name="version">
    /// The exact version to return, or <see langword="null"/> for the newest one.
    /// </param>
    /// <param name="descriptor">The layout found, or <see langword="null"/>.</param>
    public bool TryGet(
        string id,
        string? version,
        [NotNullWhen(true)] out TemplateDescriptor? descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        descriptor = null;

        if (version is null)
        {
            var matches = Matching(id);
            if (matches.Count == 0)
            {
                return false;
            }

            descriptor = matches
                .OrderByDescending(candidate => candidate.Version, Comparer<string>.Create(TemplateVersion.Compare))
                .First();
            return true;
        }

        descriptor = _templates.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Version, version, StringComparison.OrdinalIgnoreCase));

        return descriptor is not null;
    }

    private List<TemplateDescriptor> Matching(string id)
        => _templates
            .Where(descriptor => string.Equals(descriptor.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static TemplateCatalog CreateDefault()
    {
        var assembly = typeof(TemplateCatalog).Assembly;

        TemplateDescriptor Load(string id)
        {
            var prefix = ResourceRoot + "." + id;
            return new TemplateDescriptor(
                id,
                TypstTemplate.LoadEmbedded(assembly, prefix),
                prefix,
                [TemplateLanguage.English, TemplateLanguage.French]);
        }

        return new TemplateCatalog(
        [
            Load(TemplateIds.Invoice),
            Load(TemplateIds.Quotation),
            Load(TemplateIds.Statement),
            Load(TemplateIds.Attestation),
            Load(TemplateIds.Contract),
            Load(TemplateIds.Payslip),
            Load(TemplateIds.PurchaseOrder),
        ]);
    }
}
