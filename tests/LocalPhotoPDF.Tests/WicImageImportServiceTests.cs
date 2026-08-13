using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalPhotoPDF.Core;

namespace LocalPhotoPDF.Tests;

public sealed class WicImageImportServiceTests
{
    private readonly WicImageImportService _service = new();

    [Fact]
    public async Task ImportAsync_AcceptsNaturalCaseInsensitiveExtensions_AndFreezesThumbnail()
    {
        using var directory = new TestDirectory();
        var jpeg = TestImages.Jpeg(directory, "holiday.JPEG", 320, 180);
        var png = TestImages.Png(directory, "diagram.PnG", 100, 200);

        var result = await _service.ImportAsync(
            new[] { jpeg, png },
            TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        Assert.Collection(
            result.Images,
            first =>
            {
                Assert.Equal("holiday.JPEG", first.DisplayName);
                Assert.Equal("JPEG", first.Format);
                Assert.Equal(320, first.PixelWidth);
                Assert.Equal(180, first.PixelHeight);
                Assert.True(first.FileSizeBytes > 0);
                Assert.True(first.Thumbnail.IsFrozen);
                Assert.IsType<RenderTargetBitmap>(first.Thumbnail);
                Assert.Equal(192, first.Thumbnail.PixelWidth);
            },
            second =>
            {
                Assert.Equal("PNG", second.Format);
                Assert.Equal(100, second.PixelWidth);
                Assert.Equal(200, second.PixelHeight);
                Assert.True(second.Thumbnail.IsFrozen);
                Assert.IsType<RenderTargetBitmap>(second.Thumbnail);
                Assert.Equal(192, second.Thumbnail.PixelHeight);
            });
    }

    [Fact]
    public async Task ImportAsync_UsesOnlyFirstFrame()
    {
        using var directory = new TestDirectory();
        var tiff = TestImages.MultiFrameTiff(directory, "scan.tiff");

        var result = await _service.ImportAsync(
            new[] { tiff },
            TestContext.Current.CancellationToken);

        var image = Assert.Single(result.Images);
        Assert.Equal(40, image.PixelWidth);
        Assert.Equal(20, image.PixelHeight);
        Assert.Equal("TIFF", image.Format);
        Assert.Empty(result.Failures);
    }

    [Theory]
    [InlineData((ushort)5)]
    [InlineData((ushort)6)]
    [InlineData((ushort)7)]
    [InlineData((ushort)8)]
    public async Task ImportAsync_AppliesExifOrientationsWithoutCropping(ushort orientation)
    {
        using var directory = new TestDirectory();
        var jpeg = TestImages.Jpeg(
            directory,
            $"orientation-{orientation}.jpg",
            80,
            40,
            Colors.OrangeRed,
            orientation);

        var result = await _service.ImportAsync(
            new[] { jpeg },
            TestContext.Current.CancellationToken);

        var image = Assert.Single(result.Images);
        Assert.Equal(40, image.PixelWidth);
        Assert.Equal(80, image.PixelHeight);
        Assert.Equal(40, image.Thumbnail.PixelWidth);
        Assert.Equal(80, image.Thumbnail.PixelHeight);

        var stride = image.Thumbnail.PixelWidth * 4;
        var pixels = new byte[stride * image.Thumbnail.PixelHeight];
        image.Thumbnail.CopyPixels(pixels, stride, 0);
        Assert.Contains(pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
    }

    [Fact]
    public async Task ImportAsync_RejectsMisleadingExtensionAndCorruptData()
    {
        using var directory = new TestDirectory();
        var jpegNamedPng = TestImages.Jpeg(directory, "renamed.png", 20, 10);
        var corrupt = directory.File("broken.jpg");
        await File.WriteAllTextAsync(
            corrupt,
            "this is not a photograph",
            TestContext.Current.CancellationToken);

        var result = await _service.ImportAsync(
            new[] { jpegNamedPng, corrupt },
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Images);
        Assert.Equal(2, result.Failures.Count);
        Assert.Contains("do not match", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decode", result.Failures[1].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_RejectsUnsupportedExtensionEvenWhenPixelsAreValid()
    {
        using var directory = new TestDirectory();
        var unsupported = TestImages.Png(directory, "photo.txt", 20, 10);

        var result = await _service.ImportAsync(
            new[] { unsupported },
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Images);
        Assert.Contains("allowlist", Assert.Single(result.Failures).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_RejectsDuplicateNormalizedPaths()
    {
        using var directory = new TestDirectory();
        var image = TestImages.Png(directory, "same.png", 20, 10);
        var alternateSpelling = System.IO.Path.Combine(directory.Path, ".", "same.png");

        var result = await _service.ImportAsync(
            new[] { image, alternateSpelling },
            TestContext.Current.CancellationToken);

        Assert.Single(result.Images);
        Assert.Contains("already", Assert.Single(result.Failures).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_ReportsMissingFileWithoutFailingOtherImports()
    {
        using var directory = new TestDirectory();
        var valid = TestImages.Png(directory, "valid.png", 20, 10);
        var missing = directory.File("missing.png");

        var result = await _service.ImportAsync(
            new[] { missing, valid },
            TestContext.Current.CancellationToken);

        Assert.Single(result.Images);
        Assert.Contains("not found", Assert.Single(result.Failures).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_HonorsCancellation()
    {
        using var directory = new TestDirectory();
        var valid = TestImages.Png(directory, "valid.png", 20, 10);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ImportAsync(new[] { valid }, cancellation.Token));
    }
}
