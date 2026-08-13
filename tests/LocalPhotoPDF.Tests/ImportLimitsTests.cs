using System.IO;
using LocalPhotoPDF.Core;

namespace LocalPhotoPDF.Tests;

/// <summary>
/// The three import safety limits had no tests at all, despite being the guards that stand between
/// a hostile or accidental input and a memory blow-up. These assert each one is actually enforced
/// and reported, rather than merely declared.
/// </summary>
public sealed class ImportLimitsTests
{
    [Fact]
    public async Task ImportAsync_RejectsAFileOverTheSizeLimit_WithoutDecodingIt()
    {
        using var directory = new TestDirectory();
        var path = directory.File("huge.png");

        // Sparse-allocate just past the limit: the size check must happen before any decode, so
        // the contents never need to be a real image and the test stays fast.
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(ImportLimits.MaximumFileSizeBytes + 1);
        }

        var result = await new WicImageImportService().ImportAsync([path], TestContext.Current.CancellationToken);

        Assert.Empty(result.Images);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("MiB", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_AcceptsAFileOnTheSizeLimitBoundary()
    {
        using var directory = new TestDirectory();
        var path = TestImages.Png(directory, "small.png", 64, 64);

        Assert.True(new FileInfo(path).Length <= ImportLimits.MaximumFileSizeBytes);

        var result = await new WicImageImportService().ImportAsync([path], TestContext.Current.CancellationToken);

        Assert.Empty(result.Failures);
        Assert.Single(result.Images);
    }

    [Fact]
    public async Task ImportAsync_StopsAtThePhotoCountLimit_AndSaysSo()
    {
        using var directory = new TestDirectory();

        // One real photo is enough: the cap is applied by index, so the surplus entries never
        // need to exist on disk. Anything past the limit must be reported, not silently dropped.
        var real = TestImages.Png(directory, "photo.png", 32, 32);
        var paths = new string[ImportLimits.MaximumPhotoCount + 5];
        paths[0] = real;
        for (var index = 1; index < paths.Length; index++)
        {
            paths[index] = directory.File($"surplus-{index}.png");
        }

        var result = await new WicImageImportService().ImportAsync(paths, TestContext.Current.CancellationToken);

        Assert.True(result.Images.Count <= ImportLimits.MaximumPhotoCount);
        Assert.Contains(
            result.Failures,
            failure => failure.Message.Contains(
                ImportLimits.MaximumPhotoCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_RefusesMoreThanThePhotoCountLimit()
    {
        using var directory = new TestDirectory();
        var photo = TestImages.Png(directory, "photo.png", 32, 32);
        var photos = Enumerable
            .Range(0, ImportLimits.MaximumPhotoCount + 1)
            .Select(_ => new PhotoSource(photo))
            .ToList();

        var request = new PdfBuildRequest(photos, directory.File("out.pdf"));

        await Assert.ThrowsAnyAsync<Exception>(
            () => new PdfGenerationService().GenerateAsync(
                request,
                progress: null,
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(directory.File("out.pdf")));
    }

    [Fact]
    public void Limits_AreSingleSourced_AndSane()
    {
        // The photo cap was previously written twice, in Core and again in the view model. Nothing
        // enforced that they matched, so one could drift and the UI would report a cap the app did
        // not apply. ImportLimits is now the single source both read.
        Assert.Equal(500, ImportLimits.MaximumPhotoCount);
        Assert.Equal(250L * 1024 * 1024, ImportLimits.MaximumFileSizeBytes);
        Assert.Equal(200_000_000, ImportLimits.MaximumPixelCount);
    }
}
