using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalPhotoPDF.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "LocalPhotoPDF.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failing test should not be hidden by best-effort fixture cleanup.
        }
    }
}

internal static class TestImages
{
    public static string Png(
        TestDirectory directory,
        string name,
        int width,
        int height,
        Color? color = null)
    {
        var path = directory.File(name);
        Save(path, CreateBitmap(width, height, color ?? Colors.CornflowerBlue), new PngBitmapEncoder());
        return path;
    }

    public static string Jpeg(
        TestDirectory directory,
        string name,
        int width,
        int height,
        Color? color = null,
        ushort? exifOrientation = null)
    {
        var path = directory.File(name);
        BitmapMetadata? metadata = null;
        if (exifOrientation.HasValue)
        {
            metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", exifOrientation.Value);
        }

        Save(
            path,
            CreateBitmap(width, height, color ?? Colors.IndianRed),
            new JpegBitmapEncoder { QualityLevel = 95 },
            metadata);
        return path;
    }

    public static string MultiFrameTiff(TestDirectory directory, string name)
    {
        var path = directory.File(name);
        var encoder = new TiffBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CreateBitmap(40, 20, Colors.Green)));
        encoder.Frames.Add(BitmapFrame.Create(CreateBitmap(10, 50, Colors.Purple)));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return path;
    }

    private static BitmapSource CreateBitmap(int width, int height, Color color)
    {
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = color.A;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Save(
        string path,
        BitmapSource source,
        BitmapEncoder encoder,
        BitmapMetadata? metadata = null)
    {
        encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }
}

internal sealed class SynchronousProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
