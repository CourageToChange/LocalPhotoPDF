namespace LocalPhotoPDF.Tests;

public sealed class MainViewModelCollectionTests
{
    [Fact]
    public async Task Import_AppendsPhotosInExactInputOrder_AndIndexesThem()
    {
        using var fixture = new MainViewModelFixture();

        var photos = await fixture.AddPhotosAsync("third.png", "first.png", "second.png");

        Assert.Equal(["third.png", "first.png", "second.png"], photos.Select(photo => photo.DisplayName));
        Assert.Equal([1, 2, 3], photos.Select(photo => photo.Position));
        Assert.Same(photos[0], fixture.ViewModel.SelectedPhoto);
        Assert.Equal("3 photos", fixture.ViewModel.PhotoCountText);
    }

    [Fact]
    public async Task RemoveMiddle_PreservesRemainingOrder_ReindexesAndSelectsSuccessor()
    {
        using var fixture = new MainViewModelFixture();
        var photos = await fixture.AddPhotosAsync("first.png", "remove.png", "last.png");

        fixture.ViewModel.RemoveCommand.Execute(photos[1]);

        Assert.Equal(["first.png", "last.png"], fixture.ViewModel.Photos.Select(photo => photo.DisplayName));
        Assert.Equal([1, 2], fixture.ViewModel.Photos.Select(photo => photo.Position));
        Assert.Same(photos[2], fixture.ViewModel.SelectedPhoto);
    }

    [Fact]
    public async Task MoveUp_MovesOnlyTheRequestedPhotoOnePlace()
    {
        using var fixture = new MainViewModelFixture();
        var photos = await fixture.AddPhotosAsync("first.png", "second.png", "third.png");

        fixture.ViewModel.MoveUpCommand.Execute(photos[1]);

        Assert.Equal(["second.png", "first.png", "third.png"], fixture.ViewModel.Photos.Select(photo => photo.DisplayName));
        Assert.Equal([1, 2, 3], fixture.ViewModel.Photos.Select(photo => photo.Position));
        Assert.Same(photos[1], fixture.ViewModel.SelectedPhoto);
    }

    [Fact]
    public async Task MoveDown_MovesOnlyTheRequestedPhotoOnePlace()
    {
        using var fixture = new MainViewModelFixture();
        var photos = await fixture.AddPhotosAsync("first.png", "second.png", "third.png");

        fixture.ViewModel.MoveDownCommand.Execute(photos[1]);

        Assert.Equal(["first.png", "third.png", "second.png"], fixture.ViewModel.Photos.Select(photo => photo.DisplayName));
        Assert.Equal([1, 2, 3], fixture.ViewModel.Photos.Select(photo => photo.Position));
        Assert.Same(photos[1], fixture.ViewModel.SelectedPhoto);
    }

    [Fact]
    public async Task MovingFirstUpAndLastDown_LeavesExactOrderUnchanged()
    {
        using var fixture = new MainViewModelFixture();
        var photos = await fixture.AddPhotosAsync("first.png", "middle.png", "last.png");

        fixture.ViewModel.MoveUpCommand.Execute(photos[0]);
        fixture.ViewModel.MoveDownCommand.Execute(photos[2]);

        Assert.Equal(["first.png", "middle.png", "last.png"], fixture.ViewModel.Photos.Select(photo => photo.DisplayName));
        Assert.Equal([1, 2, 3], fixture.ViewModel.Photos.Select(photo => photo.Position));
    }

    [Fact]
    public async Task DragReorder_MovesDraggedPhotoToTargetIndex_AndReindexes()
    {
        using var fixture = new MainViewModelFixture();
        var photos = await fixture.AddPhotosAsync("first.png", "second.png", "third.png");

        fixture.ViewModel.ReorderPhoto(photos[0], photos[2]);

        Assert.Equal(["second.png", "third.png", "first.png"], fixture.ViewModel.Photos.Select(photo => photo.DisplayName));
        Assert.Equal([1, 2, 3], fixture.ViewModel.Photos.Select(photo => photo.Position));
        Assert.Same(photos[0], fixture.ViewModel.SelectedPhoto);
    }

    [Fact]
    public async Task Clear_RemovesEveryPhotoAndRestoresEmptyState()
    {
        using var fixture = new MainViewModelFixture();
        await fixture.AddPhotosAsync("first.png", "second.png");

        fixture.ViewModel.ClearCommand.Execute(null);

        Assert.Empty(fixture.ViewModel.Photos);
        Assert.Null(fixture.ViewModel.SelectedPhoto);
        Assert.False(fixture.ViewModel.HasPhotos);
        Assert.True(fixture.ViewModel.IsEmpty);
        Assert.Equal("0 photos", fixture.ViewModel.PhotoCountText);
        Assert.False(fixture.ViewModel.CanGenerate);
    }

    [Fact]
    public async Task ImportingDuplicatePath_DoesNotChangeExistingOrder()
    {
        using var fixture = new MainViewModelFixture();
        await fixture.AddPhotosAsync("first.png", "second.png");

        await fixture.ViewModel.ImportFilesAsync(
            [fixture.Directory.File("FIRST.png"), fixture.Directory.File("third.png")]);

        Assert.Equal(["first.png", "second.png", "third.png"], fixture.ViewModel.Photos.Select(photo => photo.DisplayName));
        Assert.Equal("Added 1 photo with 1 skipped", fixture.ViewModel.NoticeTitle);
        Assert.Contains("already", fixture.ViewModel.NoticeMessage, StringComparison.OrdinalIgnoreCase);
    }
}
