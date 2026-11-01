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
    public Task Welcome_SkipPersistsThenImportDoesNotOfferConnectionAgain() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory); paths.EnsureCreated(); var dialogs = new FakeDialogs();
            using var vm = CreateEmptyViewModel(paths, dialogs);
            Assert.True(vm.ShowWelcomeConnection); Assert.False(vm.ShowWelcomeImport);
            await vm.SkipWelcomeConnectionAsync();
            Assert.False(vm.ShowWelcomeConnection); Assert.True(vm.ShowWelcomeImport);
            var saved = new SettingsStore(paths).Load().Settings;
            Assert.True(saved.WelcomeCoverSetupCompleted); Assert.Null(saved.SteamGridDbApiKey);
            using var reopened = CreateEmptyViewModel(paths, dialogs, saved);
            Assert.False(reopened.ShowWelcomeConnection); Assert.True(reopened.ShowWelcomeImport);
            var exe = Path.Combine(directory, "Game.exe"); await File.WriteAllBytesAsync(exe, [0]);
            await vm.HandleDroppedFilesAsync([exe]);
            Assert.Single(vm.Games); Assert.False(vm.ShowCoverSetupHint); Assert.False(vm.ShowWelcomeImport);
            Assert.Equal(0, dialogs.ConnectionRequests);
            await vm.PrepareCloseAsync(); await reopened.PrepareCloseAsync();
        }
        finally { Directory.Delete(directory, true); }
    });

    [Fact]
    public Task Welcome_OnlyAdvancesAfterSavedConnection_AndDisconnectDoesNotRestartIt() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory); paths.EnsureCreated(); var dialogs = new FakeDialogs();
            using var vm = CreateEmptyViewModel(paths, dialogs);
            await vm.ConnectCoversAsync(); // Closing the connection form is not choosing No.
            Assert.True(vm.ShowWelcomeConnection); Assert.False(File.Exists(paths.SettingsFile));
            dialogs.SettingsResult = new AppSettings { SteamGridDbApiKey = "fixture-key" };
            Directory.CreateDirectory(paths.SettingsFile);
            await vm.ConnectCoversAsync();
            Assert.True(vm.ShowWelcomeConnection); Assert.False(vm.IsSteamGridConfigured);
            Directory.Delete(paths.SettingsFile);
            await vm.ConnectCoversAsync();
            Assert.False(vm.ShowWelcomeConnection); Assert.True(vm.ShowWelcomeImport); Assert.Empty(vm.Games);
            Assert.True(new SettingsStore(paths).Load().Settings.WelcomeCoverSetupCompleted);
            dialogs.SettingsResult = new AppSettings();
            await vm.OpenPreferencesAsync();
            Assert.True(vm.ShowWelcomeImport); Assert.False(vm.IsSteamGridConfigured);
            using var reopened = CreateEmptyViewModel(paths, dialogs, new SettingsStore(paths).Load().Settings);
            Assert.True(reopened.ShowWelcomeImport); Assert.False(reopened.ShowWelcomeConnection);
            await vm.PrepareCloseAsync(); await reopened.PrepareCloseAsync();
        }
        finally { Directory.Delete(directory, true); }
    });

    [Fact]
    public Task Welcome_FailedSkipDoesNotAdvance_AndCanBeRetried() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory); paths.EnsureCreated();
            using var vm = CreateEmptyViewModel(paths, new FakeDialogs());
            Directory.CreateDirectory(paths.SettingsFile);
            await vm.SkipWelcomeConnectionAsync();
            Assert.True(vm.ShowWelcomeConnection); Assert.False(vm.ShowWelcomeImport); Assert.True(vm.ToastIsError);
            Directory.Delete(paths.SettingsFile);
            await vm.SkipWelcomeConnectionAsync(); Assert.True(vm.ShowWelcomeImport);
            await vm.PrepareCloseAsync();
        }
        finally { Directory.Delete(directory, true); }
    });

    [Fact]
    public Task Welcome_ExistingConnectionSkipsFirstStepWithoutNetwork() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory); paths.EnsureCreated();
            using var vm = CreateEmptyViewModel(paths, new FakeDialogs(), new AppSettings { SteamGridDbApiKey = "fixture-key" });
            Assert.True(vm.ShowWelcomeImport); Assert.False(vm.ShowWelcomeConnection); Assert.False(File.Exists(paths.SettingsFile));
            await vm.PrepareCloseAsync();
        }
        finally { Directory.Delete(directory, true); }
    });

    private static MainViewModel CreateEmptyViewModel(AppPaths paths, IDialogService dialogs, AppSettings? settings = null) => new(
        [], settings ?? new AppSettings(), new LibraryStore(paths), new SettingsStore(paths), new GameImporter(new FakeShortcutResolver()),
        new RecordingLauncher(), new CoverStore(paths), new SteamGridDbClient(settings?.SteamGridDbApiKey), dialogs, null);

    [Fact]
    public Task WelcomeConnection_UsesDedicatedFlowAndCancelDoesNotChangeSettings() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory); paths.EnsureCreated();
            var dialogs = new FakeDialogs();
            using var vm = CreateViewModel(paths, dialogs);
            await vm.ConnectCoversAsync();
            Assert.Equal(1, dialogs.ConnectionRequests);
            Assert.False(vm.IsSteamGridConfigured);
            Assert.False(File.Exists(paths.SettingsFile));
            dialogs.SettingsResult = new AppSettings { SteamGridDbApiKey = "fixture-key" };
            await vm.ConnectCoversAsync();
            Assert.Equal(2, dialogs.ConnectionRequests);
            Assert.True(vm.IsSteamGridConfigured);
            Assert.True(new SettingsStore(paths).Load().Settings.AutoFetchCovers);
        }
        finally { Directory.Delete(directory, true); }
    });
    [Fact]
    public Task UnexpectedLaunchFailure_IsFriendlyAndAllowsRetry() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory); paths.EnsureCreated();
            using var vm = new MainViewModel([new Game { Name = "Example", LaunchPath = @"C:\fixture.exe" }],
                new AppSettings(), new LibraryStore(paths), new SettingsStore(paths),
                new GameImporter(new FakeShortcutResolver()), new FailingLauncher(), new CoverStore(paths),
                new SteamGridDbClient(null), new FakeDialogs(), null);
            var card = Assert.Single(vm.Games);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await vm.PlayAsync(card);
                Assert.Equal(Viciotek.Localization.Language.Get("FriendlyLaunchFailure"), vm.ToastMessage);
                Assert.DoesNotContain("private-path", vm.ToastMessage);
                Assert.False(card.IsLaunching);
            }
        }
        finally { Directory.Delete(directory, true); }
    });

    private sealed class FailingLauncher : IGameLauncher
    {
        public Task<LaunchResult> LaunchAsync(Game game) => Task.FromException<LaunchResult>(new InvalidOperationException("private-path technical failure"));
    }
    [Fact]
    public Task Welcome_TracksImportSearchAndLastRemoval() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            var dialogs = new FakeDialogs();
            using var viewModel = new MainViewModel([], new AppSettings(), new LibraryStore(paths),
                new SettingsStore(paths), new GameImporter(new FakeShortcutResolver()), new RecordingLauncher(),
                new CoverStore(paths), new SteamGridDbClient(null), dialogs, null);
            Assert.True(viewModel.ShowEmptyState);
            Assert.False(viewModel.ShowCoverSetupHint);
            Assert.False(viewModel.ShowNoResults);
            await viewModel.AddGamesFromDialogAsync();
            Assert.True(dialogs.DirectPick);
            Assert.Empty(viewModel.Games); // Canceling the native picker keeps the welcome.
            var notifications = new List<string?>();
            viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
            var executable = Path.Combine(directory, "Example.exe");
            await File.WriteAllBytesAsync(executable, [0]);
            dialogs.FilesToPick = [executable];
            await viewModel.AddGamesFromDialogAsync();
            Assert.True(dialogs.DirectPick);
            dialogs.FilesToPick = null;
            Assert.False(viewModel.ShowEmptyState);
            Assert.True(viewModel.ShowCoverSetupHint);
            Assert.False(viewModel.ShowNoResults);
            Assert.Contains(nameof(viewModel.ShowEmptyState), notifications);
            viewModel.SearchQuery = "no matching game";
            Assert.False(viewModel.ShowEmptyState);
            Assert.True(viewModel.ShowNoResults);
            await viewModel.AddGamesFromDialogAsync();
            Assert.True(dialogs.DirectPick); // Adding follows the same direct path in a populated library.
            viewModel.ClearSearchCommand.Execute(null);
            Assert.False(viewModel.ShowNoResults);
            await viewModel.RemoveAsync(Assert.Single(viewModel.Games));
            Assert.True(viewModel.ShowEmptyState);
            Assert.False(viewModel.ShowNoResults);
            Assert.False(viewModel.ShowCoverSetupHint);
            dialogs.SettingsResult = new AppSettings { SteamGridDbApiKey = "test-key" };
            await viewModel.OpenPreferencesAsync();
            Assert.True(viewModel.ShowEmptyState);
            Assert.False(viewModel.ShowCoverSetupHint);
            Assert.Contains(nameof(viewModel.ShowCoverSetupHint), notifications);
        }
        finally { Directory.Delete(directory, recursive: true); }
    });

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

    [Fact]
    public Task EquivalentSearchText_DoesNotResetTheGallery() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            using var vm = CreateViewModel(paths, new FakeDialogs());
            var resets = 0;
            ((System.Collections.Specialized.INotifyCollectionChanged)vm.GamesView).CollectionChanged += (_, _) => resets++;
            vm.SearchQuery = "knight";
            var initial = resets;
            vm.SearchQuery = "  KNÍGHT  ";
            Assert.Equal(initial, resets);
            vm.ClearSearchCommand.Execute(null);
            Assert.False(vm.ShowNoResults);
            await Task.CompletedTask;
        }
        finally { Directory.Delete(directory, true); }
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
    public Task Preferences_UpdateOptionalCoverActionAndConfirmSavedConnection() => WpfTest.RunAsync(async () =>
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
            Assert.True(viewModel.IsToastVisible);
            Assert.Equal(Viciotek.Localization.Language.Get("SteamGridConnected"), viewModel.ToastMessage);
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
            Assert.Equal(280, viewModel.PreferredCoverWidth);
            viewModel.PreviewCoverSize(0);
            Assert.Equal(200, viewModel.PreferredCoverWidth);
            Assert.Equal(2, new AppSettings().CoverSizePreset);
            await viewModel.OpenPreferencesAsync(); // canceled: discard the live preview
            Assert.Equal(280, viewModel.PreferredCoverWidth);
            dialogs.SettingsResult = new AppSettings { AnimationsEnabled = false, CoverSizePreset = 3 };
            var notified = false;
            viewModel.PropertyChanged += (_, args) => notified |= args.PropertyName == nameof(viewModel.AnimationsEnabled);

            await viewModel.OpenPreferencesAsync();

            Assert.False(viewModel.AnimationsEnabled);
            Assert.True(notified);
            Assert.False(new SettingsStore(paths).Load().Settings.AnimationsEnabled);
            Assert.Equal(320, viewModel.PreferredCoverWidth);
            Assert.Equal(480, viewModel.PreferredCoverHeight);
            Assert.Equal(3, new SettingsStore(paths).Load().Settings.CoverSizePreset);
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
        public int ConnectionRequests { get; private set; }
        public AppSettings? ConnectCovers(AppSettings settings) { ConnectionRequests++; return SettingsResult; }
        public bool DirectPick { get; private set; }
        public string[]? FilesToPick { get; set; }
        public string[]? PickGameFiles(bool directly) { DirectPick = directly; return FilesToPick; }
        public GameEditResult? EditResult { get; set; }
        public AppSettings? SettingsResult { get; set; }
        public GameEditResult? EditGame(Game game) => EditResult;
        public AppSettings? EditPreferences(AppSettings settings) => SettingsResult;
        public bool ConfirmRemove(string gameName) => true;
        public IReadOnlyList<Viciotek.Application.DetectedGame>? ReviewImport(IReadOnlyList<Viciotek.Application.DetectedGame> games) => games;
        public string[]? PickGameFiles() => null;
        public string? PickCoverFile() => null;
        public Task PickCoverFromGalleryAsync(Game game, SteamGridDbClient steamGrid,
            Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
