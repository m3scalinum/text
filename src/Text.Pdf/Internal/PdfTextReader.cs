using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace M3scalinum.Text.Pdf;

/// <summary>
/// Reads the text a page draws, by walking its content streams with the text state they depend on.
/// </summary>
/// <remarks>
/// The walk is a small interpreter for the text-showing operators only: <c>BT</c>/<c>ET</c>,
/// <c>Tf</c>, <c>Td</c>/<c>TD</c>/<c>Tm</c>/<c>T*</c>, <c>Tj</c>/<c>TJ</c>/<c>'</c>/<c>"</c>, the
/// spacing operators they read, and <c>Do</c> for a form XObject. Everything that paints — paths,
/// images, colour, shadings — is skipped, because none of it changes a character.
/// <para>
/// The result is the glyphs in the order the content stream yields them. The positions are used for
/// one thing only: deciding where a word gap and a line break belong, so that two words drawn as
/// separate strings do not run together.
/// </para>
/// </remarks>
internal sealed class PdfTextReader
{
    /// <summary>How deep a form XObject may nest its own <c>Do</c> operators.</summary>
    private const int MaxFormDepth = 8;

    private readonly PdfFile _file;
    private readonly StringBuilder _text = new();
    private readonly List<string> _fallbacks = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly HashSet<PdfStream> _openForms = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfDictionary, PdfTextFont> _fonts = new(ReferenceEqualityComparer.Instance);

    private int _skippedStreams;
    private bool _started;
    private double _lastX;
    private double _lastY;
    private double _lastStartX;

    private PdfTextReader(PdfFile file) => _file = file;

    /// <summary>Reads page <paramref name="page"/> (zero-based) of <paramref name="pdf"/>.</summary>
    /// <param name="pdf">The document, as its whole file.</param>
    /// <param name="page">Zero-based page index.</param>
    /// <param name="password">The password that opens an encrypted document, or <see langword="null"/>.</param>
    /// <exception cref="PdfTextException">The bytes are not a PDF, the document is encrypted, or its catalog cannot be read.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> names a page the document does not have.</exception>
    public static PdfPageText ReadPage(ReadOnlySpan<byte> pdf, int page, string? password = null)
    {
        var reader = Open(pdf.ToArray(), password);
        var pages = reader._file.Pages();

        if (page < 0 || page >= pages.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                $"the document has {pages.Count} page(s), so page {page} cannot be read");
        }

        return reader.ReadPage(pages[page], page + 1);
    }

    private static PdfTextReader Open(byte[] pdf, string? password)
    {
        if (!PdfFile.TryOpen(pdf, out var file) || file is null)
        {
            throw new PdfTextException(
                PdfTextError.NotAPdf,
                "the bytes are not a PDF, so there is no text to read");
        }

        if (file.IsEncrypted)
        {
            file = Decrypt(pdf, file, password);
        }

        if (file.Catalog is null)
        {
            throw new PdfTextException(
                PdfTextError.Unreadable,
                "the document has no readable catalog, so its pages cannot be found");
        }

        return new PdfTextReader(file);
    }

    /// <summary>
    /// Opens an encrypted document with <paramref name="password"/> and returns the decrypted file,
    /// so the walk below reads plaintext and never needs to know the document was protected.
    /// </summary>
    /// <param name="pdf">The encrypted document.</param>
    /// <param name="file">The encrypted file, already parsed, whose handler names the reason for a refusal.</param>
    /// <param name="password">The password supplied by the caller, or <see langword="null"/> when none was.</param>
    private static PdfFile Decrypt(byte[] pdf, PdfFile file, string? password)
    {
        if (password is null)
        {
            throw new PdfTextException(
                PdfTextError.Encrypted,
                $"{EncryptedReason(file)}; pass its password to read its text");
        }

        if (!SupportsDecryption(file))
        {
            throw new PdfTextException(
                PdfTextError.Encrypted,
                $"{EncryptedReason(file)}; this library does not decrypt that security handler, so its text cannot be read");
        }

        byte[] decrypted;

        try
        {
            decrypted = PdfEncryption.Open(pdf, password).Pdf;
        }
        catch (PdfEncryptionException)
        {
            throw new PdfTextException(
                PdfTextError.WrongPassword,
                "the password does not open the document, so its text cannot be read");
        }

        // The bytes PdfEncryption.Open returned are a rebuilt document, so they parse like any other;
        // a failure here would mean the decryption produced something that is not a PDF at all.
        if (!PdfFile.TryOpen(decrypted, out var opened) || opened is null)
        {
            throw new PdfTextException(
                PdfTextError.Unreadable,
                "the document decrypted, but the bytes it produced are not a readable PDF");
        }

        return opened;
    }

    /// <summary>
    /// Whether <paramref name="file"/> declares a security handler <see cref="PdfEncryption.Open"/>
    /// implements, so that a failure to open it is the password's fault rather than the cipher's.
    /// </summary>
    /// <param name="file">The encrypted document.</param>
    private static bool SupportsDecryption(PdfFile file)
    {
        var encrypt = file.ResolveDictionary(file.Trailer["Encrypt"]);

        if (encrypt is null)
        {
            return false;
        }

        var filter = (file.Resolve(encrypt["Filter"]) as PdfName)?.Value;
        var version = (file.Resolve(encrypt["V"]) as PdfNumber)?.Value;
        var revision = (file.Resolve(encrypt["R"]) as PdfNumber)?.Value;

        return string.Equals(filter, "Standard", StringComparison.Ordinal)
            && ((version == 5 && revision == 6) || (version == 4 && revision == 4));
    }

    private static string EncryptedReason(PdfFile file)
    {
        var encrypt = file.ResolveDictionary(file.Trailer["Encrypt"]);
        var filter = (encrypt is null ? null : file.Resolve(encrypt["Filter"]) as PdfName)?.Value;
        var handler = string.IsNullOrEmpty(filter) ? "an unnamed security handler" : $"/{filter}";
        var version = encrypt is null ? null : file.Resolve(encrypt["V"]) as PdfNumber;

        var detail = version is null ? handler : $"{handler}, revision {version.Value}";
        return $"the document is encrypted ({detail})";
    }

    private PdfTextResult Run()
    {
        var pages = _file.Pages();
        var texts = new List<PdfPageText>(pages.Count);

        for (var index = 0; index < pages.Count; index++)
        {
            texts.Add(ReadPage(pages[index], index + 1));
        }

        return new PdfTextResult(texts);
    }

    /// <summary>Reads the text of every page.</summary>
    /// <param name="pdf">The document.</param>
    /// <param name="password">The password that opens an encrypted document, or <see langword="null"/>.</param>
    public static PdfTextResult Extract(ReadOnlySpan<byte> pdf, string? password = null)
        => Open(pdf.ToArray(), password).Run();

    private PdfPageText ReadPage(PdfPage page, int number)
    {
        _text.Clear();
        _fallbacks.Clear();
        _reported.Clear();
        _openForms.Clear();
        _skippedStreams = 0;
        _started = false;

        if (Contents(page) is { } content)
        {
            Walk(content, page.Resources, 0);
        }

        return new PdfPageText(number, Finish(), [.. _fallbacks], _skippedStreams);
    }

    /// <summary>
    /// The page's content streams, decoded and joined with a newline. The array form is one logical
    /// stream split for the writer's convenience, so the text state crosses the boundary; a
    /// whitespace byte between the pieces keeps two operators from fusing.
    /// </summary>
    private byte[]? Contents(PdfPage page)
    {
        var value = page.Dictionary["Contents"];
        var resolved = _file.Resolve(value);
        IReadOnlyList<PdfValue> items = resolved is PdfArray array
            ? array.Items
            : [value ?? PdfValue.Null];

        using var buffer = new MemoryStream();
        var decoded = false;

        foreach (var item in items)
        {
            var stream = _file.ResolveStream(item);
            if (stream is null)
            {
                continue;
            }

            var data = _file.Decode(stream);
            if (data is null)
            {
                // A filter this reader does not implement: the page's text is incomplete, and the
                // count says so rather than pretending the page was empty.
                _skippedStreams++;
                continue;
            }

            buffer.Write(data, 0, data.Length);
            buffer.WriteByte((byte)'\n');
            decoded = true;
        }

        return decoded ? buffer.ToArray() : null;
    }

    private void Walk(byte[] content, PdfDictionary? resources, int depth)
    {
        if (depth > MaxFormDepth)
        {
            return;
        }

        var fontResources = _file.ResolveDictionary(resources is null ? null : resources["Font"]);
        var xObjects = _file.ResolveDictionary(resources is null ? null : resources["XObject"]);

        var state = new TextState();
        var saved = new Stack<SavedState>();
        var ctm = Matrix.Identity;
        var matrix = Matrix.Identity;
        var line = Matrix.Identity;
        var operands = new List<PdfValue>();
        var position = 0;

        while (PdfFile.TryReadContentToken(content, ref position, out var operand, out var keyword))
        {
            if (keyword is null)
            {
                operands.Add(operand ?? PdfValue.Null);
                continue;
            }

            switch (keyword)
            {
                case "BT":
                    matrix = Matrix.Identity;
                    line = Matrix.Identity;
                    break;

                case "Tf":
                    state.Font = Font(fontResources, NameAt(operands, 0));
                    state.FontSize = NumberAt(operands, 1, state.FontSize);
                    break;

                case "Tc":
                    state.CharSpacing = NumberAt(operands, 0, state.CharSpacing);
                    break;

                case "Tw":
                    state.WordSpacing = NumberAt(operands, 0, state.WordSpacing);
                    break;

                case "Tz":
                    state.HorizontalScale = NumberAt(operands, 0, 100) / 100;
                    break;

                case "TL":
                    state.Leading = NumberAt(operands, 0, state.Leading);
                    break;

                case "Ts":
                    state.Rise = NumberAt(operands, 0, 0);
                    break;

                case "Td":
                    line = line.Translate(NumberAt(operands, 0, 0), NumberAt(operands, 1, 0));
                    matrix = line;
                    break;

                case "TD":
                    state.Leading = -NumberAt(operands, 1, 0);
                    line = line.Translate(NumberAt(operands, 0, 0), NumberAt(operands, 1, 0));
                    matrix = line;
                    break;

                case "Tm":
                    matrix = line = new Matrix(
                        NumberAt(operands, 0, 1),
                        NumberAt(operands, 1, 0),
                        NumberAt(operands, 2, 0),
                        NumberAt(operands, 3, 1),
                        NumberAt(operands, 4, 0),
                        NumberAt(operands, 5, 0));
                    break;

                case "T*":
                    line = line.Translate(0, -state.Leading);
                    matrix = line;
                    break;

                case "Tj":
                    Show(StringAt(operands, 0), state, ctm, ref matrix);
                    break;

                case "'":
                    line = line.Translate(0, -state.Leading);
                    matrix = line;
                    Show(StringAt(operands, 0), state, ctm, ref matrix);
                    break;

                case "\"":
                    state.WordSpacing = NumberAt(operands, 0, state.WordSpacing);
                    state.CharSpacing = NumberAt(operands, 1, state.CharSpacing);
                    line = line.Translate(0, -state.Leading);
                    matrix = line;
                    Show(StringAt(operands, 2), state, ctm, ref matrix);
                    break;

                case "TJ":
                    ShowArray(operands, state, ctm, ref matrix);
                    break;

                case "q":
                    saved.Push(new SavedState(state, ctm));
                    break;

                case "Q":
                    if (saved.Count > 0)
                    {
                        var savedState = saved.Pop();
                        state = savedState.Text;
                        ctm = savedState.Transform;
                    }

                    break;

                case "cm":
                    ctm = new Matrix(
                        NumberAt(operands, 0, 1),
                        NumberAt(operands, 1, 0),
                        NumberAt(operands, 2, 0),
                        NumberAt(operands, 3, 1),
                        NumberAt(operands, 4, 0),
                        NumberAt(operands, 5, 0)).Multiply(ctm);
                    break;

                case "Do":
                    Form(NameAt(operands, 0), xObjects, resources, depth);
                    break;

                case "BI":
                    // An inline image's bytes are not content-stream tokens; skip them as a block.
                    position = SkipInlineImage(content, position);
                    break;
            }

            operands.Clear();
        }
    }

    /// <summary>Draws a form XObject's text where the <c>Do</c> appears.</summary>
    private void Form(string? name, PdfDictionary? xObjects, PdfDictionary? pageResources, int depth)
    {
        if (name is null || xObjects is null)
        {
            return;
        }

        var stream = _file.ResolveStream(xObjects[name]);
        if (stream is null || (stream.Dictionary["Subtype"] is PdfName subtype && subtype.Value != "Form"))
        {
            return;
        }

        // A form may draw itself; the depth limit and this path guard both stop the walk. The form
        // is removed afterwards so a page that draws it twice keeps both copies of its text.
        if (!_openForms.Add(stream))
        {
            return;
        }

        try
        {
            var data = _file.Decode(stream);
            if (data is null)
            {
                _skippedStreams++;
                return;
            }

            // A form carries its own coordinate system, so its text is a run of its own: comparing
            // its positions with the page's would invent gaps that are not on the page.
            NewLine();
            _started = false;

            var formResources = _file.ResolveDictionary(stream.Dictionary["Resources"]) ?? pageResources;
            Walk(data, formResources, depth + 1);

            NewLine();
            _started = false;
        }
        finally
        {
            _openForms.Remove(stream);
        }
    }

    private void ShowArray(List<PdfValue> operands, in TextState state, in Matrix ctm, ref Matrix matrix)
    {
        if (operands.Count == 0 || operands[0] is not PdfArray array)
        {
            return;
        }

        foreach (var operand in array.Items)
        {
            switch (operand)
            {
                case PdfString text:
                    Show(text, state, ctm, ref matrix);
                    break;

                case PdfNumber adjustment:
                    // A TJ number moves the pen without drawing: negative moves it right, which is a
                    // gap between glyphs and may be the space between two words.
                    matrix = matrix.Translate(
                        -adjustment.Value / 1000 * state.FontSize * state.HorizontalScale,
                        0);
                    break;
            }
        }
    }

    private void Show(PdfString? text, in TextState state, in Matrix ctm, ref Matrix matrix)
    {
        if (text is null || state.Font is null || text.Bytes.Length == 0)
        {
            return;
        }

        var glyphs = state.Font.Decode(text.Bytes);
        if (glyphs.Count == 0)
        {
            return;
        }

        if (!state.Font.HasUnicodeMap && _reported.Add(state.Font.Name))
        {
            _fallbacks.Add(state.Font.Name);
        }

        Place(matrix, state, ctm);

        foreach (var glyph in glyphs)
        {
            Append(glyph.Text);

            var advance = ((glyph.Width / 1000 * state.FontSize)
                + state.CharSpacing
                + (glyph.IsSpace ? state.WordSpacing : 0)) * state.HorizontalScale;

            matrix = matrix.Translate(advance, 0);
            _lastX = ctm.E + (matrix.E * ctm.A) + (matrix.F * ctm.C);
            _lastY = ctm.F + (matrix.E * ctm.B) + (matrix.F * ctm.D);
        }
    }

    /// <summary>
    /// Puts the separator the layout calls for before a shown string: a newline when the baseline
    /// moved, a space when the pen jumped forward, nothing when the string continues a run.
    /// </summary>
    private void Place(in Matrix matrix, in TextState state, in Matrix ctm)
    {
        var textE = matrix.E + (state.Rise * matrix.C);
        var textF = matrix.F + (state.Rise * matrix.D);
        var startX = ctm.E + (textE * ctm.A) + (textF * ctm.C);
        var startY = ctm.F + (textE * ctm.B) + (textF * ctm.D);
        var size = Math.Max(Math.Abs(state.FontSize), 1.0);

        if (_started)
        {
            if (Math.Abs(startY - _lastY) > Math.Max(size * 0.4, 1.0))
            {
                NewLine();
            }
            else if (startX < _lastStartX - Math.Max(size * 0.5, 1.0))
            {
                NewLine();
            }
            else if (startX - _lastX > Math.Max(size * 0.15, 0.5))
            {
                Space();
            }
        }

        _lastStartX = startX;
        _lastY = startY;
        _started = true;
    }

    private void Append(string piece)
    {
        if (piece.Length == 0)
        {
            return;
        }

        var start = 0;
        if (_text.Length > 0 && _text[^1] is ' ' or '\n')
        {
            while (start < piece.Length && piece[start] == ' ')
            {
                start++;
            }
        }

        _text.Append(piece, start, piece.Length - start);
    }

    private void Space()
    {
        if (_text.Length == 0 || _text[^1] is ' ' or '\n')
        {
            return;
        }

        _text.Append(' ');
    }

    private void NewLine()
    {
        while (_text.Length > 0 && _text[^1] == ' ')
        {
            _text.Length--;
        }

        if (_text.Length > 0 && _text[^1] != '\n')
        {
            _text.Append('\n');
        }
    }

    /// <summary>The page's text, with one trailing newline when it has any, as the compiler's page text has.</summary>
    private string Finish()
    {
        var length = _text.Length;
        while (length > 0 && _text[length - 1] is ' ' or '\n' or '\r' or '\t')
        {
            length--;
        }

        return length == 0 ? string.Empty : string.Concat(_text.ToString(0, length), "\n");
    }

    /// <summary>The font of a <c>Tf</c> name, built once per font dictionary and cached.</summary>
    private PdfTextFont? Font(PdfDictionary? fonts, string? name)
    {
        if (fonts is null || name is null)
        {
            return null;
        }

        var dictionary = _file.ResolveDictionary(fonts[name]);
        if (dictionary is null)
        {
            return null;
        }

        if (_fonts.TryGetValue(dictionary, out var cached))
        {
            return cached;
        }

        var font = PdfTextFont.Create(_file, name, dictionary);
        _fonts[dictionary] = font;

        return font;
    }

    /// <summary>Skips an inline image's bytes, which begin after <c>ID</c> and end before <c>EI</c>.</summary>
    private static int SkipInlineImage(byte[] content, int position)
    {
        var id = IndexOf(content, "ID"u8, position);
        if (id < 0)
        {
            return content.Length;
        }

        var start = id + 2;
        if (start < content.Length && content[start] is 0 or 9 or 10 or 12 or 13 or 32)
        {
            start++;
        }

        for (var index = start; index + 1 < content.Length; index++)
        {
            if (content[index] == (byte)'E' && content[index + 1] == (byte)'I' &&
                index > start && content[index - 1] is 0 or 9 or 10 or 12 or 13 or 32 &&
                (index + 2 >= content.Length || content[index + 2] is 0 or 9 or 10 or 12 or 13 or 32))
            {
                return index + 2;
            }
        }

        return content.Length;
    }

    private static int IndexOf(byte[] content, ReadOnlySpan<byte> keyword, int from)
    {
        var found = content.AsSpan(from).IndexOf(keyword);
        return found < 0 ? -1 : from + found;
    }

    private static string? NameAt(List<PdfValue> operands, int index)
        => operands.Count > index && operands[index] is PdfName name ? name.Value : null;

    private static double NumberAt(List<PdfValue> operands, int index, double fallback)
        => operands.Count > index && operands[index] is PdfNumber number ? number.Value : fallback;

    private static PdfString? StringAt(List<PdfValue> operands, int index)
        => operands.Count > index ? operands[index] as PdfString : null;

    /// <summary>What <c>q</c> pushes and <c>Q</c> restores: the text state and the transform in force.</summary>
    private readonly record struct SavedState(TextState Text, Matrix Transform);

    /// <summary>The text state the text operators change; the graphics state saves and restores it.</summary>
    private struct TextState
    {
        public PdfTextFont? Font;
        public double FontSize = 0;
        public double CharSpacing = 0;
        public double WordSpacing = 0;
        public double HorizontalScale = 1;
        public double Leading = 0;
        public double Rise = 0;

        public TextState()
        {
        }
    }

    /// <summary>A text matrix: how a point in text space lands in the page's user space.</summary>
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);

        /// <summary>This matrix translated by <paramref name="x"/> and <paramref name="y"/> in text space.</summary>
        public Matrix Translate(double x, double y)
            => new(A, B, C, D, (x * A) + (y * C) + E, (x * B) + (y * D) + F);

        /// <summary>This matrix applied before <paramref name="outer"/>, as PDF transforms compose.</summary>
        public Matrix Multiply(in Matrix outer) => new(
            (A * outer.A) + (B * outer.C),
            (A * outer.B) + (B * outer.D),
            (C * outer.A) + (D * outer.C),
            (C * outer.B) + (D * outer.D),
            (E * outer.A) + (F * outer.C) + outer.E,
            (E * outer.B) + (F * outer.D) + outer.F);
    }
}
