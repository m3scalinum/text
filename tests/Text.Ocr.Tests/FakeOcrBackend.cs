using System;
using System.Collections.Generic;
using M3scalinum.Text.Ocr.Internal;

namespace M3scalinum.Text.Ocr.Tests;

/// <summary>
/// A recognition engine with no model behind it.
/// </summary>
/// <remarks>
/// The real engine needs two model files that weigh tens of megabytes and that this
/// repository does not ship, so every promise of the public surface is asserted
/// against this double instead. It returns whatever it was told to return, and
/// records what the engine passed it.
/// </remarks>
internal sealed class FakeOcrBackend : IOcrBackend, IDisposable
{
    internal FakeOcrBackend()
    {
        Result = new OcrResult(
            "first line\nsecond line",
            [
                new OcrLine("first line", new OcrBox(10, 20, 120, 18), 0.94,
                    [new OcrWord("first", new OcrBox(10, 20, 50, 18), 0.95),
                     new OcrWord("line", new OcrBox(70, 20, 60, 18), 0.93)]),
                new OcrLine("second line", new OcrBox(10, 44, 130, 18), 0.88,
                    [new OcrWord("second", new OcrBox(10, 44, 70, 18), 0.9),
                     new OcrWord("line", new OcrBox(90, 44, 50, 18), 0.86)]),
            ],
            0.91,
            2480,
            3508);

        Pages =
        [
            new OcrPageResult(0, "first line", [Result.Lines[0]], 0.94, 2480, 3508, 300),
            new OcrPageResult(1, "second line", [Result.Lines[1]], 0.88, 2480, 3508, 300),
        ];
    }

    internal OcrResult Result { get; set; }

    internal IReadOnlyList<OcrPageResult> Pages { get; set; }

    internal int RecognizeCalls { get; private set; }

    internal int RecognizePdfCalls { get; private set; }

    internal byte[]? LastImage { get; private set; }

    internal byte[]? LastPdf { get; private set; }

    internal OcrPdfOptions? LastOptions { get; private set; }

    internal bool Disposed { get; private set; }

    public OcrResult Recognize(byte[] image)
    {
        RecognizeCalls++;
        LastImage = image;
        return Result;
    }

    public IReadOnlyList<OcrPageResult> RecognizePdf(byte[] pdf, OcrPdfOptions options)
    {
        RecognizePdfCalls++;
        LastPdf = pdf;
        LastOptions = options;
        return Pages;
    }

    public void Dispose() => Disposed = true;
}
