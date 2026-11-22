using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3scalinum.Text.Documents;

/// <summary>
/// The three block kinds the model labels and Typst numbers: sections, figures and tables.
/// </summary>
/// <remarks>
/// A kind names a Typst counter, so the number a reference prints comes from the element the label
/// sits on: <c>heading</c> for a section, <c>figure.where(kind: image)</c> for a figure and
/// <c>figure.where(kind: table)</c> for a table. The model never knows those numbers; it only emits
/// the label and the reference markup.
/// </remarks>
public enum TypstLabelKind
{
    /// <summary>A heading. Emitted labels are <c>sec:1</c>, <c>sec:2</c>, …</summary>
    Section,

    /// <summary>An image figure. Emitted labels are <c>fig:1</c>, <c>fig:2</c>, …</summary>
    Figure,

    /// <summary>A table figure. Emitted labels are <c>tbl:1</c>, <c>tbl:2</c>, …</summary>
    Table,
}

/// <summary>
/// Keeps the labels one <c>ToMarkup()</c> call emitted, so that a document never contains the same
/// label twice.
/// </summary>
/// <remarks>
/// Typst refuses a document in which a label occurs more than once
/// (<c>label `x` occurs multiple times in the document</c>), so the model indexes the repeats:
/// the first occurrence keeps the name it was given and every later one gets <c>-2</c>, <c>-3</c>, …
/// appended. A reference always names the first occurrence, which is the one the caller wrote.
/// </remarks>
internal sealed class TypstLabelRegistry
{
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    /// <summary>The prefix a kind's automatic labels use: <c>sec</c>, <c>fig</c> or <c>tbl</c>.</summary>
    public static string Prefix(TypstLabelKind kind) => kind switch
    {
        TypstLabelKind.Section => "sec",
        TypstLabelKind.Figure => "fig",
        TypstLabelKind.Table => "tbl",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown label kind"),
    };

    /// <summary>
    /// Returns the name this occurrence of <paramref name="requested"/> is emitted under: the name
    /// itself when it is free, otherwise <c>requested-2</c>, <c>requested-3</c>, …
    /// </summary>
    public string Index(string requested)
    {
        var candidate = requested;

        for (var suffix = 2; !_taken.Add(candidate); suffix++)
        {
            candidate = requested + "-" + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }
}
