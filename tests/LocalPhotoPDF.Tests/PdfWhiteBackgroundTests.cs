using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalPhotoPDF.Core;

namespace LocalPhotoPDF.Tests;

/// <summary>
/// Guards the fast path added when the PDF build was profiled. Compositing every photo onto a
/// white background through the WPF render pipeline was 76% of the whole build, and for a photo
/// that already fits the quality limit, is already at 96 DPI, and has no alpha channel it cannot
/// change a single pixel. The build now skips it in exactly that case.
///
/// These assert what a reader sees, which is the contract, rather than which path ran.
///
/// ⚠️ They do NOT detect the fast path being taken wrongly, and that is worth knowing before
/// trusting them for that. Removing the opacity condition entirely leaves all three passing,
/// because the JPEG encoder blends towards white the same way the composite does, so the output
/// is byte-identical either way. What they do catch is the white background being lost or
/// changed: painting it black fails the two transparency cases and correctly leaves the opaque
/// one passing. Every structural property of the document, the page count, the page size and the
/// file itself, is identical in all of these situations, so nothing else in the suite would
/// notice.
/// </summary>
public sealed class PdfWhiteBackgroundTests
{
    private readonly PdfGenerationService _service = new();

    [Fact]
    public async Task GenerateAsync_CompositesFullyTransparentPixelsOntoWhite()
    {
        using var directory = new TestDirectory();
        var transparent = TestImages.Png(
            directory,
            "transparent.png",
            160,
            120,
            Color.FromArgb(0, 255, 0, 0));
        var output = directory.File("transparent.pdf");

        await _service.GenerateAsync(
            new PdfBuildRequest(new[] { new PhotoSource(transparent, 0) }, output),
            null,
            TestContext.Current.CancellationToken);

        var pixel = CentrePixelOfFirstEmbeddedImage(output);

        // Transparent red against white is white. Paint that background any other colour and this
        // fails, which is what it is here to catch.
        Assert.True(
            pixel.R > 250 && pixel.G > 250 && pixel.B > 250,
            $"expected a white background, got R={pixel.R} G={pixel.G} B={pixel.B}");
    }

    [Fact]
    public async Task GenerateAsync_BlendsSemiTransparentPixelsTowardsWhite()
    {
        using var directory = new TestDirectory();
        // Half-opaque red over white is a pink near (255, 127, 127). Asserting that the green and
        // blue channels lifted, rather than the exact blend, keeps this from breaking on a codec
        // rounding difference while still failing on a background that is not white.
        var semi = TestImages.Png(
            directory,
            "semi.png",
            160,
            120,
            Color.FromArgb(128, 255, 0, 0));
        var output = directory.File("semi.pdf");

        await _service.GenerateAsync(
            new PdfBuildRequest(new[] { new PhotoSource(semi, 0) }, output),
            null,
            TestContext.Current.CancellationToken);

        var pixel = CentrePixelOfFirstEmbeddedImage(output);

        Assert.True(
            pixel.G > 80 && pixel.B > 80,
            $"expected the red to be blended towards white, got R={pixel.R} G={pixel.G} B={pixel.B}");
    }

    [Fact]
    public async Task GenerateAsync_LeavesAnOpaquePhotoAtItsOwnColour()
    {
        using var directory = new TestDirectory();
        // The photo the fast path is FOR. It must come out unchanged, which is the other half of
        // the claim: skipping the composite is only safe because it changes nothing.
        var opaque = TestImages.Jpeg(directory, "opaque.jpg", 160, 120, Colors.Blue);
        var output = directory.File("opaque.pdf");

        await _service.GenerateAsync(
            new PdfBuildRequest(new[] { new PhotoSource(opaque, 0) }, output),
            null,
            TestContext.Current.CancellationToken);

        var pixel = CentrePixelOfFirstEmbeddedImage(output);

        Assert.True(
            pixel.B > 200 && pixel.R < 60,
            $"expected the photo's own blue, got R={pixel.R} G={pixel.G} B={pixel.B}");
    }

    // The PDF stores each page's photo as an embedded JPEG. Pulling it back out and decoding it is
    // the only way to assert what a reader will actually see: every structural property of the
    // document is identical whether the composite happened or not.
    private static Color CentrePixelOfFirstEmbeddedImage(string pdfPath)
    {
        var bytes = File.ReadAllBytes(pdfPath);
        var marker = Encoding.ASCII.GetBytes("/DCTDecode");
        var markerIndex = IndexOf(bytes, marker, 0);
        Assert.True(markerIndex >= 0, "the PDF contains no DCTDecode image stream");

        var streamIndex = IndexOf(bytes, Encoding.ASCII.GetBytes("stream"), markerIndex);
        Assert.True(streamIndex >= 0, "the image stream has no start");
        var start = streamIndex + "stream".Length;
        if (start + 1 < bytes.Length && bytes[start] == (byte)'\r' && bytes[start + 1] == (byte)'\n')
        {
            start += 2;
        }
        else if (start < bytes.Length && (bytes[start] == (byte)'\n' || bytes[start] == (byte)'\r'))
        {
            start += 1;
        }

        var end = IndexOf(bytes, Encoding.ASCII.GetBytes("endstream"), start);
        Assert.True(end > start, "the image stream has no end");

        using var jpeg = new MemoryStream(bytes, start, end - start, writable: false);
        var decoder = new JpegBitmapDecoder(
            jpeg,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var pixels = new byte[4];
        var centre = new System.Windows.Int32Rect(
            converted.PixelWidth / 2,
            converted.PixelHeight / 2,
            1,
            1);
        converted.CopyPixels(centre, pixels, 4, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (var i = from; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
