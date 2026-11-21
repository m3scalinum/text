using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Fills and flattens the AcroForm fields of a PDF.
/// </summary>
/// <remarks>
/// Both operations rewrite the document: the catalog and every object it reaches are copied into a
/// new file with a fresh cross-reference table. Filling writes <c>/V</c> on the field the name
/// selects, and redraws the widget's appearance when the widget carries one, so a viewer that does
/// not regenerate appearances shows the new value. Flattening draws each widget's appearance into
/// the page content, removes the widget from <c>/Annots</c> and removes the field from the form.
/// <para>
/// Flattening refuses what it cannot draw -- a widget with no appearance stream, an empty rectangle,
/// a signature field -- and leaves those fields interactive, reporting each one. The <c>/AcroForm</c>
/// entry is only removed when every field was flattened; a form that kept its values and its
/// appearance is worth more than one whose fields disappeared. An encrypted document is refused;
/// open it with <see cref="PdfEncryption.Open"/> and fill the bytes it returns.
/// </para>
/// </remarks>
public static class PdfForm
{
    /// <summary>
    /// Sets <c>/V</c> on the fields named in <paramref name="values"/> and updates the appearance of
    /// the widgets that carry one.
    /// </summary>
    /// <param name="pdf">The document.</param>
    /// <param name="values">
    /// Field values by fully qualified name, parts joined with a dot. A checkbox or radio button takes
    /// the appearance state it selects (<c>Yes</c>, <c>Off</c>, or the state the form names).
    /// </param>
    /// <returns>The filled document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A name does not match any field of the form.</exception>
    /// <exception cref="PdfOperationException">
    /// The document has no AcroForm, is encrypted, cannot be read, or a name selects a signature field.
    /// </exception>
    public static byte[] Fill(byte[] pdf, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(values);

        var file = PdfSource.Open(pdf, "fill");
        var form = PdfFormFields.Form(file)
            ?? throw new PdfOperationException("the document has no /AcroForm, so it has no field to fill");

        var fields = PdfFormFields.Collect(file, form);
        var byName = new Dictionary<string, PdfFormField>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            if (field.Name.Length > 0)
            {
                byName[field.Name] = field;
            }
        }

        var missing = values.Keys
            .Where(key => !byName.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"the form has no field named {string.Join(", ", missing.Select(key => $"'{key}'"))}",
                nameof(values));
        }

        // The whole set is validated before anything is written, so a signature field cannot leave the
        // document half-filled.
        foreach (var name in values.Keys)
        {
            if (byName[name].IsSignature)
            {
                throw new PdfOperationException(
                    $"the field '{name}' is a signature field, and filling it would destroy the signature");
            }
        }

        var writer = new PdfObjectWriter();
        var catalogNumber = writer.Reserve();
        var copier = new PdfObjectCopier(file, writer);

        foreach (var (name, value) in values)
        {
            Apply(file, copier, writer, byName[name], value);
        }

        var catalog = copier.CopyDictionary(file.Catalog!);
        writer.Set(catalogNumber, catalog);

        return writer.Build(catalogNumber, file.MajorVersion, file.MinorVersion);
    }

    /// <summary>
    /// Draws every widget appearance it can into its page and removes the fields whose appearance was
    /// drawn from the form.
    /// </summary>
    /// <param name="pdf">The document.</param>
    /// <returns>Which fields were flattened and which were left interactive, with the reason.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pdf"/> is <see langword="null"/>.</exception>
    /// <exception cref="PdfOperationException">The document is encrypted or cannot be read.</exception>
    public static PdfFormFlattenResult Flatten(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        var file = PdfSource.Open(pdf, "flatten");
        var form = PdfFormFields.Form(file);

        if (form is null)
        {
            return new PdfFormFlattenResult(pdf, [], []);
        }

        var owners = new Dictionary<PdfDictionary, PdfDictionary>();
        var fields = PdfFormFields.Collect(file, form, owners);

        if (fields.Count == 0)
        {
            // An AcroForm without a field draws nothing: there is no value to lose, so it is removed
            // rather than left as an empty shell.
            file.Catalog!.Entries.Remove("AcroForm");

            return new PdfFormFlattenResult(Compose(file), [], []);
        }

        var pages = file.Pages();
        var pageOf = WidgetPages(file, pages);

        var flattenedNames = new List<string>();
        var skipped = new List<PdfFormFlattenSkip>();
        var flattened = new List<PdfFormField>();
        var groups = new Dictionary<int, FlattenGroup>();

        foreach (var field in fields)
        {
            var name = Display(field);

            if (field.IsSignature)
            {
                skipped.Add(new PdfFormFlattenSkip(name, "a signature field is not flattened"));
                continue;
            }

            if (field.Widgets.Count == 0)
            {
                skipped.Add(new PdfFormFlattenSkip(name, "the field has no widget annotation"));
                continue;
            }

            string? reason = null;
            var items = new List<FlattenItem>();

            foreach (var widget in field.Widgets)
            {
                var appearance = PdfFormFields.Appearance(file, widget, field.Dictionary);
                if (appearance is null)
                {
                    reason = "the widget has no usable appearance stream";
                    break;
                }

                var rectangle = PdfFormFields.Rectangle(file, widget);
                if (rectangle is null || rectangle[2] <= rectangle[0] || rectangle[3] <= rectangle[1])
                {
                    reason = "the widget has no usable /Rect";
                    break;
                }

                var box = PdfFormFields.BoundingBox(file, appearance.Dictionary);
                if (box is null || box[2] <= box[0] || box[3] <= box[1])
                {
                    reason = "the appearance has no usable /BBox";
                    break;
                }

                if (!pageOf.TryGetValue(widget, out var page))
                {
                    reason = "the widget is not attached to a page";
                    break;
                }

                items.Add(new FlattenItem(widget, appearance, rectangle, box, page));
            }

            if (reason is not null)
            {
                skipped.Add(new PdfFormFlattenSkip(name, reason));
                continue;
            }

            flattened.Add(field);
            flattenedNames.Add(name);

            foreach (var item in items)
            {
                if (!groups.TryGetValue(item.Page.Number, out var group))
                {
                    group = new FlattenGroup(item.Page);
                    groups[item.Page.Number] = group;
                }

                group.Items.Add(item);
            }
        }

        if (flattened.Count == 0)
        {
            return new PdfFormFlattenResult(pdf, flattenedNames, skipped);
        }

        foreach (var field in flattened)
        {
            Detach(file, form, owners, field);
        }

        if (file.ResolveArray(form["Fields"]).Count == 0)
        {
            file.Catalog!.Entries.Remove("AcroForm");
        }

        var writer = new PdfObjectWriter();
        var catalogNumber = writer.Reserve();
        var copier = new PdfObjectCopier(file, writer);
        var catalog = copier.CopyDictionary(file.Catalog!);

        var widgets = new HashSet<PdfDictionary>(flattened.SelectMany(field => field.Widgets));
        var dropped = DroppedWidgets(file, pages, copier, widgets);

        foreach (var group in groups.Values)
        {
            var number = copier.Map(group.Page.Number)
                ?? throw new PdfOperationException("a page holding a flattened widget was not copied");

            var page = writer.Get(number) as PdfDictionary
                ?? throw new PdfOperationException("a page holding a flattened widget is not a dictionary");

            Draw(copier, writer, group, page, dropped);
        }

        writer.Set(catalogNumber, catalog);

        return new PdfFormFlattenResult(
            writer.Build(catalogNumber, file.MajorVersion, file.MinorVersion),
            flattenedNames,
            skipped);
    }

    private static void Apply(
        PdfFile file,
        PdfObjectCopier copier,
        PdfObjectWriter writer,
        PdfFormField field,
        string value)
    {
        if (string.Equals(field.FieldType, "Btn", StringComparison.Ordinal))
        {
            ApplyState(file, field, value);

            return;
        }

        field.Dictionary.Entries["V"] = new PdfString(Encode(value));

        foreach (var widget in field.Widgets)
        {
            Redraw(file, copier, writer, field.Dictionary, widget, value);
        }
    }

    /// <summary>Sets a checkbox or radio button to the appearance state <paramref name="value"/> names.</summary>
    private static void ApplyState(PdfFile file, PdfFormField field, string value)
    {
        var off = value.Length == 0 ||
                  string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(value, "0", StringComparison.Ordinal);

        PdfDictionary? selected = null;
        var state = "Off";

        if (!off)
        {
            foreach (var widget in field.Widgets)
            {
                if (PdfFormFields.States(file, widget).Contains(value, StringComparer.Ordinal))
                {
                    selected = widget;
                    state = value;
                    break;
                }
            }

            if (selected is null)
            {
                throw new PdfOperationException(
                    $"the field '{Display(field)}' has no appearance state named '{value}'");
            }
        }

        field.Dictionary.Entries["V"] = new PdfName(state);

        foreach (var widget in field.Widgets)
        {
            var states = PdfFormFields.States(file, widget);
            if (states.Count == 0)
            {
                continue;
            }

            var widgetState = ReferenceEquals(widget, selected) ? state : "Off";
            if (!states.Contains(widgetState, StringComparer.Ordinal))
            {
                // Asking for "off" must never turn another state on: the widget keeps the state it
                // already shows instead.
                continue;
            }

            widget.Entries["AS"] = new PdfName(widgetState);
        }
    }

    /// <summary>Replaces a text widget's normal appearance with one drawing <paramref name="value"/>.</summary>
    private static void Redraw(
        PdfFile file,
        PdfObjectCopier copier,
        PdfObjectWriter writer,
        PdfDictionary field,
        PdfDictionary widget,
        string value)
    {
        var forms = file.ResolveDictionary(widget["AP"]);

        if (forms is null || file.Resolve(forms["N"]) is not PdfStream appearance)
        {
            // The field carries no appearance stream to update. /V is written, and the viewer is the
            // one that draws the value; that is the documented behaviour, not a silent failure.
            return;
        }

        var box = PdfFormFields.BoundingBox(file, appearance.Dictionary);

        if (box is null && PdfFormFields.Rectangle(file, widget) is { } rectangle)
        {
            box = [0, 0, rectangle[2] - rectangle[0], rectangle[3] - rectangle[1]];
        }

        if (box is null || box[2] <= box[0] || box[3] <= box[1])
        {
            return;
        }

        var (font, size) = PdfFormFields.DefaultAppearance(file, field, widget);
        var dictionary = copier.CopyDictionary(appearance.Dictionary, "Length");
        dictionary.Entries["Type"] = new PdfName("XObject");
        dictionary.Entries["Subtype"] = new PdfName("Form");
        dictionary.Entries["BBox"] = Box(box);

        var resources = Resources(writer, dictionary);
        var fontName = EnsureFont(writer, resources, font);

        var number = writer.AddStream(dictionary, Encoding.Latin1.GetBytes(Text(fontName, size, box, value)));

        var replacement = new PdfDictionary();
        replacement.Entries["N"] = appearance;
        widget.Entries["AP"] = replacement;

        copier.Substitute(appearance, new PdfReference(number));
    }

    /// <summary>Adds each flattened widget's appearance to its page and removes the widget annotation.</summary>
    private static void Draw(
        PdfObjectCopier copier,
        PdfObjectWriter writer,
        FlattenGroup group,
        PdfDictionary page,
        HashSet<int> dropped)
    {
        var resources = PageResources(copier, writer, group.Page, page);
        var xobjects = XObjects(writer, resources);
        var blocks = new StringBuilder();
        var index = 0;

        foreach (var item in group.Items)
        {
            var dictionary = copier.CopyDictionary(item.Appearance.Dictionary, "Length");
            dictionary.Entries["Type"] = new PdfName("XObject");
            dictionary.Entries["Subtype"] = new PdfName("Form");
            dictionary.Entries["BBox"] = Box(item.Box);

            var xobject = writer.AddStream(dictionary, item.Appearance.Data);
            var name = "M3Form" + group.Page.Number.ToString(CultureInfo.InvariantCulture) + "_" + index.ToString(CultureInfo.InvariantCulture);
            index++;

            xobjects.Entries[name] = new PdfReference(xobject);

            var width = item.Rectangle[2] - item.Rectangle[0];
            var height = item.Rectangle[3] - item.Rectangle[1];
            var scaleX = width / (item.Box[2] - item.Box[0]);
            var scaleY = height / (item.Box[3] - item.Box[1]);
            var left = item.Rectangle[0] - (scaleX * item.Box[0]);
            var bottom = item.Rectangle[1] - (scaleY * item.Box[1]);

            blocks.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"q\n{PdfSyntax.Number(scaleX)} 0 0 {PdfSyntax.Number(scaleY)} {PdfSyntax.Number(left)} {PdfSyntax.Number(bottom)} cm\n/{name} Do\nQ\n"));
        }

        var content = writer.AddStream(new PdfDictionary(), Encoding.Latin1.GetBytes(blocks.ToString()));
        page.Entries["Contents"] = WithContent(page["Contents"], new PdfReference(content));

        if (page["Annots"] is PdfArray annotations)
        {
            var kept = new PdfArray();

            foreach (var item in annotations.Items)
            {
                if (item is PdfReference reference && dropped.Contains(reference.Number))
                {
                    continue;
                }

                kept.Items.Add(item);
            }

            if (kept.Items.Count > 0)
            {
                page.Entries["Annots"] = kept;
            }
            else
            {
                page.Entries.Remove("Annots");
            }
        }
    }

    /// <summary>The page resources of the copied page, materialised so an XObject can be added to them.</summary>
    private static PdfDictionary PageResources(
        PdfObjectCopier copier,
        PdfObjectWriter writer,
        PdfPage source,
        PdfDictionary page)
    {
        switch (page["Resources"])
        {
            case PdfDictionary direct:
                return direct;
            case PdfReference reference when writer.Get(reference.Number) is PdfDictionary referenced:
                return referenced;
            default:
                var resources = source.Resources is null
                    ? new PdfDictionary()
                    : copier.CopyDictionary(source.Resources);
                page.Entries["Resources"] = resources;

                return resources;
        }
    }

    /// <summary>The <c>/XObject</c> sub-dictionary of <paramref name="resources"/>, created when absent.</summary>
    private static PdfDictionary XObjects(PdfObjectWriter writer, PdfDictionary resources)
    {
        switch (resources["XObject"])
        {
            case PdfDictionary direct:
                return direct;
            case PdfReference reference when writer.Get(reference.Number) is PdfDictionary referenced:
                return referenced;
            default:
                var created = new PdfDictionary();
                resources.Entries["XObject"] = created;

                return created;
        }
    }

    /// <summary>The <c>/Resources</c> dictionary of an appearance, created when absent.</summary>
    private static PdfDictionary Resources(PdfObjectWriter writer, PdfDictionary dictionary)
    {
        switch (dictionary["Resources"])
        {
            case PdfDictionary direct:
                return direct;
            case PdfReference reference when writer.Get(reference.Number) is PdfDictionary referenced:
                return referenced;
            default:
                var created = new PdfDictionary();
                dictionary.Entries["Resources"] = created;

                return created;
        }
    }

    /// <summary>
    /// Makes sure the appearance has a font under a name the content stream can select, and returns
    /// that name. A form whose appearance resources do not carry the <c>/DA</c> font still draws.
    /// </summary>
    private static string EnsureFont(PdfObjectWriter writer, PdfDictionary resources, string fontName)
    {
        PdfDictionary fonts;

        switch (resources["Font"])
        {
            case PdfDictionary direct:
                fonts = direct;
                break;
            case PdfReference reference when writer.Get(reference.Number) is PdfDictionary referenced:
                fonts = referenced;
                break;
            default:
                fonts = new PdfDictionary();
                resources.Entries["Font"] = fonts;
                break;
        }

        if (!fonts.ContainsKey(fontName))
        {
            var helvetica = new PdfDictionary();
            helvetica.Entries["Type"] = new PdfName("Font");
            helvetica.Entries["Subtype"] = new PdfName("Type1");
            helvetica.Entries["BaseFont"] = new PdfName("Helvetica");
            helvetica.Entries["Encoding"] = new PdfName("WinAnsiEncoding");
            fonts.Entries[fontName] = helvetica;
        }

        return fontName;
    }

    /// <summary>The content of a single-line text appearance that draws <paramref name="value"/>.</summary>
    private static string Text(string font, double size, double[] box, string value)
    {
        var height = box[3] - box[1];
        var baseline = Math.Max(1, ((height - size) / 2) + (size * 0.25));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"q\nBT\n/{font} {PdfSyntax.Number(size)} Tf\n0 g\n2 {PdfSyntax.Number(baseline)} Td\n(")
            + Escape(value)
            + ") Tj\nET\nQ";
    }

    private static PdfArray WithContent(PdfValue? contents, PdfReference addition)
    {
        var array = new PdfArray();

        switch (contents)
        {
            case PdfArray existing:
                array.Items.AddRange(existing.Items);
                break;
            case PdfReference reference:
                array.Items.Add(reference);
                break;
        }

        array.Items.Add(addition);

        return array;
    }

    /// <summary>The copied object numbers of the widgets the flatten removes from the pages.</summary>
    private static HashSet<int> DroppedWidgets(
        PdfFile file,
        IReadOnlyList<PdfPage> pages,
        PdfObjectCopier copier,
        HashSet<PdfDictionary> widgets)
    {
        var dropped = new HashSet<int>();

        foreach (var page in pages)
        {
            foreach (var item in file.ResolveArray(page.Dictionary["Annots"]))
            {
                if (file.TryResolveReference(item, out var number, out var resolved) &&
                    resolved is PdfDictionary annotation &&
                    widgets.Contains(annotation) &&
                    copier.Map(number) is { } target)
                {
                    dropped.Add(target);
                }
            }
        }

        return dropped;
    }

    /// <summary>Maps every annotation of the document to the page whose <c>/Annots</c> lists it.</summary>
    private static Dictionary<PdfDictionary, PdfPage> WidgetPages(PdfFile file, IReadOnlyList<PdfPage> pages)
    {
        var map = new Dictionary<PdfDictionary, PdfPage>();

        foreach (var page in pages)
        {
            foreach (var item in file.ResolveArray(page.Dictionary["Annots"]))
            {
                if (file.Resolve(item) is PdfDictionary annotation)
                {
                    map[annotation] = page;
                }
            }
        }

        return map;
    }

    /// <summary>Removes a flattened field from the form, and the parents it leaves empty.</summary>
    private static void Detach(
        PdfFile file,
        PdfDictionary form,
        Dictionary<PdfDictionary, PdfDictionary> owners,
        PdfFormField field)
    {
        Remove(file, field.Owner, field.OwnerKey, field.Dictionary);

        var owner = field.Owner;

        while (!ReferenceEquals(owner, form) &&
               owners.TryGetValue(owner, out var parent) &&
               file.ResolveArray(owner["Kids"]).Count == 0)
        {
            Remove(file, parent, ReferenceEquals(parent, form) ? "Fields" : "Kids", owner);
            owner = parent;
        }
    }

    private static void Remove(PdfFile file, PdfDictionary owner, string key, PdfDictionary target)
    {
        if (file.Resolve(owner[key]) is not PdfArray array)
        {
            return;
        }

        for (var index = array.Items.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(file.Resolve(array.Items[index]), target))
            {
                array.Items.RemoveAt(index);
            }
        }
    }

    private static PdfArray Box(double[] box)
    {
        var array = new PdfArray();
        array.Items.Add(new PdfNumber(box[0]));
        array.Items.Add(new PdfNumber(box[1]));
        array.Items.Add(new PdfNumber(box[2]));
        array.Items.Add(new PdfNumber(box[3]));

        return array;
    }

    /// <summary>The value as a PDF text string: Latin-1 when it fits, UTF-16BE otherwise.</summary>
    private static byte[] Encode(string value)
    {
        foreach (var character in value)
        {
            if (character > '\u00FF')
            {
                var bytes = new byte[2 + (value.Length * 2)];
                bytes[0] = 0xFE;
                bytes[1] = 0xFF;
                Encoding.BigEndianUnicode.GetBytes(value, 0, value.Length, bytes, 2);

                return bytes;
            }
        }

        return Encoding.Latin1.GetBytes(value);
    }

    /// <summary>Escapes a value for a WinAnsi literal string.</summary>
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is < ' ' or > '\u00FF')
            {
                builder.Append('?');
            }
            else if (character is '(' or ')' or '\\')
            {
                builder.Append('\\').Append(character);
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string Display(PdfFormField field)
        => field.Name.Length > 0 ? field.Name : "(unnamed)";

    /// <summary>Copies the whole catalog graph into a fresh file.</summary>
    private static byte[] Compose(PdfFile file)
    {
        var writer = new PdfObjectWriter();
        var catalogNumber = writer.Reserve();
        var copier = new PdfObjectCopier(file, writer);
        var catalog = copier.CopyDictionary(file.Catalog!);
        writer.Set(catalogNumber, catalog);

        return writer.Build(catalogNumber, file.MajorVersion, file.MinorVersion);
    }

    private sealed record FlattenItem(
        PdfDictionary Widget,
        PdfStream Appearance,
        double[] Rectangle,
        double[] Box,
        PdfPage Page);

    private sealed record FlattenGroup(PdfPage Page)
    {
        public List<FlattenItem> Items { get; } = [];
    }
}
