using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace M3scalinum.Text.Pdf.Tests;

/// <summary>
/// Adds an AcroForm to a document the compiler produced, as an incremental update with a valid
/// cross-reference chain. A form cannot come out of Typst, so the form half of the tests takes a
/// real page and puts a real widget on it: the operations then meet the same page tree, fonts and
/// content streams as they do in production.
/// </summary>
internal static class TestForm
{
    /// <summary>
    /// Adds a text field and its normal appearance stream to the first page.
    /// </summary>
    /// <param name="pdf">The document to extend.</param>
    /// <param name="name">The field name.</param>
    /// <param name="value">The value the field starts with.</param>
    /// <param name="withAppearance">Whether the widget carries an <c>/AP /N</c> stream.</param>
    public static byte[] WithTextField(byte[] pdf, string name, string value = "", bool withAppearance = true)
    {
        var document = PdfDocument.Parse(Encoding.Latin1.GetString(pdf));
        var next = document.NextObjectNumber();
        var fieldNumber = next++;
        var appearanceNumber = withAppearance ? next++ : 0;
        var pageNumber = document.PageNumbers()[0];

        var append = new PdfAppend(document.Text);
        append.Add(document.RootNumber, WithField(document.Catalog, fieldNumber));
        append.Add(pageNumber, WithAnnotation(document.Require(pageNumber).Body, fieldNumber));

        var field = new StringBuilder("<</Type/Annot/Subtype/Widget/FT/Tx/T(")
            .Append(Escape(name))
            .Append(")/V(")
            .Append(Escape(value))
            .Append(")/Rect[50 700 300 720]/P ")
            .Append(pageNumber.ToString(CultureInfo.InvariantCulture))
            .Append(" 0 R/DA(/Helv 12 Tf 0 g)/F 4");

        if (withAppearance)
        {
            field.Append("/AP<</N ")
                 .Append(appearanceNumber.ToString(CultureInfo.InvariantCulture))
                 .Append(" 0 R>>");
        }

        append.Add(fieldNumber, field.Append(">>").ToString());

        if (withAppearance)
        {
            append.Add(appearanceNumber, Appearance(value, "Helvetica"));
        }

        return Encoding.Latin1.GetBytes(append.Finish(document, next));
    }

    /// <summary>Adds a checkbox whose two appearance states are real streams.</summary>
    /// <param name="pdf">The document to extend.</param>
    /// <param name="name">The field name.</param>
    public static byte[] WithCheckBox(byte[] pdf, string name)
    {
        var document = PdfDocument.Parse(Encoding.Latin1.GetString(pdf));
        var next = document.NextObjectNumber();
        var fieldNumber = next++;
        var offNumber = next++;
        var onNumber = next++;
        var pageNumber = document.PageNumbers()[0];

        var append = new PdfAppend(document.Text);
        append.Add(document.RootNumber, WithField(document.Catalog, fieldNumber));
        append.Add(pageNumber, WithAnnotation(document.Require(pageNumber).Body, fieldNumber));
        append.Add(fieldNumber, string.Create(
            CultureInfo.InvariantCulture,
            $"<</Type/Annot/Subtype/Widget/FT/Btn/T({Escape(name)})/V/Off/Rect[50 650 70 670]/P {pageNumber} 0 R/AS/Off/AP<</N<</Off {offNumber} 0 R/Yes {onNumber} 0 R>>>>/F 4>>"));
        append.Add(offNumber, CheckBoxAppearance("0 0 20 20 re S"));
        append.Add(onNumber, CheckBoxAppearance("0 0 20 20 re S 2 w 3 3 m 17 17 l S 3 17 m 17 3 l S"));

        return Encoding.Latin1.GetBytes(append.Finish(document, next));
    }

    /// <summary>Adds a signature field, which fill and flatten must leave alone.</summary>
    /// <param name="pdf">The document to extend.</param>
    /// <param name="name">The field name.</param>
    public static byte[] WithSignatureField(byte[] pdf, string name)
    {
        var document = PdfDocument.Parse(Encoding.Latin1.GetString(pdf));
        var next = document.NextObjectNumber();
        var fieldNumber = next++;
        var pageNumber = document.PageNumbers()[0];

        var append = new PdfAppend(document.Text);
        append.Add(document.RootNumber, WithField(document.Catalog, fieldNumber));
        append.Add(pageNumber, WithAnnotation(document.Require(pageNumber).Body, fieldNumber));
        append.Add(fieldNumber, string.Create(
            CultureInfo.InvariantCulture,
            $"<</Type/Annot/Subtype/Widget/FT/Sig/T({Escape(name)})/Rect[50 600 200 640]/P {pageNumber} 0 R/F 4>>"));

        return Encoding.Latin1.GetBytes(append.Finish(document, next));
    }

    private static string Appearance(string value, string font)
    {
        var content = "q\nBT\n/Helv 12 Tf\n0 g\n2 5 Td\n(" + Escape(value) + ") Tj\nET\nQ";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"<</Type/XObject/Subtype/Form/FormType 1/BBox[0 0 250 20]/Resources<</Font<</Helv<</Type/Font/Subtype/Type1/BaseFont/{font}/Encoding/WinAnsiEncoding>>>>>>/Length {Encoding.Latin1.GetByteCount(content)}>>\nstream\n{content}\nendstream");
    }

    private static string CheckBoxAppearance(string drawing)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"<</Type/XObject/Subtype/Form/FormType 1/BBox[0 0 20 20]/Resources<<>>/Length {Encoding.Latin1.GetByteCount(drawing)}>>\nstream\n{drawing}\nendstream");

    /// <summary>Adds the field to the catalog's <c>/AcroForm</c>, keeping the fields already there.</summary>
    private static string WithField(string catalog, int fieldNumber)
        => PdfSyntax.Has(catalog, "AcroForm")
            ? throw new InvalidOperationException("the fixture already has an AcroForm")
            : Insert(catalog, "/AcroForm<</Fields[" + Reference(fieldNumber) + "]>>");

    /// <summary>Adds the widget to the page's <c>/Annots</c>, keeping the annotations already there.</summary>
    private static string WithAnnotation(string page, int fieldNumber)
    {
        if (PdfSyntax.ArrayBody(page, "Annots") is { } annots)
        {
            var existing = annots.Trim();

            return Regex.Replace(
                page,
                @"(/Annots\s*\[)([^\]]*)\]",
                match => match.Groups[1].Value + existing
                    + (existing.Length > 0 ? " " : string.Empty)
                    + Reference(fieldNumber) + "]",
                RegexOptions.CultureInvariant);
        }

        return Insert(page, "/Annots[" + Reference(fieldNumber) + "]");
    }

    private static string Insert(string body, string value)
    {
        var close = PdfSyntax.CloseDictionary(body, body.IndexOf("<<", StringComparison.Ordinal));

        if (close < 0)
        {
            throw new InvalidOperationException("the object is not a dictionary");
        }

        return string.Concat(body.AsSpan(0, close), value, body.AsSpan(close));
    }

    private static string Reference(int number) => number.ToString(CultureInfo.InvariantCulture) + " 0 R";

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);
}
