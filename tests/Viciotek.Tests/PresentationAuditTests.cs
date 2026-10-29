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

public sealed class PresentationAuditTests
{
    [Fact]
    public Task SearchEnter_LaunchesFirstVisibleResultAndIgnoresStaleSelection() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            var first = new Game { Name = "Hades", LaunchPath = @"C:\hades.exe" };
            var second = new Game { Name = "Hollow Knight", LaunchPath = @"C:\knight.exe" };
            var launcher = new RecordingLauncher();
            using var viewModel = new MainViewModel([first, second], new AppSettings(), new LibraryStore(paths),
                new SettingsStore(paths), new GameImporter(new FakeShortcutResolver()), launcher,
                new CoverStore(paths), new SteamGridDbClient(null), new FakeDialogs(), null);
            viewModel.SelectedGame = viewModel.Games[0];
            viewModel.SearchQuery = "knight";
            await viewModel.LaunchSearchResultAsync();
            Assert.Equal(second.Id, Assert.Single(launcher.Launched));
            viewModel.SearchQuery = "no matches";
            await viewModel.LaunchSearchResultAsync();
            Assert.Single(launcher.Launched);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    });

    [Fact]
    public Task SearchEnter_RespectsSelectedVisibleCardAndBusyState() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            var launcher = new RecordingLauncher();
            using var viewModel = new MainViewModel([new Game { Name = "A" }, new Game { Name = "B" }],
                new AppSettings(), new LibraryStore(paths), new SettingsStore(paths), new GameImporter(new FakeShortcutResolver()),
                launcher, new CoverStore(paths), new SteamGridDbClient(null), new FakeDialogs(), null);
            viewModel.SelectedGame = viewModel.Games[1];
            viewModel.SelectedGame.IsBusy = true;
            await viewModel.LaunchSearchResultAsync();
            Assert.Empty(launcher.Launched);
            viewModel.SelectedGame.IsBusy = false;
            await viewModel.LaunchSearchResultAsync();
            Assert.Equal(viewModel.SelectedGame.Model.Id, Assert.Single(launcher.Launched));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    });

    private sealed class RecordingLauncher : IGameLauncher
    {
        public List<Guid> Launched { get; } = [];
        public Task<LaunchResult> LaunchAsync(Game game)
        {
            Launched.Add(game.Id);
            return Task.FromResult(LaunchResult.Fail("Test: no real process is started."));
        }
    }

    [Fact]
    public Task Edit_RefreshesFilteredResultsMonogramAndSearchKeyWithoutSuccessToast() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            var dialogs = new FakeDialogs();
            using var viewModel = CreateViewModel(paths, dialogs);
            var card = Assert.Single(viewModel.Games);
            Assert.Equal("HK", card.Monogram);
            viewModel.SearchQuery = "knight";
            Assert.False(viewModel.GamesView.IsEmpty);
            dialogs.EditResult = new("Pokémon Azul", card.Model.LaunchPath, LaunchKind.Executable, null, null);

            await viewModel.EditAsync(card);

            Assert.True(viewModel.GamesView.IsEmpty);
            Assert.True(viewModel.ShowNoResults);
            Assert.Equal("PA", card.Monogram);
            viewModel.SearchQuery = "pokemon";
            Assert.False(viewModel.GamesView.IsEmpty);
            Assert.False(viewModel.IsToastVisible);
            Assert.Equal("Pokémon Azul", Assert.Single(new LibraryStore(paths).Load().Games).Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    });

    [Fact]
    public Task Preferences_UpdateOptionalCoverActionWithoutStartupNetworkOrSuccessToast() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            var dialogs = new FakeDialogs();
            using var viewModel = CreateViewModel(paths, dialogs);
            var card = Assert.Single(viewModel.Games);
            Assert.False(card.CanFindCover);
            dialogs.SettingsResult = new AppSettings { SteamGridDbApiKey = "test-key" };
            var notified = false;
            card.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(card.CanFindCover);

            await viewModel.OpenPreferencesAsync();

            Assert.True(card.CanFindCover);
            Assert.True(notified);
            Assert.False(viewModel.IsToastVisible);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    });

    private static MainViewModel CreateViewModel(AppPaths paths, IDialogService dialogs) => new(
        [new Game { Name = "Hollow Knight", LaunchPath = @"C:\game.exe" }], new AppSettings(),
        new LibraryStore(paths), new SettingsStore(paths), new GameImporter(new FakeShortcutResolver()),
        new GameLauncher(), new CoverStore(paths), new SteamGridDbClient(null), dialogs, null);

    [Fact]
    public Task Preferences_UpdateAnimationsSettingAndNotify() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            var dialogs = new FakeDialogs();
            using var viewModel = CreateViewModel(paths, dialogs);
            Assert.True(viewModel.AnimationsEnabled);
            dialogs.SettingsResult = new AppSettings { AnimationsEnabled = false };
            var notified = false;
            viewModel.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(viewModel.AnimationsEnabled);

            await viewModel.OpenPreferencesAsync();

            Assert.False(viewModel.AnimationsEnabled);
            Assert.True(notified);
            Assert.False(new SettingsStore(paths).Load().Settings.AnimationsEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    });

    private sealed class FakeShortcutResolver : IShortcutResolver
    {
        public ShortcutInfo Resolve(string shortcutPath) => throw new NotSupportedException();
    }

    private sealed class FakeDialogs : IDialogService
    {
        public GameEditResult? EditResult { get; set; }
        public AppSettings? SettingsResult { get; set; }
        public GameEditResult? EditGame(Game game) => EditResult;
        public AppSettings? EditPreferences(AppSettings settings) => SettingsResult;
        public bool ConfirmRemove(string gameName) => true;
        public string[]? PickGameFiles() => null;
        public string? PickCoverFile() => null;
        public Task PickCoverFromGalleryAsync(Game game, SteamGridDbClient steamGrid,
            Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
