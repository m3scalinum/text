using System;
using System.Collections.Generic;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Copies the objects of one source file into a <see cref="PdfObjectWriter"/>, renumbering them.
/// </summary>
/// <remarks>
/// A reference is copied once: the map from source number to new number is filled before the object
/// it names is walked, so cycles (a page's <c>/Parent</c>, an outline's parent) terminate, and two
/// references to one source object share one new object instead of duplicating a font.
/// <para>
/// Pages are never copied implicitly. The caller reserves their numbers first with
/// <see cref="AssignPage"/> and fills them afterwards, so a page this operation keeps out of the
/// result can be recognised: a reference to it is written as <c>null</c> and
/// <see cref="Dangling"/> is raised, which lets the caller drop the annotation that carried it.
/// </para>
/// </remarks>
internal sealed class PdfObjectCopier(PdfFile source, PdfObjectWriter target)
{
    private readonly PdfFile _source = source;
    private readonly PdfObjectWriter _target = target;
    private readonly Dictionary<int, int> _numbers = [];
    private readonly HashSet<int> _excludedPages = [];
    private readonly HashSet<int> _copying = [];
    private readonly Dictionary<PdfValue, PdfReference> _substitutions = [];

    /// <summary>The deepest object chain a copy follows before giving up.</summary>
    private const int MaxDepth = 256;

    private int _depth;

    /// <summary>Whether a reference to a page the operation dropped was met while copying.</summary>
    public bool Dangling { get; private set; }

    /// <summary>The new number of a source object, when it has been copied or reserved.</summary>
    public int? Map(int sourceNumber)
        => _numbers.TryGetValue(sourceNumber, out var number) ? number : null;

    /// <summary>Reserves the new number of a page that the caller will write itself.</summary>
    public int AssignPage(int sourceNumber)
    {
        var number = _target.Reserve();
        _numbers[sourceNumber] = number;

        return number;
    }

    /// <summary>Records that a page is not part of the result.</summary>
    public void ExcludePage(int sourceNumber) => _excludedPages.Add(sourceNumber);

    /// <summary>Forgets a dangling reference seen while copying an optional object.</summary>
    public void ClearDangling() => Dangling = false;

    /// <summary>
    /// Registers a value that must be written as <paramref name="replacement"/> instead of being
    /// copied. It is how an object the caller built in the output -- a regenerated appearance --
    /// replaces its counterpart in the source graph.
    /// </summary>
    public void Substitute(PdfValue value, PdfReference replacement)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(replacement);

        _substitutions[value] = replacement;
    }

    /// <summary>Copies <paramref name="value"/>, resolving and renumbering every reference it holds.</summary>
    public PdfValue Copy(PdfValue? value)
    {
        if (value is null)
        {
            return PdfValue.Null;
        }

        if (++_depth > MaxDepth)
        {
            throw new PdfOperationException("the document's objects form a chain too long to copy");
        }

        try
        {
            if (_substitutions.TryGetValue(value, out var replacement))
            {
                return replacement;
            }

            return value switch
            {
                PdfReference reference => CopyReference(reference),
                PdfDictionary dictionary => CopyDictionary(dictionary),
                PdfStream stream => CopyStream(stream),
                PdfArray array => CopyArray(array),
                _ => value,
            };
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>Copies a dictionary, leaving out the keys named in <paramref name="skip"/>.</summary>
    public PdfDictionary CopyDictionary(PdfDictionary source, params string[] skip)
    {
        var dictionary = new PdfDictionary();
        var skipped = new HashSet<string>(skip, StringComparer.Ordinal);

        foreach (var (key, value) in source.Entries)
        {
            if (!skipped.Contains(key))
            {
                dictionary.Entries[key] = Copy(value);
            }
        }

        return dictionary;
    }

    /// <summary>Copies <paramref name="value"/> into an object number reserved by the caller.</summary>
    public void CopyInto(int targetNumber, PdfValue value) => _target.Set(targetNumber, Copy(value));

    private PdfValue CopyReference(PdfReference reference)
    {
        var number = reference.Number;

        if (_numbers.TryGetValue(number, out var existing))
        {
            return new PdfReference(existing);
        }

        if (_excludedPages.Contains(number))
        {
            Dangling = true;
            return PdfValue.Null;
        }

        var value = _source.GetObject(number);
        if (value is null or PdfNullValue)
        {
            return PdfValue.Null;
        }

        // An object stream or a cross-reference stream is a container the parser already exploded:
        // nothing in the copied graph may point at it, because the output has plain objects only.
        if (value is PdfStream stream && (stream.Dictionary.IsType("XRef") || stream.Dictionary.IsType("ObjStm")))
        {
            return PdfValue.Null;
        }

        var target = _target.Reserve();
        _numbers[number] = target;

        if (!_copying.Add(number))
        {
            return new PdfReference(target);
        }

        try
        {
            _target.Set(target, Copy(value));
        }
        finally
        {
            _copying.Remove(number);
        }

        return new PdfReference(target);
    }

    private PdfArray CopyArray(PdfArray source)
    {
        var array = new PdfArray();

        foreach (var item in source.Items)
        {
            array.Items.Add(Copy(item));
        }

        return array;
    }

    private PdfStream CopyStream(PdfStream source)
    {
        // The bytes stay encoded exactly as the producer wrote them: a filter this package does not
        // decode is still a filter a reader will. Only /Length changes, because the bytes are now
        // written as part of a different file.
        var dictionary = CopyDictionary(source.Dictionary, "Length");
        dictionary.Entries["Length"] = new PdfNumber(source.Data.Length);

        return new PdfStream(dictionary, source.Data);
    }
}
