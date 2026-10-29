using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Processes;
using Viciotek.Infrastructure.Storage;
using Viciotek.Presentation;
using Viciotek.Presentation.Dialogs;

namespace Viciotek.Tests;

public sealed class CoverTransactionTests
{
    [Fact]
    public Task FailedCoverCommit_PreservesJsonModelAndPreviousFiles() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync();
        using var fileLock = File.Open(fixture.Paths.LibraryFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await fixture.ViewModel.ReplaceCoverFromDropAsync(fixture.Card, fixture.Replacement);

        Assert.Equal(fixture.Initial.CoverFileName, fixture.Card.Model.CoverFileName);
        Assert.Equal(fixture.Initial.CoverFileName, Assert.Single(fixture.Library.Load().Games).CoverFileName);
        Assert.True(File.Exists(fixture.Initial.ThumbnailPath));
        Assert.True(File.Exists(Path.Combine(fixture.Paths.CoversDirectory, fixture.Initial.CoverFileName)));
        Assert.Single(Directory.GetFiles(fixture.Paths.CoversDirectory));
        Assert.Single(Directory.GetFiles(fixture.Paths.ThumbsDirectory));
        Assert.True(fixture.ViewModel.ToastIsError);
    });

    [Fact]
    public Task FailedRemoval_DoesNotRemoveGameOrItsCover() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync();
        using var fileLock = File.Open(fixture.Paths.LibraryFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await fixture.ViewModel.RemoveAsync(fixture.Card);
        Assert.Single(fixture.ViewModel.Games);
        Assert.Single(fixture.Library.Load().Games);
        Assert.True(File.Exists(fixture.Initial.ThumbnailPath));
        Assert.True(File.Exists(Path.Combine(fixture.Paths.CoversDirectory, fixture.Initial.CoverFileName)));
        Assert.True(fixture.ViewModel.ToastIsError);
    });

    [Fact]
    public Task SuccessfulReplacements_RetainBackupAndCollectOnlyUnreferencedGeneratedFiles() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync();
        var userFile = Path.Combine(fixture.Paths.CoversDirectory, "my-image.png");
        await File.WriteAllTextAsync(userFile, "not owned by the app");
        await fixture.ViewModel.ReplaceCoverFromDropAsync(fixture.Card, fixture.Replacement);
        var previous = fixture.Card.Model.CoverFileName!;
        var previousThumb = fixture.Card.CoverPath!;
        Assert.True(File.Exists(Path.Combine(fixture.Paths.CoversDirectory, fixture.Initial.CoverFileName)));

        await fixture.ViewModel.ReplaceCoverFromDropAsync(fixture.Card, fixture.Replacement);

        Assert.NotEqual(previous, fixture.Card.Model.CoverFileName);
        Assert.False(File.Exists(Path.Combine(fixture.Paths.CoversDirectory, fixture.Initial.CoverFileName)));
        Assert.False(File.Exists(fixture.Initial.ThumbnailPath));
        Assert.True(File.Exists(Path.Combine(fixture.Paths.CoversDirectory, previous)));
        Assert.True(File.Exists(previousThumb));
        Assert.True(File.Exists(fixture.Card.CoverPath));
        Assert.True(File.Exists(userFile));
        var backup = new AtomicJsonFile<LibraryDocument>(fixture.Paths.LibraryFile + ".bak").Read().Value;
        Assert.Equal(previous, Assert.Single(backup!.Games).CoverFileName);
    });

    [Fact]
    public Task CloseDuringStagedSave_CancelsRollsBackAndDrainsBeforeDisposal() => WpfTest.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await Fixture.CreateAsync(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var change = fixture.ViewModel.ReplaceCoverFromDropAsync(fixture.Card, fixture.Replacement);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var close = fixture.ViewModel.PrepareCloseAsync();
        await Task.WhenAll(change, close).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(fixture.ViewModel.IsClosing);
        Assert.False(fixture.ViewModel.IsDropEnabled);
        Assert.Equal(fixture.Initial.CoverFileName, fixture.Card.Model.CoverFileName);
        Assert.Equal(fixture.Initial.CoverFileName, Assert.Single(fixture.Library.Load().Games).CoverFileName);
        Assert.Single(Directory.GetFiles(fixture.Paths.CoversDirectory));
        Assert.Single(Directory.GetFiles(fixture.Paths.ThumbsDirectory));
        fixture.ViewModel.Dispose();
        fixture.ViewModel.Dispose();
    });

    [Fact]
    public Task CloseDuringStartedCommit_WaitsAndKeepsTheCommittedNewPair() => WpfTest.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? file = null;
        await using var fixture = await Fixture.CreateAsync(async (document, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            await new AtomicJsonFile<LibraryDocument>(file!).WriteAsync(document);
        });
        file = fixture.Paths.LibraryFile;
        var change = fixture.ViewModel.ReplaceCoverFromDropAsync(fixture.Card, fixture.Replacement);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var close = fixture.ViewModel.PrepareCloseAsync();
            Assert.False(close.IsCompleted);
            release.SetResult();
            await Task.WhenAll(change, close).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(fixture.Initial.CoverFileName, fixture.Card.Model.CoverFileName);
            Assert.Equal(fixture.Card.Model.CoverFileName, Assert.Single(fixture.Library.Load().Games).CoverFileName);
            Assert.True(File.Exists(fixture.Card.CoverPath));
            Assert.True(File.Exists(Path.Combine(fixture.Paths.CoversDirectory, fixture.Card.Model.CoverFileName!)));
        }
        finally
        {
            release.TrySetResult();
        }
    });

    [Fact]
    public Task RemovalDuringCoverCommit_IsSerializedAndKeepsBackupRecoverable() => WpfTest.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? file = null;
        await using var fixture = await Fixture.CreateAsync(async (document, token) =>
        {
            entered.TrySetResult();
            await release.Task;
            await new AtomicJsonFile<LibraryDocument>(file!).WriteAsync(document, token);
        });
        file = fixture.Paths.LibraryFile;
        var change = fixture.ViewModel.ReplaceCoverFromDropAsync(fixture.Card, fixture.Replacement);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var remove = fixture.ViewModel.RemoveAsync(fixture.Card);
            Assert.Single(fixture.ViewModel.Games);
            release.SetResult();
            await Task.WhenAll(change, remove).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(fixture.ViewModel.Games);
            Assert.Empty(fixture.Library.Load().Games);
            Assert.True(File.Exists(fixture.Card.CoverPath)); // Referenced by the removal's .bak.
        }
        finally
        {
            release.TrySetResult();
        }
    });

    [Fact]
    public Task FailedImport_LeavesCollectionAndSavedLibraryUnchanged() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = await Fixture.CreateAsync();
        var shortcut = Path.Combine(fixture.Paths.Root, "Test.url");
        await File.WriteAllTextAsync(shortcut, "[InternetShortcut]\nURL=steam://run/123");
        using var fileLock = File.Open(fixture.Paths.LibraryFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await fixture.ViewModel.ImportFilesAsync([shortcut]);
        Assert.Single(fixture.ViewModel.Games);
        Assert.Single(fixture.Library.Load().Games);
        Assert.True(fixture.ViewModel.ToastIsError);
    });

    private sealed class Fixture : IAsyncDisposable
    {
        public required AppPaths Paths { get; init; }
        public required LibraryStore Library { get; init; }
        public required MainViewModel ViewModel { get; init; }
        public required CoverUpdate Initial { get; init; }
        public required string Replacement { get; init; }
        public GameCardViewModel Card { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(Func<LibraryDocument, CancellationToken, Task>? write = null)
        {
            var paths = new AppPaths(TestHelpers.CreateTempDirectory());
            paths.EnsureCreated();
            var covers = new CoverStore(paths);
            var game = new Game { Name = "Game", LaunchPath = @"C:\game.exe" };
            var initial = await covers.SetFromBytesAsync(game.Id, TestHelpers.CreatePng(480, 720), null);
            game.CoverFileName = initial.CoverFileName;
            var library = new LibraryStore(paths);
            await library.SaveAsync([game]);
            var replacement = Path.Combine(paths.Root, "replacement.png");
            await File.WriteAllBytesAsync(replacement, TestHelpers.CreatePng(1000, 600));
            var viewModel = new MainViewModel([game], new AppSettings(), write is null ? library : new LibraryStore(paths, write),
                new SettingsStore(paths), new GameImporter(new NoopResolver()), new GameLauncher(), covers,
                new SteamGridDbClient(null), new Dialogs(), null);
            return new Fixture { Paths = paths, Library = library, ViewModel = viewModel, Initial = initial,
                Replacement = replacement, Card = Assert.Single(viewModel.Games) };
        }

        public async ValueTask DisposeAsync()
        {
            await ViewModel.PrepareCloseAsync();
            Directory.Delete(Paths.Root, recursive: true);
        }
    }

    private sealed class NoopResolver : IShortcutResolver
    {
        public ShortcutInfo Resolve(string shortcutPath) => throw new NotSupportedException();
    }

    private sealed class Dialogs : IDialogService
    {
        public string[]? PickGameFiles() => null;
        public string? PickCoverFile() => null;
        public Task PickCoverFromGalleryAsync(Game game, SteamGridDbClient steamGrid,
            Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback, CancellationToken cancellationToken) => Task.CompletedTask;
        public GameEditResult? EditGame(Game game) => null;
        public AppSettings? EditPreferences(AppSettings settings) => null;
        public bool ConfirmRemove(string gameName) => true;
    }
}
