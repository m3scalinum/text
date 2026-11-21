using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// One terminal field of an AcroForm: the dictionary that carries <c>/FT</c>, the widgets it draws
/// through, and the name a caller fills it by.
/// </summary>
/// <param name="Dictionary">The field dictionary.</param>
/// <param name="Name">The fully qualified name, parts joined with a dot.</param>
/// <param name="FieldType">The <c>/FT</c> value, inherited from the parent when the field omits it.</param>
/// <param name="Widgets">The widget annotations, the field's own dictionary included when merged.</param>
/// <param name="Owner">The dictionary that lists this field in <c>/Fields</c> or <c>/Kids</c>.</param>
/// <param name="OwnerKey">Whether <paramref name="Owner"/> lists it in <c>Fields</c> or <c>Kids</c>.</param>
internal sealed record PdfFormField(
    PdfDictionary Dictionary,
    string Name,
    string? FieldType,
    IReadOnlyList<PdfDictionary> Widgets,
    PdfDictionary Owner,
    string OwnerKey)
{
    /// <summary>Whether the field is a signature field, which these operations never rewrite.</summary>
    public bool IsSignature => string.Equals(FieldType, "Sig", StringComparison.Ordinal);
}

/// <summary>Reads the field tree of an AcroForm and the appearance a widget draws.</summary>
internal static class PdfFormFields
{
    /// <summary>The catalog's <c>/AcroForm</c> dictionary, or <see langword="null"/>.</summary>
    public static PdfDictionary? Form(PdfFile file)
        => file.Catalog is { } catalog ? file.ResolveDictionary(catalog["AcroForm"]) : null;

    /// <summary>The terminal fields of <paramref name="form"/>, in the order the tree lists them.</summary>
    /// <param name="file">The document.</param>
    /// <param name="form">The AcroForm dictionary.</param>
    /// <param name="owners">
    /// When given, receives every field's owner: the dictionary that lists it in <c>/Fields</c> or
    /// <c>/Kids</c>. Flattening needs it to remove a field it could not render from the tree.
    /// </param>
    public static List<PdfFormField> Collect(
        PdfFile file,
        PdfDictionary form,
        IDictionary<PdfDictionary, PdfDictionary>? owners = null)
    {
        var fields = new List<PdfFormField>();

        foreach (var item in file.ResolveArray(form["Fields"]))
        {
            if (file.Resolve(item) is PdfDictionary field)
            {
                owners?[field] = form;
                Walk(file, field, null, null, form, "Fields", fields, owners);
            }
        }

        return fields;
    }

    private static void Walk(
        PdfFile file,
        PdfDictionary field,
        string? prefix,
        string? inheritedType,
        PdfDictionary owner,
        string ownerKey,
        List<PdfFormField> fields,
        IDictionary<PdfDictionary, PdfDictionary>? owners)
    {
        var type = Name(file, field["FT"]) ?? inheritedType;
        var partial = (file.Resolve(field["T"]) as PdfString)?.Decode();
        var name = string.IsNullOrEmpty(partial)
            ? prefix ?? string.Empty
            : string.IsNullOrEmpty(prefix) ? partial : prefix + "." + partial;

        var widgets = new List<PdfDictionary>();
        var children = new List<PdfDictionary>();

        foreach (var kid in file.ResolveArray(field["Kids"]))
        {
            if (file.Resolve(kid) is not PdfDictionary child)
            {
                continue;
            }

            if (string.Equals(Name(file, child["Subtype"]), "Widget", StringComparison.Ordinal))
            {
                widgets.Add(child);
            }
            else
            {
                children.Add(child);
            }
        }

        if (string.Equals(Name(file, field["Subtype"]), "Widget", StringComparison.Ordinal))
        {
            fields.Add(new PdfFormField(field, name, type, [field], owner, ownerKey));
            return;
        }

        if (children.Count > 0)
        {
            foreach (var child in children)
            {
                owners?[child] = field;
                Walk(file, child, name, type, field, "Kids", fields, owners);
            }

            return;
        }

        fields.Add(new PdfFormField(field, name, type, widgets, owner, ownerKey));
    }

    /// <summary>The <c>/Rect</c> of a widget, or <see langword="null"/> when it carries none.</summary>
    public static double[]? Rectangle(PdfFile file, PdfDictionary widget)
        => Numbers(file, widget["Rect"]);

    /// <summary>The <c>/BBox</c> of an appearance stream, or <see langword="null"/>.</summary>
    public static double[]? BoundingBox(PdfFile file, PdfDictionary appearance)
        => Numbers(file, appearance["BBox"]);

    /// <summary>
    /// The appearance a widget currently draws: its normal appearance stream, or the state its
    /// <c>/AS</c> (or the field's <c>/V</c>) names when <c>/AP /N</c> is a dictionary of states.
    /// </summary>
    public static PdfStream? Appearance(PdfFile file, PdfDictionary widget, PdfDictionary field)
    {
        var forms = file.ResolveDictionary(widget["AP"]);
        if (forms is null)
        {
            return null;
        }

        switch (file.Resolve(forms["N"]))
        {
            case PdfStream stream:
                return stream;
            case PdfDictionary states:
                var state = Name(file, widget["AS"]) ?? Name(file, field["V"]);
                if (state is not null && file.Resolve(states[state]) is PdfStream selected)
                {
                    return selected;
                }

                return file.Resolve(states["Off"]) is PdfStream off ? off : null;
            default:
                return null;
        }
    }

    /// <summary>The names <c>/AP /N</c> offers when it is a dictionary of states.</summary>
    public static IReadOnlyList<string> States(PdfFile file, PdfDictionary widget)
    {
        var forms = file.ResolveDictionary(widget["AP"]);

        return file.Resolve(forms?["N"]) is PdfDictionary states
            ? [.. states.Entries.Keys]
            : [];
    }

    /// <summary>A name value, without the slash.</summary>
    public static string? Name(PdfFile file, PdfValue? value)
        => file.Resolve(value) is PdfName name ? name.Value : null;

    /// <summary>Four numbers, when the entry is an array of at least four.</summary>
    public static double[]? Numbers(PdfFile file, PdfValue? value)
    {
        var resolved = file.Resolve(value);
        if (resolved is not PdfArray array || array.Items.Count < 4)
        {
            return null;
        }

        var numbers = new double[4];
        for (var index = 0; index < 4; index++)
        {
            if (file.Resolve(array.Items[index]) is not PdfNumber number)
            {
                return null;
            }

            numbers[index] = number.Value;
        }

        return numbers;
    }

    /// <summary>The font resource name and size of a <c>/DA</c> string, defaulting to Helvetica 12.</summary>
    public static (string Font, double Size) DefaultAppearance(PdfFile file, params PdfDictionary[] dictionaries)
    {
        foreach (var dictionary in dictionaries)
        {
            if (file.Resolve(dictionary["DA"]) is not PdfString text)
            {
                continue;
            }

            var tokens = text.Decode().Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (var index = 0; index + 2 < tokens.Length; index++)
            {
                if (!string.Equals(tokens[index + 2], "Tf", StringComparison.Ordinal) ||
                    !tokens[index].StartsWith('/'))
                {
                    continue;
                }

                var size = double.TryParse(tokens[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                           parsed > 0
                    ? parsed
                    : 12;

                return (tokens[index][1..], size);
            }
        }

        return ("Helv", 12);
    }
}
