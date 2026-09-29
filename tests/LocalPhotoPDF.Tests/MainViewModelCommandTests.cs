using LocalPhotoPDF.Core;

namespace LocalPhotoPDF.Tests;

public sealed class MainViewModelCommandTests
{
    [Fact]
    public void Commands_WithAnEmptyList_ExposeOnlyIdleEditing()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.ViewModel;

        Assert.True(viewModel.CanEdit);
        Assert.False(viewModel.CanGenerate);
        Assert.False(viewModel.ClearCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.False(viewModel.MoveUpCommand.CanExecute(null));
        Assert.False(viewModel.MoveDownCommand.CanExecute(null));
        Assert.False(viewModel.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Commands_WithOnePhoto_EnableBuildRemoveAndClear_ButNotReorder()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.ViewModel;
        var photo = Assert.Single(await fixture.AddPhotosAsync("only.png"));

        Assert.True(viewModel.CanGenerate);
        Assert.True(viewModel.ClearCommand.CanExecute(null));
        Assert.True(viewModel.RemoveCommand.CanExecute(photo));
        Assert.True(viewModel.RotateLeftCommand.CanExecute(photo));
        Assert.True(viewModel.RotateRightCommand.CanExecute(photo));
        Assert.False(viewModel.MoveUpCommand.CanExecute(photo));
        Assert.False(viewModel.MoveDownCommand.CanExecute(photo));
    }

    [Fact]
    public async Task ReorderCommands_RespectFirstMiddleAndLastBoundaries()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.ViewModel;
        var photos = await fixture.AddPhotosAsync("first.png", "middle.png", "last.png");

        Assert.False(viewModel.MoveUpCommand.CanExecute(photos[0]));
        Assert.True(viewModel.MoveDownCommand.CanExecute(photos[0]));
        Assert.True(viewModel.MoveUpCommand.CanExecute(photos[1]));
        Assert.True(viewModel.MoveDownCommand.CanExecute(photos[1]));
        Assert.True(viewModel.MoveUpCommand.CanExecute(photos[2]));
        Assert.False(viewModel.MoveDownCommand.CanExecute(photos[2]));
    }

    [Fact]
    public async Task PhotoCommands_RejectNullAndItemsOutsideTheCollection()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.ViewModel;
        await fixture.AddPhotosAsync("listed.png");
        using var otherFixture = new MainViewModelFixture();
        var outsider = Assert.Single(await otherFixture.AddPhotosAsync("outsider.png"));

        Assert.False(viewModel.MoveUpCommand.CanExecute(outsider));
        Assert.False(viewModel.MoveDownCommand.CanExecute(outsider));
        Assert.False(viewModel.RemoveCommand.CanExecute(outsider));
        Assert.False(viewModel.RotateLeftCommand.CanExecute(outsider));
        Assert.False(viewModel.RotateRightCommand.CanExecute(null));
    }

    [Fact]
    public async Task AllEditingAndBuildCommands_DisableDuringGeneration_WhileCancelEnables()
    {
        var generator = new ControlledPdfGenerationService();
        using var fixture = new MainViewModelFixture(generator);
        var viewModel = fixture.ViewModel;
        var photos = await fixture.AddPhotosAsync("first.png", "second.png");

        var generation = viewModel.GeneratePdfAsync(fixture.Directory.File("busy.pdf"));
        await generator.Started.Task;

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanEdit);
        Assert.False(viewModel.CanGenerate);
        Assert.False(viewModel.ClearCommand.CanExecute(null));
        Assert.False(viewModel.MoveDownCommand.CanExecute(photos[0]));
        Assert.False(viewModel.RemoveCommand.CanExecute(photos[0]));
        Assert.True(viewModel.CancelCommand.CanExecute(null));

        viewModel.CancelCommand.Execute(null);
        await generation;
    }

    [Fact]
    public async Task AddBuildAndEditing_DisableDuringImport_WhileCancelEnables()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.ViewModel;
        var existing = Assert.Single(await fixture.AddPhotosAsync("existing.png"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<ImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serviceToken = CancellationToken.None;
        fixture.ImportService.Import = async (_, cancellationToken) =>
        {
            serviceToken = cancellationToken;
            using var registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
            started.SetResult();
            return await completion.Task;
        };

        var import = viewModel.ImportFilesAsync([fixture.Directory.File("pending.png")]);
        await started.Task;

        Assert.True(viewModel.IsImporting);
        Assert.False(viewModel.CanEdit);
        Assert.False(viewModel.CanGenerate);
        Assert.False(viewModel.ClearCommand.CanExecute(null));
        Assert.False(viewModel.RemoveCommand.CanExecute(existing));
        Assert.True(viewModel.CancelCommand.CanExecute(null));

        viewModel.CancelCommand.Execute(null);
        await import;

        Assert.True(serviceToken.IsCancellationRequested);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanEdit);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.Equal("Import cancelled", viewModel.NoticeTitle);
    }
}
