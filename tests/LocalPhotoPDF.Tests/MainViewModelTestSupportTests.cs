using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalPhotoPDF.Core;
using LocalPhotoPDF.Services;
using LocalPhotoPDF.ViewModels;

namespace LocalPhotoPDF.Tests;

internal sealed class MainViewModelFixture : IDisposable
{
    public MainViewModelFixture(IPdfGenerationService? pdfGenerationService = null)
    {
        Directory = new TestDirectory();
        ImportService = new StubImageImportService();
        ViewModel = new MainViewModel(
            ImportService,
            pdfGenerationService ?? new StubPdfGenerationService(),
            new ShellService(),
            new SettingsService(),
            new AppSettings());
    }

    public TestDirectory Directory { get; }

    public StubImageImportService ImportService { get; }

    public MainViewModel ViewModel { get; }

    public async Task<PhotoItemViewModel[]> AddPhotosAsync(params string[] names)
    {
        var paths = names.Select(Directory.File).ToArray();
        await ViewModel.ImportFilesAsync(paths);
        return ViewModel.Photos.ToArray();
    }

    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Dispose();
    }
}

internal sealed class StubImageImportService : IImageImportService
{
    private static readonly BitmapSource Thumbnail = CreateThumbnail();

    public IReadOnlySet<string> SupportedExtensions { get; set; } =
        new HashSet<string>([".png", ".JPG", ".bmp"], StringComparer.OrdinalIgnoreCase);

    public Func<IEnumerable<string>, CancellationToken, Task<ImportResult>>? Import { get; set; }

    public Task<ImportResult> ImportAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        if (Import is not null)
        {
            return Import(filePaths, cancellationToken);
        }

        var images = filePaths
            .Select(path => new ImageInfo(
                path,
                Path.GetFileName(path),
                1_024,
                40,
                30,
                "PNG",
                Thumbnail))
            .ToArray();
        return Task.FromResult(new ImportResult(images, []));
    }

    private static BitmapSource CreateThumbnail()
    {
        var pixels = new byte[] { 0x80, 0x40, 0x20, 0xff };
        var thumbnail = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            4);
        thumbnail.Freeze();
        return thumbnail;
    }
}

internal sealed class StubPdfGenerationService : IPdfGenerationService
{
    public Task GenerateAsync(
        PdfBuildRequest request,
        IProgress<PdfBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class ControlledPdfGenerationService : IPdfGenerationService
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PdfBuildRequest? Request { get; private set; }

    public IProgress<PdfBuildProgress>? Progress { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public async Task GenerateAsync(
        PdfBuildRequest request,
        IProgress<PdfBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Request = request;
        Progress = progress;
        CancellationToken = cancellationToken;
        Started.SetResult();
        using var registration = cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
        await _completion.Task;
    }

    public void Report(PdfBuildProgress update)
    {
        Assert.NotNull(Progress);
        Progress.Report(update);
    }

    public void CompleteSuccessfully()
    {
        Assert.NotNull(Request);
        File.WriteAllBytes(Request.OutputPath, "%PDF-test"u8.ToArray());
        _completion.SetResult();
    }

    public void Fail(Exception exception) => _completion.SetException(exception);
}

internal static class MainViewModelTestWait
{
    public static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
