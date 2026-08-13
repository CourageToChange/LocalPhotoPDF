using System.IO;
using System.Windows.Media;
using LocalPhotoPDF.Core;
using PdfSharp.Pdf.IO;

namespace LocalPhotoPDF.Tests;

public sealed class PdfGenerationServiceTests
{
    private const double A4Width = 595.275590551181;
    private const double A4Height = 841.889763779528;
    private static readonly int[] ExpectedProgressCounts = { 0, 1, 2 };
    private readonly PdfGenerationService _service = new();

    [Fact]
    public async Task GenerateAsync_CreatesPagesInInputOrder_WithAutomaticA4Orientation()
    {
        using var directory = new TestDirectory();
        var portrait = TestImages.Png(directory, "01-portrait.png", 120, 200, Colors.Red);
        var landscape = TestImages.Jpeg(directory, "02-landscape.jpg", 300, 100, Colors.Blue);
        var output = directory.File("ordered.pdf");
        var updates = new List<PdfBuildProgress>();

        await _service.GenerateAsync(
            new PdfBuildRequest(
                new[] { new PhotoSource(portrait), new PhotoSource(landscape) },
                output),
            new SynchronousProgress<PdfBuildProgress>(updates.Add),
            TestContext.Current.CancellationToken);

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
        AssertPageSize(document.Pages[0], A4Width, A4Height);
        AssertPageSize(document.Pages[1], A4Height, A4Width);
        Assert.Equal(ExpectedProgressCounts, updates.Select(update => update.CompletedPhotos));
        Assert.All(updates, update => Assert.Equal(2, update.TotalPhotos));
        Assert.Null(updates[0].CurrentFilePath);
        Assert.Equal(100, updates[^1].Percentage);
    }

    [Fact]
    public async Task GenerateAsync_MatchPhotoUsesTwelveInchLongestContentEdge_AndMargins()
    {
        using var directory = new TestDirectory();
        var landscape = TestImages.Png(directory, "landscape.png", 400, 200);
        var portrait = TestImages.Png(directory, "portrait.png", 100, 300);
        var output = directory.File("matched.pdf");
        var margin = 5d / 25.4d * 72;

        await _service.GenerateAsync(new PdfBuildRequest(
            new[] { new PhotoSource(landscape), new PhotoSource(portrait) },
            output,
            new PdfBuildOptions(PdfPageSize.MatchPhoto, PdfMargin.FiveMillimeters)),
            cancellationToken: TestContext.Current.CancellationToken);

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        AssertPageSize(document.Pages[0], 864 + 2 * margin, 432 + 2 * margin);
        AssertPageSize(document.Pages[1], 288 + 2 * margin, 864 + 2 * margin);
    }

    [Fact]
    public async Task GenerateAsync_ManualRotationControlsLetterOrientation()
    {
        using var directory = new TestDirectory();
        var portrait = TestImages.Png(directory, "portrait.png", 100, 200);
        var output = directory.File("rotated.pdf");

        await _service.GenerateAsync(new PdfBuildRequest(
            new[] { new PhotoSource(portrait, 90) },
            output,
            new PdfBuildOptions(PdfPageSize.Letter, PdfMargin.None, PdfQuality.Small)),
            cancellationToken: TestContext.Current.CancellationToken);

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        AssertPageSize(document.Pages[0], 792, 612);
    }

    [Fact]
    public async Task GenerateAsync_RunsWpfRenderingFromWorkerThread()
    {
        using var directory = new TestDirectory();
        var image = TestImages.Png(directory, "alpha.png", 60, 40, Color.FromArgb(80, 10, 120, 240));
        var output = directory.File("worker.pdf");

        await _service.GenerateAsync(
            new PdfBuildRequest(new[] { new PhotoSource(image) }, output),
            cancellationToken: TestContext.Current.CancellationToken);

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public async Task GenerateAsync_ReplacesExistingOutputOnlyAfterSuccessfulValidation()
    {
        using var directory = new TestDirectory();
        var image = TestImages.Png(directory, "photo.png", 60, 40);
        var output = directory.File("replace.pdf");
        await File.WriteAllBytesAsync(
            output,
            "OLD PDF"u8.ToArray(),
            TestContext.Current.CancellationToken);

        await _service.GenerateAsync(
            new PdfBuildRequest(new[] { new PhotoSource(image) }, output),
            cancellationToken: TestContext.Current.CancellationToken);

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Single(document.Pages);
        Assert.Empty(TemporaryFilesFor(output));
    }

    [Fact]
    public async Task GenerateAsync_SupportsLongOutputFilenameWithoutLengtheningTemporaryFilename()
    {
        using var directory = new TestDirectory();
        var image = TestImages.Png(directory, "photo.png", 60, 40);
        var output = directory.File($"{new string('a', 220)}.pdf");

        await _service.GenerateAsync(
            new PdfBuildRequest(new[] { new PhotoSource(image) }, output),
            cancellationToken: TestContext.Current.CancellationToken);

        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Single(document.Pages);
        Assert.Empty(TemporaryFilesFor(output));
    }

    [Fact]
    public async Task GenerateAsync_PreservesExistingOutputAndCleansTempWhenSourceFails()
    {
        using var directory = new TestDirectory();
        var corrupt = directory.File("broken.png");
        await File.WriteAllTextAsync(
            corrupt,
            "not an image",
            TestContext.Current.CancellationToken);
        var output = directory.File("safe.pdf");
        var original = "ORIGINAL CONTENT"u8.ToArray();
        await File.WriteAllBytesAsync(output, original, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _service.GenerateAsync(
                new PdfBuildRequest(new[] { new PhotoSource(corrupt) }, output),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken));
        Assert.Empty(TemporaryFilesFor(output));
    }

    [Fact]
    public async Task GenerateAsync_PreservesLockedExistingOutputAndCleansTemp()
    {
        using var directory = new TestDirectory();
        var image = TestImages.Png(directory, "photo.png", 60, 40);
        var output = directory.File("locked.pdf");
        var original = "LOCKED ORIGINAL"u8.ToArray();
        await File.WriteAllBytesAsync(output, original, TestContext.Current.CancellationToken);

        await using (var outputLock = new FileStream(
                         output,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(
                () => _service.GenerateAsync(
                    new PdfBuildRequest(new[] { new PhotoSource(image) }, output),
                    cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken));
        Assert.Empty(TemporaryFilesFor(output));
    }

    [Fact]
    public async Task GenerateAsync_CancellationPreservesExistingOutput()
    {
        using var directory = new TestDirectory();
        var first = TestImages.Png(directory, "first.png", 60, 40);
        var second = TestImages.Png(directory, "second.png", 60, 40);
        var output = directory.File("cancelled.pdf");
        var original = "UNCHANGED"u8.ToArray();
        await File.WriteAllBytesAsync(output, original, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var progress = new SynchronousProgress<PdfBuildProgress>(update =>
        {
            if (update.CompletedPhotos == 1)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.GenerateAsync(
                new PdfBuildRequest(
                    new[] { new PhotoSource(first), new PhotoSource(second) },
                    output),
                progress,
                cancellation.Token));

        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken));
        Assert.Empty(TemporaryFilesFor(output));
    }

    [Theory]
    [InlineData(45)]
    [InlineData(-1)]
    public async Task GenerateAsync_RejectsNonQuarterTurnRotation(int rotation)
    {
        using var directory = new TestDirectory();
        var image = TestImages.Png(directory, "photo.png", 60, 40);
        var output = directory.File("invalid.pdf");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateAsync(
                new PdfBuildRequest(new[] { new PhotoSource(image, rotation) }, output),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task GenerateAsync_RejectsDuplicateSources()
    {
        using var directory = new TestDirectory();
        var image = TestImages.Png(directory, "photo.png", 60, 40);
        var alternateSpelling = System.IO.Path.Combine(directory.Path, ".", "photo.png");
        var output = directory.File("duplicate.pdf");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateAsync(
                new PdfBuildRequest(
                    new[] { new PhotoSource(image), new PhotoSource(alternateSpelling) },
                    output),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(File.Exists(output));
    }

    private static void AssertPageSize(PdfSharp.Pdf.PdfPage page, double width, double height)
    {
        Assert.Equal(width, page.Width.Point, 1);
        Assert.Equal(height, page.Height.Point, 1);
    }

    private static string[] TemporaryFilesFor(string outputPath)
    {
        var directory = System.IO.Path.GetDirectoryName(outputPath)!;
        return Directory.GetFiles(directory, ".LocalPhotoPDF.*.tmp");
    }
}
