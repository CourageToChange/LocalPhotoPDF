using System.Windows.Media.Imaging;

namespace LocalPhotoPDF.Core;

public enum PdfPageSize
{
    A4,
    MatchPhoto,
    Letter,
}

public enum PdfMargin
{
    None,
    FiveMillimeters,
    TenMillimeters,
}

public enum PdfQuality
{
    High,
    Balanced,
    Small,
}

public sealed record PhotoSource(string FilePath, int RotationDegrees = 0);

public sealed record ImageInfo(
    string FilePath,
    string DisplayName,
    long FileSizeBytes,
    int PixelWidth,
    int PixelHeight,
    string Format,
    BitmapSource Thumbnail);

public sealed record ImportFailure(string FilePath, string Message);

public sealed record ImportResult(
    IReadOnlyList<ImageInfo> Images,
    IReadOnlyList<ImportFailure> Failures)
{
    public bool HasFailures => Failures.Count != 0;
}

public sealed record PdfBuildOptions(
    PdfPageSize PageSize = PdfPageSize.A4,
    PdfMargin Margin = PdfMargin.FiveMillimeters,
    PdfQuality Quality = PdfQuality.Balanced);

public sealed record PdfBuildRequest(
    IReadOnlyList<PhotoSource> Photos,
    string OutputPath,
    PdfBuildOptions Options)
{
    public PdfBuildRequest(IReadOnlyList<PhotoSource> photos, string outputPath)
        : this(photos, outputPath, new PdfBuildOptions())
    {
    }
}

public sealed record PdfBuildProgress(
    int CompletedPhotos,
    int TotalPhotos,
    string? CurrentFilePath)
{
    public double Percentage => TotalPhotos == 0
        ? 0
        : (double)CompletedPhotos / TotalPhotos * 100;
}
