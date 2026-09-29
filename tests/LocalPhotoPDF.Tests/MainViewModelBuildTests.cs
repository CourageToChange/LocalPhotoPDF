using System.ComponentModel;
using System.IO;
using LocalPhotoPDF.Core;

namespace LocalPhotoPDF.Tests;

public sealed class MainViewModelBuildTests
{
    [Fact]
    public async Task SuccessfulBuild_TransitionsBusyToIdle_ReportsProgressAndExposesOutput()
    {
        var generator = new ControlledPdfGenerationService();
        using var fixture = new MainViewModelFixture(generator);
        var viewModel = fixture.ViewModel;
        await fixture.AddPhotosAsync("first.png", "second.png");
        var output = fixture.Directory.File("result.pdf");

        var generation = viewModel.GeneratePdfAsync(output);
        await generator.Started.Task;

        Assert.True(viewModel.IsGenerating);
        Assert.True(viewModel.IsBusy);
        Assert.Equal(0, viewModel.ProgressPercentage);
        Assert.Equal("Preparing 2 photos…", viewModel.ProgressText);
        Assert.Equal(["first.png", "second.png"], generator.Request!.Photos.Select(photo => Path.GetFileName(photo.FilePath)));

        generator.Report(new PdfBuildProgress(1, 2, fixture.Directory.File("second.png")));
        await MainViewModelTestWait.UntilAsync(() => viewModel.ProgressPercentage == 50);

        Assert.Equal("Processed 1 of 2 — second.png", viewModel.ProgressText);

        generator.CompleteSuccessfully();
        await generation;

        Assert.False(viewModel.IsGenerating);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(100, viewModel.ProgressPercentage);
        Assert.Equal("Finished 2 of 2 photos", viewModel.ProgressText);
        Assert.Equal(Path.GetFullPath(output), viewModel.LastOutputPath);
        Assert.True(viewModel.HasOutput);
        Assert.Equal("result.pdf", viewModel.OutputFileName);
        Assert.Equal("Your PDF is ready", viewModel.NoticeTitle);
        Assert.Contains("2 pages", viewModel.NoticeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelCommand_CancelsRunningBuild_AndReturnsToIdleWithNotice()
    {
        var generator = new ControlledPdfGenerationService();
        using var fixture = new MainViewModelFixture(generator);
        var viewModel = fixture.ViewModel;
        await fixture.AddPhotosAsync("photo.png");

        var generation = viewModel.GeneratePdfAsync(fixture.Directory.File("cancelled.pdf"));
        await generator.Started.Task;
        viewModel.CancelCommand.Execute(null);
        await generation;

        Assert.True(generator.CancellationToken.IsCancellationRequested);
        Assert.False(viewModel.IsGenerating);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.Equal("Cancelling safely…", viewModel.ProgressText);
        Assert.Equal("PDF creation cancelled", viewModel.NoticeTitle);
        Assert.Equal("No existing PDF was changed.", viewModel.NoticeMessage);
        Assert.False(viewModel.HasOutput);
    }

    [Fact]
    public async Task RecoverableBuildFailure_ReturnsToIdleAndExposesFriendlyError()
    {
        var generator = new ControlledPdfGenerationService();
        using var fixture = new MainViewModelFixture(generator);
        var viewModel = fixture.ViewModel;
        await fixture.AddPhotosAsync("photo.png");
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        var generation = viewModel.GeneratePdfAsync(fixture.Directory.File("failed.pdf"));
        await generator.Started.Task;
        generator.Fail(new UnauthorizedAccessException("sensitive operating-system detail"));
        await generation;

        Assert.False(viewModel.IsGenerating);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasOutput);
        Assert.Null(viewModel.LastOutputPath);
        Assert.Equal("The PDF could not be created", viewModel.NoticeTitle);
        Assert.Equal("!", viewModel.NoticeIcon);
        Assert.Equal(
            "Windows denied access to that location. Choose a folder you can write to and try again.",
            viewModel.NoticeMessage);
        Assert.Contains(nameof(viewModel.NoticeIcon), changed, StringComparer.Ordinal);
    }

    [Fact]
    public async Task BuildStateChanges_NotifyEveryBoundUiProperty()
    {
        var generator = new ControlledPdfGenerationService();
        using var fixture = new MainViewModelFixture(generator);
        var viewModel = fixture.ViewModel;
        await fixture.AddPhotosAsync("photo.png");
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        var generation = viewModel.GeneratePdfAsync(fixture.Directory.File("notified.pdf"));
        await generator.Started.Task;
        generator.Report(new PdfBuildProgress(1, 2, fixture.Directory.File("photo.png")));
        await MainViewModelTestWait.UntilAsync(() => viewModel.ProgressPercentage == 50);
        generator.CompleteSuccessfully();
        await generation;

        AssertPropertiesChanged(
            changed,
            nameof(viewModel.IsGenerating),
            nameof(viewModel.IsBusy),
            nameof(viewModel.CanEdit),
            nameof(viewModel.CanGenerate),
            nameof(viewModel.ImportStatus),
            nameof(viewModel.ProgressPercentage),
            nameof(viewModel.ProgressText),
            nameof(viewModel.LastOutputPath),
            nameof(viewModel.HasOutput),
            nameof(viewModel.OutputFileName),
            nameof(viewModel.NoticeTitle),
            nameof(viewModel.NoticeMessage),
            nameof(viewModel.HasNotice));
    }

    [Fact]
    public async Task ImportAndCollectionChanges_NotifyEveryBoundComputedProperty()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.ViewModel;
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await fixture.AddPhotosAsync("photo.png");

        AssertPropertiesChanged(
            changed,
            nameof(viewModel.IsImporting),
            nameof(viewModel.IsBusy),
            nameof(viewModel.CanEdit),
            nameof(viewModel.CanGenerate),
            nameof(viewModel.ImportStatus),
            nameof(viewModel.SelectedPhoto),
            nameof(viewModel.HasPhotos),
            nameof(viewModel.IsEmpty),
            nameof(viewModel.PhotoCountText));
    }

    [Fact]
    public void OptionChanges_NotifyDescriptionsAndUpdateSettingsBackedValues()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.ViewModel;
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.PageSize = PdfPageSize.Letter;
        viewModel.Margin = PdfMargin.None;
        viewModel.Quality = PdfQuality.High;

        Assert.Equal("Fits each photo to a US Letter portrait or landscape page.", viewModel.PageSizeDescription);
        Assert.Equal("Uses the full printable page.", viewModel.MarginDescription);
        Assert.Equal("Keeps more detail and creates a larger PDF.", viewModel.QualityDescription);
        AssertPropertiesChanged(
            changed,
            nameof(viewModel.PageSize),
            nameof(viewModel.PageSizeDescription),
            nameof(viewModel.Margin),
            nameof(viewModel.MarginDescription),
            nameof(viewModel.Quality),
            nameof(viewModel.QualityDescription));
    }

    [Fact]
    public void SupportedPhotoFilter_IsSortedCaseInsensitivelyAndIncludesAllFiles()
    {
        using var fixture = new MainViewModelFixture();

        Assert.Equal(
            "Supported photos|*.bmp;*.JPG;*.png|All files|*.*",
            fixture.ViewModel.SupportedPhotoFilter);
    }

    [Fact]
    public async Task SuccessfulOutput_IsInvalidatedWhenAnOptionChanges()
    {
        var generator = new ControlledPdfGenerationService();
        using var fixture = new MainViewModelFixture(generator);
        var viewModel = fixture.ViewModel;
        await fixture.AddPhotosAsync("photo.png");
        var generation = viewModel.GeneratePdfAsync(fixture.Directory.File("stale.pdf"));
        await generator.Started.Task;
        generator.CompleteSuccessfully();
        await generation;

        viewModel.Margin = PdfMargin.TenMillimeters;

        Assert.Null(viewModel.LastOutputPath);
        Assert.False(viewModel.HasOutput);
        Assert.False(viewModel.HasNotice);
        Assert.False(viewModel.OpenPdfCommand.CanExecute(null));
        Assert.False(viewModel.ShowInFolderCommand.CanExecute(null));
    }

    private static void AssertPropertiesChanged(
        IEnumerable<string?> changed,
        params string[] expectedProperties)
    {
        foreach (var property in expectedProperties)
        {
            Assert.Contains(property, changed, StringComparer.Ordinal);
        }
    }
}
