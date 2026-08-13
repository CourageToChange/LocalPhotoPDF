using System.IO;

namespace LocalPhotoPDF.Core;

public sealed class WicImageImportService : IImageImportService
{
    public IReadOnlySet<string> SupportedExtensions => ImageDecoding.SupportedExtensions;

    public Task<ImportResult> ImportAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var paths = filePaths.ToArray();
        // Runs on the shared imaging thread rather than the pool: constructing WPF visuals on a
        // pool thread creates a Dispatcher there that is never shut down. See ImagingThread.
        return ImagingThread.Shared.RunAsync(() => Import(paths, cancellationToken), cancellationToken);
    }

    private static ImportResult Import(
        string[] filePaths,
        CancellationToken cancellationToken)
    {
        var images = new List<ImageInfo>();
        var failures = new List<ImportFailure>();
        var encounteredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < filePaths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suppliedPath = filePaths[index] ?? string.Empty;

            if (index >= ImageDecoding.MaximumPhotoCount)
            {
                failures.Add(new ImportFailure(
                    suppliedPath,
                    $"Only {ImageDecoding.MaximumPhotoCount} photos can be imported at once."));
                continue;
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(suppliedPath);
            }
            catch (Exception exception) when (exception is ArgumentException
                                              or NotSupportedException
                                              or PathTooLongException)
            {
                failures.Add(new ImportFailure(suppliedPath, exception.Message));
                continue;
            }

            if (!encounteredPaths.Add(normalizedPath))
            {
                failures.Add(new ImportFailure(suppliedPath, "This photo is already in the list."));
                continue;
            }

            try
            {
                var decoded = ImageDecoding.Decode(normalizedPath, maximumLongEdge: 192);
                var thumbnail = ImageDecoding.CreateThumbnail(decoded.Bitmap);
                images.Add(new ImageInfo(
                    decoded.FilePath,
                    Path.GetFileName(decoded.FilePath),
                    decoded.FileSizeBytes,
                    decoded.PixelWidth,
                    decoded.PixelHeight,
                    decoded.Format,
                    thumbnail));
            }
            catch (Exception exception) when (IsRecoverableImportFailure(exception))
            {
                failures.Add(new ImportFailure(suppliedPath, exception.Message));
            }
        }

        return new ImportResult(images.AsReadOnly(), failures.AsReadOnly());
    }

    private static bool IsRecoverableImportFailure(Exception exception) => exception is
        ArgumentException
        or FileNotFoundException
        or DirectoryNotFoundException
        or UnauthorizedAccessException
        or IOException
        or NotSupportedException
        or InvalidDataException
        or System.Runtime.InteropServices.COMException
        or OutOfMemoryException;
}
