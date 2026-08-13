using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalPhotoPDF.Core;

internal static class ImageDecoding
{
    internal const long MaximumFileSizeBytes = 250L * 1024 * 1024;
    internal const long MaximumPixelCount = 200_000_000;
    internal const int MaximumPhotoCount = 500;

    internal static readonly FrozenSet<string> SupportedExtensions = new[]
    {
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png",
        ".bmp", ".dib", ".rle",
        ".gif",
        ".tif", ".tiff",
        ".jxr", ".wdp", ".hdp",
        ".heic", ".heif", ".hif",
        ".webp", ".avif",
        ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2",
        ".orf", ".rw2", ".raf", ".pef", ".raw",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ExifOrientationQueries =
    {
        "/app1/ifd/{ushort=274}",
        "/ifd/{ushort=274}",
        "/xmp/tiff:Orientation",
    };

    private static readonly char[] DecoderExtensionSeparators = { ',', ';', ' ' };

    internal static DecodedPhoto Decode(string filePath, int? maximumLongEdge = null)
    {
        if (maximumLongEdge is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumLongEdge),
                maximumLongEdge,
                "The maximum decoded edge must be positive.");
        }

        var fullPath = ValidateAndNormalizePath(filePath);
        var extension = Path.GetExtension(fullPath);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new NotSupportedException(
                $"The file extension '{extension}' is not in the supported photo allowlist.");
        }

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);

        if (stream.Length > MaximumFileSizeBytes)
        {
            throw new InvalidDataException(
                $"The photo is larger than the {MaximumFileSizeBytes / 1024 / 1024} MiB safety limit.");
        }

        BitmapDecoder decoder;
        try
        {
            decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);
        }
        catch (Exception exception) when (exception is FileFormatException
                                          or NotSupportedException
                                          or InvalidOperationException
                                          or ArgumentException
                                          or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidDataException(
                "Windows could not decode this file as a valid photo. The file may be damaged or its codec may be unavailable.",
                exception);
        }

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("The photo contains no image frames.");
        }

        if (!ExtensionMatchesDecoder(extension, decoder))
        {
            throw new InvalidDataException(
                "The file contents do not match its filename extension.");
        }

        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
        {
            throw new InvalidDataException("The photo has invalid pixel dimensions.");
        }

        var pixelCount = checked((long)frame.PixelWidth * frame.PixelHeight);
        if (pixelCount > MaximumPixelCount)
        {
            throw new InvalidDataException(
                $"The photo exceeds the {MaximumPixelCount / 1_000_000} megapixel safety limit.");
        }

        var orientation = ReadExifOrientation(frame.Metadata as BitmapMetadata);
        var swapsAxes = orientation is >= 5 and <= 8;
        var displayWidth = swapsAxes ? frame.PixelHeight : frame.PixelWidth;
        var displayHeight = swapsAxes ? frame.PixelWidth : frame.PixelHeight;

        BitmapSource source = ScaleToLongEdge(frame, maximumLongEdge);
        source = ApplyExifOrientation(source, orientation);
        source = ConvertToSrgb(source, frame);
        source = Materialize(source);

        return new DecodedPhoto(
            fullPath,
            stream.Length,
            GetFormatName(decoder),
            displayWidth,
            displayHeight,
            source);
    }

    internal static BitmapSource ApplyManualRotation(BitmapSource source, int rotationDegrees)
    {
        if (rotationDegrees % 90 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotationDegrees),
                rotationDegrees,
                "Rotation must be a multiple of 90 degrees.");
        }

        var normalized = ((rotationDegrees % 360) + 360) % 360;
        if (normalized == 0)
        {
            return source;
        }

        var rotated = new TransformedBitmap(source, new RotateTransform(normalized));
        Freeze(rotated);
        return rotated;
    }

    internal static BitmapSource CreateThumbnail(BitmapSource source, int maximumLongEdge = 192)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLongEdge);

        var longEdge = Math.Max(source.PixelWidth, source.PixelHeight);
        var scale = Math.Min(1d, (double)maximumLongEdge / longEdge);
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));

        // A TransformedBitmap keeps its full-resolution source graph alive. Materialize
        // the preview so a list of small thumbnails cannot retain hundreds of megabytes
        // of decoded source pixels per photo.
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(source, new Rect(0, 0, width, height));
        }

        var thumbnail = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        thumbnail.Render(visual);
        Freeze(thumbnail);
        return thumbnail;
    }

    private static BitmapSource ScaleToLongEdge(BitmapSource source, int? maximumLongEdge)
    {
        if (maximumLongEdge is null)
        {
            return source;
        }

        var longEdge = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longEdge <= maximumLongEdge.Value)
        {
            return source;
        }

        var scale = (double)maximumLongEdge.Value / longEdge;
        return new TransformedBitmap(source, new ScaleTransform(scale, scale));
    }

    private static string ValidateAndNormalizePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A photo path is required.", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The photo file was not found.", fullPath);
        }

        return fullPath;
    }

    private static bool ExtensionMatchesDecoder(string extension, BitmapDecoder decoder)
    {
        return decoder switch
        {
            JpegBitmapDecoder => extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                                 || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                                 || extension.Equals(".jpe", StringComparison.OrdinalIgnoreCase)
                                 || extension.Equals(".jfif", StringComparison.OrdinalIgnoreCase),
            PngBitmapDecoder => extension.Equals(".png", StringComparison.OrdinalIgnoreCase),
            BmpBitmapDecoder => extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
                                || extension.Equals(".dib", StringComparison.OrdinalIgnoreCase)
                                || extension.Equals(".rle", StringComparison.OrdinalIgnoreCase),
            GifBitmapDecoder => extension.Equals(".gif", StringComparison.OrdinalIgnoreCase),
            TiffBitmapDecoder => extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
                                 || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase),
            WmpBitmapDecoder => extension.Equals(".jxr", StringComparison.OrdinalIgnoreCase)
                                || extension.Equals(".wdp", StringComparison.OrdinalIgnoreCase)
                                || extension.Equals(".hdp", StringComparison.OrdinalIgnoreCase),
            _ => DecoderAdvertisesExtension(decoder, extension),
        };
    }

    // A codec does not always advertise every extension its format ships under. Canon and Sony
    // write ordinary HEIF files as .hif, but the Microsoft HEIF Image Extension advertises only
    // .heic;.heif;.avci — so a photo that decodes perfectly was refused with "the file contents do
    // not match its filename extension", which reads as a corruption warning rather than the codec
    // gap it actually is.
    //
    // Matching within a family is still a real check: the extension and the decoder must belong to
    // the SAME group. A JPEG renamed to .hif never reaches here — it matches the JpegBitmapDecoder
    // arm above and is correctly rejected — so this does not weaken the misleading-extension guard.
    private static readonly string[][] ExtensionAliasGroups =
    {
        new[] { ".heic", ".heics", ".heif", ".heifs", ".hif", ".avci", ".avcs" },
        new[] { ".avif", ".avifs" },
    };

    private static bool DecoderAdvertisesExtension(BitmapDecoder decoder, string extension)
    {
        var advertised = decoder.CodecInfo?.FileExtensions;
        if (string.IsNullOrWhiteSpace(advertised))
        {
            return false;
        }

        var advertisedExtensions = advertised
            .Split(DecoderExtensionSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim().TrimStart('*'))
            .ToArray();

        if (advertisedExtensions.Any(value => value.Equals(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var aliases = ExtensionAliasGroups.FirstOrDefault(
            group => group.Contains(extension, StringComparer.OrdinalIgnoreCase));

        return aliases is not null
               && advertisedExtensions.Any(value => aliases.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    private static int ReadExifOrientation(BitmapMetadata? metadata)
    {
        if (metadata is null)
        {
            return 1;
        }

        foreach (var query in ExifOrientationQueries)
        {
            try
            {
                if (metadata.GetQuery(query) is { } value)
                {
                    var orientation = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    if (orientation is >= 1 and <= 8)
                    {
                        return orientation;
                    }
                }
            }
            catch (Exception exception) when (exception is NotSupportedException
                                              or InvalidOperationException
                                              or ArgumentException
                                              or FormatException
                                              or InvalidCastException
                                              or OverflowException
                                              or System.Runtime.InteropServices.COMException)
            {
                // A malformed metadata block must not prevent the pixels from importing.
            }
        }

        return 1;
    }

    private static BitmapSource ApplyExifOrientation(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Combine(new ScaleTransform(-1, 1), new RotateTransform(270)),
            6 => new RotateTransform(90),
            7 => Combine(new ScaleTransform(-1, 1), new RotateTransform(90)),
            8 => new RotateTransform(270),
            _ => null,
        };

        if (transform is null)
        {
            return source;
        }

        var oriented = new TransformedBitmap(source, transform);
        Freeze(oriented);
        return oriented;
    }

    private static TransformGroup Combine(params Transform[] transforms)
    {
        var group = new TransformGroup();
        foreach (var transform in transforms)
        {
            group.Children.Add(transform);
        }

        return group;
    }

    private static BitmapSource ConvertToSrgb(BitmapSource source, BitmapFrame frame)
    {
        if (frame.ColorContexts is { Count: > 0 })
        {
            try
            {
                var converted = new ColorConvertedBitmap(
                    source,
                    frame.ColorContexts[0],
                    new ColorContext(PixelFormats.Bgra32),
                    PixelFormats.Bgra32);
                Freeze(converted);
                return converted;
            }
            catch (Exception exception) when (exception is NotSupportedException
                                              or InvalidOperationException
                                              or ArgumentException
                                              or FileFormatException
                                              or System.Runtime.InteropServices.COMException)
            {
                // Invalid or unsupported embedded profiles fall back to WIC's pixel conversion.
            }
        }

        var fallback = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        Freeze(fallback);
        return fallback;
    }

    private static WriteableBitmap Materialize(BitmapSource source)
    {
        // WriteableBitmap copies the scaled pixels into its own backing store. This
        // detaches the returned bitmap from the decoder, stream, metadata, and any
        // full-resolution transform chain before those objects leave this method.
        var materialized = new WriteableBitmap(source);
        Freeze(materialized);
        return materialized;
    }

    private static string GetFormatName(BitmapDecoder decoder) => decoder switch
    {
        JpegBitmapDecoder => "JPEG",
        PngBitmapDecoder => "PNG",
        BmpBitmapDecoder => "BMP",
        GifBitmapDecoder => "GIF",
        TiffBitmapDecoder => "TIFF",
        WmpBitmapDecoder => "JPEG XR",
        _ => decoder.CodecInfo?.FriendlyName ?? "Image",
    };

    private static void Freeze(BitmapSource source)
    {
        if (source.CanFreeze && !source.IsFrozen)
        {
            source.Freeze();
        }
    }
}

internal sealed record DecodedPhoto(
    string FilePath,
    long FileSizeBytes,
    string Format,
    int PixelWidth,
    int PixelHeight,
    BitmapSource Bitmap);
