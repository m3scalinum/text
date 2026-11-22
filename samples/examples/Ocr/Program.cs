using M3scalinum.Text.Ocr;

// Loads the native OCR library from the package. The package ships no model — the caller passes the
// weights, which keeps 40 MB out of the download — so this example proves the two things a consumer
// can check without them: the shared library loads on this runtime, and the ABI accepts a model
// call and answers with its own typed error instead of DllNotFoundException.
//
// Point M3SCALINUM_OCR_MODELS at a directory holding detection.rten and recognition.rten to run a
// real recognition on the image the same directory must then provide as page.png.

var models = Environment.GetEnvironmentVariable("M3SCALINUM_OCR_MODELS");

Console.WriteLine("M3scalinum.Text.Ocr");

if (!string.IsNullOrEmpty(models))
{
    var directory = new DirectoryInfo(models);
    var detection = File.ReadAllBytes(Path.Combine(directory.FullName, "detection.rten"));
    var recognition = File.ReadAllBytes(Path.Combine(directory.FullName, "recognition.rten"));
    var image = File.ReadAllBytes(Path.Combine(directory.FullName, "page.png"));

    using var engine = OcrEngine.FromModels(detection, recognition);
    var outcome = engine.Recognize(image);

    Console.WriteLine($"  models       {detection.Length} + {recognition.Length} bytes");

    // A page below a confidence threshold is refused rather than returned as approximate text,
    // so the caller has to say which of the two it got. That is the whole point of the split.
    if (outcome is not OcrResult result)
    {
        var refusal = ((OcrUnreadable)outcome).Refusal;
        Console.WriteLine($"  unreadable   {refusal.Message}");

        throw new InvalidOperationException(
            $"the engine refused the image ({refusal.Reason}): {refusal.Message}");
    }

    Console.WriteLine($"  image        {result.Width}x{result.Height}, {result.Lines.Count} lines,"
        + $" confidence {result.MeanConfidence:0.00}, {result.RejectedWordCount} word(s) flagged");
    Console.WriteLine($"  text         '{result.Text.Trim()}'");

    if (string.IsNullOrWhiteSpace(result.Text))
    {
        throw new InvalidOperationException("the engine read no text from the image");
    }

    return;
}

try
{
    using var rejected = OcrEngine.FromModels("not a model"u8.ToArray(), "not a model"u8.ToArray());

    throw new InvalidOperationException("the native library accepted bytes that are not a model");
}
catch (OcrException error)
{
    // The message comes from the native library: a package without its shared library never reaches
    // this catch, it fails earlier with DllNotFoundException.
    Console.WriteLine($"  native       loaded, model rejected with code '{error.Code}': {error.Message}");

    if (string.IsNullOrEmpty(error.Code) || error.Code == "unknown")
    {
        throw new InvalidOperationException("the native error carries no code, so the ABI did not run");
    }
}

Console.WriteLine();
Console.WriteLine("M3scalinum.Text.Ocr: the native library loaded from the package and answered on the ABI.");
Console.WriteLine("Set M3SCALINUM_OCR_MODELS to a directory with the models to run a real recognition.");
