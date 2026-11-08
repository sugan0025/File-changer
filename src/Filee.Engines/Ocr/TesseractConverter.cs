// Text recognition (OCR) with Tesseract (Apache-2.0, a separate program downloaded on demand into engines/tesseract;
// on Linux and macOS the one installed with the system's packages). "Recognize text" (PdfOptions.Ocr) turns a scan or
// a picture into a searchable PDF: the page as it looks, with the recognized words as an invisible text layer on top.
// The route planner puts this step first and goes on from the PDF, so DOCX, TXT, Markdown and HWPX get the text
// (PdfDocumentReader reads the layer when the preset asks for OCR).
//
// A PDF keeps its pages that already have text (born-digital pages are never turned into pictures); only pages
// without any are rendered at 300 dpi and recognized, page by page, at most four at a time. The language data comes from OcrLanguages,
// downloaded the first time a language is used, and is copied into the job's folder so Tesseract reads only it. A
// preset can name several languages (#1123): each one's data is there, and Tesseract gets them all (-l chi_sim+eng).

using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Infrastructure;
using Filee.Engines.Pdf;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

// PDFtoImage marks its API for the platforms PDFium ships for; Filee only targets desktop platforms, all covered.
#pragma warning disable CA1416

namespace Filee.Engines.Ocr;

/// <summary>Scans and pictures → searchable PDF through Tesseract.</summary>
public sealed class TesseractConverter : IConverter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>Scans are rendered at this resolution, which Tesseract reads best (and the page keeps its size).</summary>
    internal const int Dpi = 300;

    /// <summary>
    /// Tesseract runs side by side at most: enough to keep a desktop busy, few enough for memory and disk on a long scan
    /// (each run holds one 300 dpi page). A PC with fewer cores runs fewer.
    /// </summary>
    internal static int Runs => Math.Clamp(System.Environment.ProcessorCount, 1, 4);

    /// <summary>Pictures Tesseract opens itself (Leptonica); others become a PNG first (RoutePlanner).</summary>
    internal static readonly IReadOnlyList<string> ImageSources = ["png", "jpg", "tiff", "bmp", "webp", "gif"];

    /// <summary>
    /// One thread per Tesseract: Filee runs several pages at a time, and Tesseract's own threads would only compete
    /// (its documentation recommends this). TESSDATA_PREFIX of the system is not used, the job's folder is.
    /// </summary>
    private static readonly Dictionary<string, string> Environment = new()
    {
        ["OMP_THREAD_LIMIT"] = "1",
        ["TESSDATA_PREFIX"] = "",
    };

    private string? _executable;

    public string Id => "tesseract";
    public string DisplayName => "Tesseract OCR";

    /// <summary>One file at a time; its pages are recognized in parallel.</summary>
    public int MaxParallelism => 1;

    public IReadOnlyList<ConversionEdge> Edges { get; } =
        [.. ImageSources.Select(f => new ConversionEdge(f, "pdf", Ocr: true)), new("pdf", "pdf", Ocr: true)];

    public EngineStatus GetStatus()
    {
        _executable = Locate();
        return _executable is null ? EngineStatus.Unavailable("engine.reason.not_installed")
            : EngineStatus.Available(_executable, EngineEnvironment.UsesFileesCopy("tesseract") ? EngineVersions.Component("tesseract") : null);
    }

    /// <summary>The user's chosen copy, Filee's download, then one installed on the system.</summary>
    internal static string? Locate() =>
        EngineEnvironment.OwnProgram("tesseract", "tesseract.exe") ?? EngineEnvironment.BundledProgram("tesseract", "tesseract.exe")
        ?? EngineEnvironment.SystemProgram("tesseract", "tesseract.exe");

    public async Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var tesseract = _executable ?? Locate() ?? throw new InvalidOperationException("Tesseract is not installed.");
        var languages = OcrLanguages.Parse(step.Preset.Pdf.OcrLanguage);
        var data = await LanguageDataAsync(languages, OcrLanguages.EnsureAsync, cancellationToken);
        var scratch = Path.Combine(step.WorkDirectory, "ocr-" + Guid.NewGuid().ToString("N")[..8]);
        var tessdata = Path.Combine(scratch, "tessdata");
        Directory.CreateDirectory(tessdata);
        foreach (var file in data)
            File.Copy(file, Path.Combine(tessdata, Path.GetFileName(file)));
        var ocr = new Run(tesseract, scratch, OcrLanguages.Join(languages.Select(l => l.Code)));
        progress?.Report(0.05);

        var output = step.Output.Allocate("pdf");
        if (output is null)
            return [];
        if (step.From == "pdf")
        {
            await RecognizePdfAsync(step, scratch, output, Runs, (picture, page, token) => ocr.ToPdfAsync(picture, page, Dpi, token),
                progress, cancellationToken);
        }
        else
        {
            // A copy with a plain name: the picture's own folder and name may not be readable for Tesseract (Run).
            var picture = "picture" + Path.GetExtension(step.InputPath).ToLowerInvariant();
            File.Copy(step.InputPath, Path.Combine(scratch, picture));
            if (Enlarge(scratch, picture, step.From) is { } enlarged)
            {
                // Only the text comes from Tesseract; the page shows the picture as it was, not the enlarged copy.
                await ocr.ToPdfAsync(enlarged.Name, "page", enlarged.Declared ? null : Dpi, cancellationToken, textOnly: true);
                PutTextOnPicture(Path.Combine(scratch, "page.pdf"), enlarged.Original, enlarged.Width, enlarged.Height, output);
            }
            else
            {
                await ocr.ToPdfAsync(picture, "page", HasResolution(step.InputPath) ? null : Dpi, cancellationToken);
                File.Move(Path.Combine(scratch, "page.pdf"), output, overwrite: true);
            }
        }
        progress?.Report(1);
        return [output];
    }

    /// <summary>
    /// The data file of each language, one after the other as <paramref name="ensure"/> downloads what is missing
    /// (<see cref="OcrLanguages.EnsureAsync"/>). A download that fails refuses the file and says which language it was.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> LanguageDataAsync(IReadOnlyList<OcrLanguage> languages,
        Func<string, CancellationToken, Task<string>> ensure, CancellationToken cancellationToken)
    {
        var data = new List<string>();
        foreach (var language in languages)
        {
            try
            {
                data.Add(await ensure(language.Code, cancellationToken));
            }
            catch (HttpRequestException ex)
            {
                throw new ConversionRefusedException("error.ocr_language_download", $"{language.NativeName}: {ex.Message}");
            }
        }
        return data;
    }

    /// <summary>
    /// The pages without text become recognized scans, the others are copied. A PDF whose pages all have text is
    /// copied as it is, with a note. Also Apple Vision's way (<see cref="VisionConverter"/>).
    /// </summary>
    /// <param name="parallel">How many pages are recognized at the same time.</param>
    /// <param name="recognize">
    /// Reads the picture of a page (<c>p{n}.png</c> in <paramref name="scratch"/>, at <see cref="Dpi"/>) and writes it
    /// as a searchable one-page PDF (<c>p{n}.pdf</c> there); it gets both names relative to <paramref name="scratch"/>.
    /// </param>
    internal static async Task RecognizePdfAsync(ConversionStep step, string scratch, string output, int parallel,
        Func<string, string, CancellationToken, Task> recognize, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(step.InputPath, cancellationToken);
        var hasText = PagesWithText(step.InputPath);
        var pages = PageRange.Parse(step.Preset.Pdf.PageRange, hasText.Count);
        var scans = pages.Where(p => !hasText[p]).ToList();
        if (scans.Count == 0 && pages.Count == hasText.Count)
        {
            step.Notice?.Invoke("notice.ocr_has_text");
            File.Copy(step.InputPath, output, overwrite: true);
            return;
        }

        // Page by page: PDFium draws a page only when one of the Tesseract runs is free, and a page's picture is deleted
        // as soon as its PDF exists, so a 300-page scan never has more than Runs pictures in the work folder.
        var options = new RenderOptions(Dpi: Dpi, WithAnnotations: true, WithFormFill: true, BackgroundColor: SKColors.White);
        using var free = new SemaphoreSlim(parallel);
        var runs = new List<Task>();
        var done = 0;
        try
        {
            foreach (var page in scans)
            {
                await free.WaitAsync(cancellationToken);
                if (runs.Any(r => r.IsFaulted))
                {
                    free.Release();
                    break; // a page failed: stop rendering, the error comes out below
                }
                var picture = Path.Combine(scratch, $"p{page}.png");
                try
                {
                    using var bitmap = Conversion.ToImage(bytes, page, null, options);
                    await using var file = File.Create(picture);
                    bitmap.Encode(file, SKEncodedImageFormat.Png, 100);
                }
                catch
                {
                    free.Release();
                    throw;
                }
                runs.Add(Task.Run(async () =>
                {
                    try
                    {
                        await recognize($"p{page}.png", $"p{page}", cancellationToken);
                        File.Delete(picture);
                        progress?.Report(0.05 + 0.9 * Interlocked.Increment(ref done) / scans.Count);
                    }
                    finally
                    {
                        free.Release();
                    }
                }, cancellationToken));
            }
        }
        finally
        {
            // Rendering stopped (a failed page, a cancel): the running pages end before the work folder can go.
            await Task.WhenAll(runs).ContinueWith(_ => { }, TaskScheduler.Default);
        }
        await Task.WhenAll(runs);

        // A new document: the pages and the document information come along (not when the preset removes metadata),
        // bookmarks and form fields don't.
        using var source = PdfSharpConverter.Open(step.InputPath, PdfDocumentOpenMode.Import);
        using var result = new PdfDocument();
        if (!step.Preset.Pdf.RemoveMetadata)
        {
            result.Info.Title = source.Info.Title;
            result.Info.Author = source.Info.Author;
            result.Info.Subject = source.Info.Subject;
            result.Info.Keywords = source.Info.Keywords;
        }
        var recognized = new List<PdfDocument>();
        try
        {
            foreach (var page in pages)
            {
                if (hasText[page])
                {
                    result.AddPage(source.Pages[page]);
                    continue;
                }
                var scan = PdfReader.Open(Path.Combine(scratch, $"p{page}.pdf"), PdfDocumentOpenMode.Import);
                recognized.Add(scan);
                result.AddPage(scan.Pages[0]);
            }
            result.Save(output);
        }
        finally
        {
            recognized.ForEach(d => d.Dispose());
        }
    }

    /// <summary>
    /// For each page: true when it has text of its own. An OCR layer (invisible text) always counts. A scan with a
    /// stamp does not: a picture over most of the page whose only text is a little visible text (a page number or a
    /// Bates number the scanner software added, under <see cref="StampCoverage"/> of the page).
    /// </summary>
    internal static List<bool> PagesWithText(string path)
    {
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(path, new UglyToad.PdfPig.ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
            return [.. pdf.GetPages().Select(HasText)];
        }
        catch (PdfDocumentEncryptedException)
        {
            throw PdfDocumentReader.PasswordProtected();
        }
    }

    /// <summary>The share of a page that visible text may cover on a scan and still be only a stamp.</summary>
    internal const double StampCoverage = 0.01;

    private static bool HasText(UglyToad.PdfPig.Content.Page page)
    {
        var letters = page.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
        if (letters.Count == 0)
            return false;
        if (letters.Any(l => l.RenderingMode is TextRenderingMode.Neither or TextRenderingMode.NeitherClip))
            return true;
        var area = page.Width * page.Height;
        if (area <= 0 || letters.Sum(l => l.GlyphRectangle.Area) >= StampCoverage * area)
            return true;
        // A page drawn with little text and no picture under it (a cover, a short note) is not a scan.
        return !page.GetImages().Any(image => image.Bounds.Area >= area / 2);
    }

    /// <summary>
    /// True when the picture says how large it is meant to be. Without that Tesseract guesses 70 dpi (a page of a
    /// metre and more); 300 dpi is what scanners write.
    /// </summary>
    private static bool HasResolution(string path)
    {
        try
        {
            var info = new MagickImageInfo(path);
            return info.Density is { X: > 1 };
        }
        catch (MagickException)
        {
            return false;
        }
    }

    /// <summary>
    /// A picture whose short side is under this many pixels is enlarged before recognition. Tesseract reads text best
    /// when its letters are about 20–30 pixels tall (its guidance: accuracy drops below 10 pt at 300 dpi, rapidly below
    /// 8 pt). Screenshots and pictures saved from the web are made at screen resolution, where body text is 10–16
    /// pixels tall (#138: a 658 × 789 screenshot with Chinese characters a dozen pixels high). An A4 or Letter page
    /// scanned at 150 dpi or more is at least 1240 pixels on its short side, so a picture smaller than this is a
    /// screenshot, a crop, a small page or a scan below 120 dpi: small text, in nearly every case.
    /// </summary>
    internal const int SmallSide = 1000;

    /// <summary>Small pictures are read at twice their size: letters of 10–16 pixels become 20–32.</summary>
    internal const double EnlargeBy = 2;

    /// <summary>
    /// The enlarged copy has at most this many pixels: about 100 MB as Tesseract holds it (32 bits a pixel), a little
    /// more than an A3 page at 300 dpi (17 megapixels). A long, narrow screenshot is enlarged less, or not at all.
    /// </summary>
    internal const double MaxEnlargedPixels = 25_000_000;

    /// <summary>Enlarging by less than this changes too little to be worth a copy of the picture.</summary>
    internal const double LeastEnlargement = 1.25;

    /// <summary>
    /// How much a picture of <paramref name="width"/> × <paramref name="height"/> pixels is enlarged before Tesseract
    /// reads it; 1 for not at all. A picture that says it has 300 dpi or more (<paramref name="dpi"/>, null when it
    /// says none) is a scan at the resolution Tesseract asks for and is read as it is.
    /// </summary>
    internal static double EnlargeFactor(uint width, uint height, double? dpi)
    {
        if (width == 0 || height == 0 || Math.Min(width, height) >= SmallSide || dpi >= Dpi)
            return 1;
        var factor = Math.Min(EnlargeBy, Math.Sqrt(MaxEnlargedPixels / ((double)width * height)));
        return factor < LeastEnlargement ? 1 : factor;
    }

    /// <summary>
    /// Lowest resolution we trust from a file's metadata: 1 or 72 dpi on a screenshot that claims 1 dpi would make a
    /// page wider than the desktop.
    /// </summary>
    private const double LeastCredibleDpi = 72;

    private sealed record Enlarged(string Name, bool Declared, string Original, double Width, double Height);

    /// <summary>
    /// Writes <c>enlarged.png</c> in <paramref name="scratch"/>, enlarged through Catmull-Rom resampling so its letters
    /// reach the size Tesseract is trained for, when <paramref name="picture"/> is smaller than <see cref="SmallSide"/>
    /// and has no 300 dpi tag (<see cref="EnlargeFactor"/>). Also returns a readable copy of the original picture
    /// (<c>original.png</c> if it wasn't a JPEG or PNG already) and the page size in PDF points.
    /// </summary>
    private static Enlarged? Enlarge(string scratch, string picture, string format)
    {
        var path = Path.Combine(scratch, picture);
        try
        {
            if (format == "tiff" && MagickImageInfo.ReadCollection(path).Skip(1).Any())
                return null;
            using var image = new MagickImage();
            image.Ping(path);
            var declared = image.Density.X > 1 ? VisionConverter.Dpi(image) : (double?)null;
            var factor = EnlargeFactor(image.Width, image.Height, declared);
            if (factor == 1)
                return null;

            var dpi = Math.Max(declared ?? Dpi, LeastCredibleDpi);
            var (width, height) = (image.Width * 72.0 / dpi, image.Height * 72.0 / dpi);

            // The first frame, as Tesseract (Leptonica) reads it: not turned by EXIF, as Tesseract's own pages aren't.
            image.Read(path, new MagickReadSettings { FrameIndex = 0 });
            var original = format is "jpg" or "png" ? PdfPictures.Readable(path, scratch) : Path.Combine(scratch, "original.png");
            if (format is not ("jpg" or "png"))
                PdfPictures.WritePng(image, original);
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove); // see-through parts read as white paper, as Tesseract reads them
            // Catmull-Rom: sharp letter edges with little ringing for Tesseract's binarization to pick up.
            image.FilterType = FilterType.Catrom;
            image.Resize((uint)Math.Round(image.Width * factor), (uint)Math.Round(image.Height * factor));
            // The copy keeps the picture's resolution (or is read at 300 dpi), not twice it: Tesseract takes letters
            // of a few points for specks, so the copy told 600 dpi read worse than the picture itself (Tesseract 5.4).
            image.Write(Path.Combine(scratch, "enlarged.png"), MagickFormat.Png24);
            return new Enlarged("enlarged.png", declared is not null, original, width, height);
        }
        catch (MagickException)
        {
            return null; // Tesseract reads the picture as it is and says what is wrong with it
        }
    }

    /// <summary>
    /// Writes a page of <paramref name="width"/> × <paramref name="height"/> points that shows <paramref name="picture"/>
    /// with the invisible text of Tesseract's text-only page <paramref name="textPdf"/> (read from an enlarged copy, so
    /// a larger page) over it, shrunk to the same size, so each word lies over the same word of the picture.
    /// </summary>
    internal static void PutTextOnPicture(string textPdf, string picture, double width, double height, string output)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(width);
        page.Height = XUnit.FromPoint(height);
        using (var gfx = XGraphics.FromPdfPage(page))
        using (var image = XImage.FromFile(picture))
        using (var text = XPdfForm.FromFile(textPdf))
        {
            gfx.DrawImage(image, 0, 0, width, height);
            gfx.DrawImage(text, 0, 0, width, height);
        }
        document.Save(output);
    }

    /// <summary>
    /// One Tesseract reading <paramref name="Languages"/> (joined as its <c>-l</c> takes them), working in
    /// <paramref name="Folder"/> (the job's scratch folder, with their data in its <c>tessdata</c>). Tesseract only ever
    /// sees plain relative names there: some Windows builds of Tesseract and Leptonica open files through the ANSI code
    /// page and fail on a user name or folder in Korean or Cyrillic.
    /// </summary>
    private sealed record Run(string Executable, string Folder, string Languages)
    {
        /// <summary>
        /// Reads <paramref name="input"/> and writes <c>{outputBase}.pdf</c>, both names in <see cref="Folder"/>; with
        /// <paramref name="textOnly"/> the page has only the invisible text, not the picture (Tesseract 3.05 and later).
        /// </summary>
        public async Task ToPdfAsync(string input, string outputBase, int? dpi, CancellationToken cancellationToken, bool textOnly = false)
        {
            // tessedit_create_pdf instead of the "pdf" config file, which lives in Tesseract's own tessdata folder.
            List<string> args = [input, outputBase, "--tessdata-dir", "tessdata", "-l", Languages, "-c", "tessedit_create_pdf=1"];
            if (textOnly)
                args.AddRange(["-c", "textonly_pdf=1"]);
            if (dpi is { } value)
                args.AddRange(["--dpi", value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            var result = await ProcessRunner.RunAsync(Executable, args, Timeout, cancellationToken, workingDirectory: Folder, environment: Environment);
            if (result.ExitCode != 0 || !File.Exists(Path.Combine(Folder, outputBase + ".pdf")))
                throw new InvalidOperationException($"Tesseract failed (exit {result.ExitCode}). {result.StandardError.Trim()}");
        }
    }
}
