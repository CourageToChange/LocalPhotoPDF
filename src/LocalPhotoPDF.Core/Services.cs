namespace LocalPhotoPDF.Core;

public interface IImageImportService
{
    IReadOnlySet<string> SupportedExtensions { get; }

    Task<ImportResult> ImportAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default);
}

public interface IPdfGenerationService
{
    Task GenerateAsync(
        PdfBuildRequest request,
        IProgress<PdfBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lifetime control for the imaging thread that decoding and rendering run on.
/// </summary>
public static class ImagingRuntime
{
    /// <summary>
    /// Shuts down the dedicated imaging thread. Call once as the application exits.
    /// </summary>
    /// <remarks>
    /// Optional: the thread is a background thread, so skipping this cannot keep the process
    /// alive. It exists so shutdown is deterministic rather than left to process teardown.
    /// </remarks>
    public static void Shutdown() => ImagingThread.ShutdownShared();
}

/// <summary>
/// The import safety limits, exposed so the UI can state them without repeating the numbers.
/// </summary>
/// <remarks>
/// The photo cap used to be written twice — once here and once as a private constant in the view
/// model. They agreed, but nothing made them agree, so changing one would have silently left the
/// UI reporting a different cap from the one actually enforced.
/// </remarks>
public static class ImportLimits
{
    /// <summary>Largest single photo file that will be imported.</summary>
    public static long MaximumFileSizeBytes => ImageDecoding.MaximumFileSizeBytes;

    /// <summary>Largest image, in pixels, that will be decoded.</summary>
    public static long MaximumPixelCount => ImageDecoding.MaximumPixelCount;

    /// <summary>Most photos a single PDF may contain.</summary>
    public static int MaximumPhotoCount => ImageDecoding.MaximumPhotoCount;
}
