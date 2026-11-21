using System;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// What a password permits on an encrypted document, as the bits of the <c>/P</c> entry of the
/// standard security handler.
/// </summary>
/// <remarks>
/// The values are the specification's bit positions rather than a dense list, so a caller can print
/// the number that is written in the file. Bit 3 is the low bit of <see cref="Print"/> because the
/// specification counts the bits of <c>/P</c> from one; a value of <see cref="All"/> is what a
/// document that grants everything carries.
/// <para>
/// Permission bits are advisory: a reader that ignores them can always show the document, and a
/// document opened with the owner password grants every one of them. They are a statement of intent
/// that conforming readers honour, not a technical restriction.
/// </para>
/// </remarks>
[Flags]
public enum PdfPermissions
{
    /// <summary>Nothing is permitted beyond reading the document.</summary>
    None = 0,

    /// <summary>Bit 3: the document may be printed.</summary>
    Print = 1 << 2,

    /// <summary>Bit 4: the content may be changed.</summary>
    Modify = 1 << 3,

    /// <summary>Bit 5: text and graphics may be copied out of the document.</summary>
    Copy = 1 << 4,

    /// <summary>Bit 6: annotations and form fields may be added or changed.</summary>
    Annotate = 1 << 5,

    /// <summary>Bit 9: existing form fields may be filled and existing signatures applied.</summary>
    FillForms = 1 << 8,

    /// <summary>Bit 10: text may be extracted for accessibility, which no document should refuse.</summary>
    Accessibility = 1 << 9,

    /// <summary>Bit 11: the document may be assembled, by inserting, rotating or deleting pages.</summary>
    Assemble = 1 << 10,

    /// <summary>Bit 12: the document may be printed at a quality that reproduces it faithfully.</summary>
    HighQualityPrint = 1 << 11,

    /// <summary>Every permission the specification defines.</summary>
    All = Print | Modify | Copy | Annotate | FillForms | Accessibility | Assemble | HighQualityPrint,
}
