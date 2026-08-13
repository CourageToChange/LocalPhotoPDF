using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace LocalPhotoPDF.Core;

public sealed class PdfGenerationService : IPdfGenerationService
{
    private const double PointsPerInch = 72;
    private const double RenderingDpi = 96;
    private const double MaximumPdfPagePoints = 14_400;
    private const double MatchPhotoLongestContentEdgePoints = 12 * PointsPerInch;

    public Task GenerateAsync(
        PdfBuildRequest request,
        IProgress<PdfBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Photos);

        var snapshot = request.Photos.ToArray();
        var stableRequest = request with { Photos = snapshot };
        // Runs on the shared imaging thread rather than the pool: RenderTargetBitmap attaches a
        // MediaContext to whatever thread's Dispatcher it finds. See ImagingThread.
        return ImagingThread.Shared.RunAsync<object?>(
            () =>
            {
                Generate(stableRequest, progress, cancellationToken);
                return null;
            },
            cancellationToken);
    }

    private static void Generate(
        PdfBuildRequest request,
        IProgress<PdfBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        var outputPath = Path.GetFullPath(request.OutputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath)
                              ?? throw new ArgumentException(
                                  "The output path must include a directory.",
                                  nameof(request));
        if (!Directory.Exists(outputDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The output directory does not exist: {outputDirectory}");
        }

        var temporaryPath = Path.Combine(
            outputDirectory,
            $".LocalPhotoPDF.{Guid.NewGuid():N}.tmp");
        var expectedPages = new List<PageGeometry>(request.Photos.Count);

        Exception? generationFailure = null;
        try
        {
            using (var document = new PdfDocument())
            {
                document.Info.Title = Path.GetFileNameWithoutExtension(outputPath);
                document.Info.Creator = "LocalPhotoPDF";

                progress?.Report(new PdfBuildProgress(0, request.Photos.Count, null));

                for (var index = 0; index < request.Photos.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var photo = request.Photos[index];
                    var geometry = AddPhotoPage(document, photo, request.Options, cancellationToken);
                    expectedPages.Add(geometry);

                    progress?.Report(new PdfBuildProgress(
                        index + 1,
                        request.Photos.Count,
                        photo.FilePath));
                }

                cancellationToken.ThrowIfCancellationRequested();
                SaveTemporaryDocument(document, temporaryPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ValidateTemporaryDocument(temporaryPath, expectedPages);
            cancellationToken.ThrowIfCancellationRequested();
            CommitAtomically(temporaryPath, outputPath);
        }
        catch (Exception exception)
        {
            generationFailure = exception;
        }

        var cleanupFailure = TryDeleteTemporaryFile(temporaryPath);
        if (cleanupFailure is not null)
        {
            var innerException = generationFailure is null
                ? cleanupFailure
                : new AggregateException(generationFailure, cleanupFailure);
            throw new PdfTemporaryFileCleanupException(temporaryPath, innerException);
        }

        if (generationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(generationFailure).Throw();
        }
    }

    private static PageGeometry AddPhotoPage(
        PdfDocument document,
        PhotoSource photo,
        PdfBuildOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var quality = GetQuality(options.Quality);
        var decoded = ImageDecoding.Decode(photo.FilePath, quality.MaximumLongEdge);
        var oriented = ImageDecoding.ApplyManualRotation(decoded.Bitmap, photo.RotationDegrees);
        var normalized = RenderOnWhite(oriented, quality.MaximumLongEdge);
        cancellationToken.ThrowIfCancellationRequested();

        var geometry = CalculatePageGeometry(
            options.PageSize,
            options.Margin,
            normalized.PixelWidth,
            normalized.PixelHeight);

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(geometry.WidthPoints);
        page.Height = XUnit.FromPoint(geometry.HeightPoints);

        using var jpegStream = EncodeJpeg(normalized, quality.JpegQuality);
        using var image = XImage.FromStream(jpegStream);
        using var graphics = XGraphics.FromPdfPage(page);

        var contentWidth = geometry.WidthPoints - 2 * geometry.MarginPoints;
        var contentHeight = geometry.HeightPoints - 2 * geometry.MarginPoints;
        var fitScale = Math.Min(
            contentWidth / normalized.PixelWidth,
            contentHeight / normalized.PixelHeight);
        var drawWidth = normalized.PixelWidth * fitScale;
        var drawHeight = normalized.PixelHeight * fitScale;
        var drawX = (geometry.WidthPoints - drawWidth) / 2;
        var drawY = (geometry.HeightPoints - drawHeight) / 2;

        graphics.DrawImage(image, drawX, drawY, drawWidth, drawHeight);
        return geometry;
    }

    private static RenderTargetBitmap RenderOnWhite(BitmapSource source, int maximumLongEdge)
    {
        var scale = Math.Min(1d, (double)maximumLongEdge / Math.Max(source.PixelWidth, source.PixelHeight));
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            drawing.DrawImage(source, new Rect(0, 0, width, height));
        }

        var rendered = new RenderTargetBitmap(
            width,
            height,
            RenderingDpi,
            RenderingDpi,
            PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static MemoryStream EncodeJpeg(BitmapSource bitmap, int jpegQuality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = jpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, null, null));

        var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return stream;
    }

    private static PageGeometry CalculatePageGeometry(
        PdfPageSize pageSize,
        PdfMargin margin,
        int imageWidth,
        int imageHeight)
    {
        var marginPoints = GetMarginPoints(margin);

        if (pageSize == PdfPageSize.MatchPhoto)
        {
            var contentScale = MatchPhotoLongestContentEdgePoints / Math.Max(imageWidth, imageHeight);
            var contentWidth = imageWidth * contentScale;
            var contentHeight = imageHeight * contentScale;
            var pageWidth = contentWidth + 2 * marginPoints;
            var pageHeight = contentHeight + 2 * marginPoints;
            var maximumDimension = Math.Max(pageWidth, pageHeight);

            if (maximumDimension > MaximumPdfPagePoints)
            {
                var availableMaximum = MaximumPdfPagePoints - 2 * marginPoints;
                var shrink = availableMaximum / Math.Max(contentWidth, contentHeight);
                contentWidth *= shrink;
                contentHeight *= shrink;
                pageWidth = contentWidth + 2 * marginPoints;
                pageHeight = contentHeight + 2 * marginPoints;
            }

            return new PageGeometry(pageWidth, pageHeight, marginPoints);
        }

        var (portraitWidth, portraitHeight) = pageSize switch
        {
            PdfPageSize.A4 => (595.275590551181d, 841.889763779528d),
            PdfPageSize.Letter => (612d, 792d),
            _ => throw new ArgumentOutOfRangeException(nameof(pageSize)),
        };

        return imageWidth > imageHeight
            ? new PageGeometry(portraitHeight, portraitWidth, marginPoints)
            : new PageGeometry(portraitWidth, portraitHeight, marginPoints);
    }

    private static QualitySettings GetQuality(PdfQuality quality) => quality switch
    {
        PdfQuality.High => new QualitySettings(6_000, 95),
        PdfQuality.Balanced => new QualitySettings(4_096, 92),
        PdfQuality.Small => new QualitySettings(2_048, 82),
        _ => throw new ArgumentOutOfRangeException(nameof(quality)),
    };

    private static double GetMarginPoints(PdfMargin margin) => margin switch
    {
        PdfMargin.None => 0,
        PdfMargin.FiveMillimeters => 5d / 25.4d * PointsPerInch,
        PdfMargin.TenMillimeters => 10d / 25.4d * PointsPerInch,
        _ => throw new ArgumentOutOfRangeException(nameof(margin)),
    };

    private static void ValidateRequest(PdfBuildRequest request)
    {
        if (request.Options is null)
        {
            throw new ArgumentException("PDF options are required.", nameof(request));
        }

        if (request.Photos.Count == 0)
        {
            throw new ArgumentException("Add at least one photo before creating a PDF.", nameof(request));
        }

        if (request.Photos.Count > ImageDecoding.MaximumPhotoCount)
        {
            throw new ArgumentException(
                $"A PDF can contain at most {ImageDecoding.MaximumPhotoCount} photos.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new ArgumentException("An output PDF path is required.", nameof(request));
        }

        if (!Path.GetExtension(request.OutputPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The output filename must end in .pdf.", nameof(request));
        }

        if (!Enum.IsDefined(request.Options.PageSize)
            || !Enum.IsDefined(request.Options.Margin)
            || !Enum.IsDefined(request.Options.Quality))
        {
            throw new ArgumentException("One or more PDF options are invalid.", nameof(request));
        }

        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var photo in request.Photos)
        {
            if (photo is null || string.IsNullOrWhiteSpace(photo.FilePath))
            {
                throw new ArgumentException("Every photo must have a file path.", nameof(request));
            }

            if (photo.RotationDegrees % 90 != 0)
            {
                throw new ArgumentException(
                    "Every photo rotation must be a multiple of 90 degrees.",
                    nameof(request));
            }

            var normalizedPath = Path.GetFullPath(photo.FilePath);
            if (!uniquePaths.Add(normalizedPath))
            {
                throw new ArgumentException("The photo list contains a duplicate file.", nameof(request));
            }
        }
    }

    private static void SaveTemporaryDocument(PdfDocument document, string temporaryPath)
    {
        using var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.SequentialScan);
        document.Save(stream, false);
        stream.Flush(true);
    }

    private static void ValidateTemporaryDocument(
        string temporaryPath,
        IReadOnlyList<PageGeometry> expectedPages)
    {
        using var document = PdfReader.Open(temporaryPath, PdfDocumentOpenMode.Import);
        if (document.PageCount != expectedPages.Count)
        {
            throw new InvalidDataException(
                $"PDF verification failed: expected {expectedPages.Count} pages, found {document.PageCount}.");
        }

        for (var index = 0; index < expectedPages.Count; index++)
        {
            var actual = document.Pages[index];
            var expected = expectedPages[index];
            if (Math.Abs(actual.Width.Point - expected.WidthPoints) > 0.1
                || Math.Abs(actual.Height.Point - expected.HeightPoints) > 0.1)
            {
                throw new InvalidDataException(
                    $"PDF verification failed: page {index + 1} has unexpected dimensions.");
            }
        }
    }

    private static void CommitAtomically(string temporaryPath, string outputPath)
    {
        if (File.Exists(outputPath))
        {
            File.Replace(temporaryPath, outputPath, null, true);
        }
        else
        {
            File.Move(temporaryPath, outputPath);
        }
    }

    private static Exception? TryDeleteTemporaryFile(string temporaryPath)
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                File.Delete(temporaryPath);
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastFailure = exception;
                if (attempt < 3)
                {
                    Thread.Sleep(50 * (attempt + 1));
                }
            }
        }

        return lastFailure;
    }

    private sealed record QualitySettings(int MaximumLongEdge, int JpegQuality);

    private sealed record PageGeometry(
        double WidthPoints,
        double HeightPoints,
        double MarginPoints);
}

public sealed class PdfTemporaryFileCleanupException : IOException
{
    internal PdfTemporaryFileCleanupException(string temporaryFilePath, Exception innerException)
        : base(
            $"Windows could not remove the temporary PDF at '{temporaryFilePath}'. " +
            "Delete that file manually because it may contain your photos.",
            innerException)
    {
        TemporaryFilePath = temporaryFilePath;
    }

    public string TemporaryFilePath { get; }
}
