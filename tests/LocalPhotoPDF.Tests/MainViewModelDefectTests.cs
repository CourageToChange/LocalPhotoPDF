using LocalPhotoPDF.Core;

namespace LocalPhotoPDF.Tests;

/// <summary>
/// Two defects found while writing the MainViewModel suite. Both were confirmed by reading the code
/// and then by these tests, which were written to FAIL first and did.
/// </summary>
public sealed class MainViewModelDefectTests
{
    [Fact]
    public async Task ClosingTheWindowDuringABuild_DoesNotThrow()
    {
        // MainWindow.xaml.cs calls _viewModel.Dispose() as the window closes, and Dispose sets
        // _generationCancellation to null. The generation's finally block then disposed that same
        // field without a null check, so closing the window while a PDF was building threw a
        // NullReferenceException out of a finally on a background path.
        //
        // The compiler could not warn: within GeneratePdfAsync the field is assigned before use,
        // and flow analysis has no way to know another method nulls it underneath.
        var generator = new ControlledPdfGenerationService();
        using var fixture = new MainViewModelFixture(generator);
        await fixture.AddPhotosAsync("first.png", "second.png");
        var output = fixture.Directory.File("closing.pdf");

        var generation = fixture.ViewModel.GeneratePdfAsync(output);
        await generator.Started.Task;

        fixture.ViewModel.Dispose();

        // Awaiting is the assertion. Before the fix this threw NullReferenceException.
        await generation;
    }

    [Fact]
    public async Task RemovingAPhotoAfterABuild_DismissesTheReadyNotice()
    {
        // Every other edit routes through InvalidateOutput(), which clears the output path AND
        // takes down the "Your PDF is ready" banner. RemovePhoto set LastOutputPath directly and
        // skipped the helper, so the banner survived a removal and went on announcing a PDF that
        // no longer matched the list on screen.
        using var fixture = new MainViewModelFixture();
        var photos = await fixture.AddPhotosAsync("first.png", "second.png");
        var output = fixture.Directory.File("ready.pdf");

        await fixture.ViewModel.GeneratePdfAsync(output);

        // Guard the premise: without a success notice this test would pass for the wrong reason.
        Assert.True(fixture.ViewModel.HasNotice);
        Assert.Equal("Your PDF is ready", fixture.ViewModel.NoticeTitle);

        fixture.ViewModel.RemoveCommand.Execute(photos[0]);

        Assert.Null(fixture.ViewModel.LastOutputPath);
        Assert.False(fixture.ViewModel.HasNotice);
    }
}
