using System.Net;
using System.Net.Http;
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

public sealed class GameIdentificationFlowTests
{
    [Theory]
    [InlineData("exe")]
    [InlineData("url")]
    public Task DroppedGame_UsesProviderSupportedDimensions_AndPersistsCover(string extension) => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture(response: request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("grids/game/") &&
                Uri.UnescapeDataString(request.RequestUri.Query).Contains("300x450", StringComparison.Ordinal))
                return new(HttpStatusCode.BadRequest) { Content = new StringContent("""{"success":false,"errors":["Invalid asset dimensions specified"]}""") };
            return DefaultResponse(request);
        });
        var path = extension == "exe" ? fixture.CreateExecutable("MK.exe") : Path.Combine(fixture.Paths.Root, "Mortal Kombat 1.url");
        if (extension == "url") await File.WriteAllTextAsync(path, "[InternetShortcut]\nURL=steam://run/123");

        await fixture.ViewModel.HandleDroppedFilesAsync([path]);
        await WpfTest.WaitUntilAsync(() => fixture.Handler.Requests.Any(uri => uri.AbsolutePath.Contains("grids/game/")));
        var request = fixture.Handler.Requests.Single(uri => uri.AbsolutePath.Contains("grids/game/"));
        Assert.DoesNotContain("300x450", Uri.UnescapeDataString(request.Query));
        Assert.Contains("660x930", Uri.UnescapeDataString(request.Query));
        var card = Assert.Single(fixture.ViewModel.Games);
        await WpfTest.WaitUntilAsync(() => card.Model.CoverFileName is not null);
        Assert.Equal(card.Model.CoverFileName, Assert.Single(fixture.Library.Load().Games).CoverFileName);
        Assert.True(File.Exists(card.CoverPath));
        Assert.Equal(0, fixture.Dialogs.Count);
    });

    [Fact]
    public Task Import_AutoDisabled_AddsResolvedTitleWithNoNetworkOrModal() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture(autoFetch: false);
        await fixture.ImportAsync("MK.exe");
        var game = Assert.Single(fixture.ViewModel.Games).Model;
        Assert.Equal("Mortal Kombat 1", game.Name);
        Assert.Equal("MK.exe", game.ExecutableName);
        Assert.Empty(fixture.Handler.Requests);
        Assert.Equal(0, fixture.Dialogs.Count);
        Assert.Equal(game.Name, Assert.Single(fixture.Library.Load().Games).Name);
    });

    [Fact]
    public Task Drop_WithoutConfiguredKey_ExplainsWhyNoCoverSearchRuns() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture(apiKey: null);
        await fixture.ViewModel.HandleDroppedFilesAsync([fixture.CreateExecutable("MK.exe")]);
        Assert.Single(fixture.ViewModel.Games);
        Assert.Empty(fixture.Handler.Requests);
        Assert.True(fixture.ViewModel.IsToastVisible);
        Assert.Contains("Configurá SteamGridDB", fixture.ViewModel.ToastMessage);
    });

    [Fact]
    public Task Drop_Duplicate_DoesNotDuplicateGameOrRedownloadExistingCover() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture();
        var path = fixture.CreateExecutable("MK.exe");
        await fixture.ViewModel.HandleDroppedFilesAsync([path]);
        var card = Assert.Single(fixture.ViewModel.Games);
        await WpfTest.WaitUntilAsync(() => card.Model.CoverFileName is not null && !card.IsFindingCover);
        var cover = card.Model.CoverFileName;
        var requests = fixture.Handler.Requests.Count;
        await fixture.ViewModel.HandleDroppedFilesAsync([path]);
        Assert.Single(fixture.ViewModel.Games);
        Assert.Equal(cover, card.Model.CoverFileName);
        Assert.Equal(requests, fixture.Handler.Requests.Count);
        Assert.Contains("ya está", fixture.ViewModel.ToastMessage);
    });

    [Fact]
    public Task Import_AutoEnabled_AppliesMatchingCoverAndAssociation_WithoutChangingExecution() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture();
        var path = await fixture.ImportAsync("MK.exe");
        var card = Assert.Single(fixture.ViewModel.Games);
        await WpfTest.WaitUntilAsync(() => card.Model.CoverFileName is not null);
        var saved = Assert.Single(fixture.Library.Load().Games);
        Assert.Equal("Mortal Kombat 1", saved.Name);
        Assert.Equal(path, saved.LaunchPath);
        Assert.Equal(42, saved.SteamGridDbGameId);
        Assert.Equal("Mortal Kombat 1", saved.CoverSearchName);
        Assert.True(File.Exists(card.CoverPath));
        Assert.Equal(0, fixture.Dialogs.Count);

        var count = fixture.Handler.Requests.Count;
        await fixture.ViewModel.FindCoverAsync(card);
        Assert.Equal(count, fixture.Handler.Requests.Count); // Already has a local cover.
    });

    [Fact]
    public Task EmptyResults_OpensManualFallback_AfterGameIsPersisted_CancelPreservesGame() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture(response: _ => Json("""{"success":true,"data":[]}"""));
        fixture.Dialogs.OnOpen = (game, _, manual, _) =>
        {
            Assert.True(manual);
            Assert.False(fixture.ViewModel.IsImporting);
            Assert.Equal(game.Id, Assert.Single(fixture.Library.Load().Games).Id);
            return Task.CompletedTask; // User cancels.
        };
        await fixture.ImportAsync("MK.exe");
        await WpfTest.WaitUntilAsync(() => fixture.Dialogs.Count == 1 && !fixture.ViewModel.Games[0].IsBusy);
        Assert.Null(fixture.ViewModel.Games[0].Model.CoverFileName);
        Assert.Equal("Mortal Kombat 1", Assert.Single(fixture.Library.Load().Games).Name);
    });

    [Fact]
    public Task MissingVerticalGrids_AlsoOpensManualFallback() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture(response: request => request.RequestUri!.AbsolutePath.Contains("search/")
            ? Json("""{"success":true,"data":[{"id":42,"name":"Mortal Kombat 1"}]}""")
            : Json("""{"success":true,"data":[]}"""));
        await fixture.ImportAsync("MK.exe");
        await WpfTest.WaitUntilAsync(() => fixture.Dialogs.Count == 1);
        Assert.True(fixture.Dialogs.LastWasFallback);
        Assert.DoesNotContain(fixture.Handler.Requests, uri => uri.Host == "cdn.example.com");
    });

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public Task HttpErrors_DoNotOpenFallbackOrRetryBatch(HttpStatusCode status) => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture(response: _ => new(status));
        await fixture.ImportAsync("MK.exe");
        await WpfTest.WaitUntilAsync(() => fixture.ViewModel.ToastIsError);
        Assert.Equal(0, fixture.Dialogs.Count);
        Assert.Single(fixture.Handler.Requests);
        Assert.Single(fixture.Library.Load().Games);
    });

    [Fact]
    public Task BatchFallbacks_AreSequential_AndDoNotDelayAddingTheBatch() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture(response: _ => Json("""{"success":true,"data":[]}"""));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrent = 0;
        var maximum = 0;
        fixture.Dialogs.OnOpen = async (_, _, _, token) =>
        {
            maximum = Math.Max(maximum, ++concurrent);
            Assert.Equal(2, fixture.Library.Load().Games.Count);
            try { await release.Task.WaitAsync(token); }
            finally { concurrent--; }
        };
        var first = fixture.CreateExecutable("MK.exe");
        var second = fixture.CreateExecutable("RE9.exe");
        await fixture.ViewModel.ImportFilesAsync([first, second]);
        await WpfTest.WaitUntilAsync(() => fixture.Dialogs.Count == 1);
        Assert.Equal(2, fixture.ViewModel.Games.Count);
        Assert.False(fixture.ViewModel.IsImporting);
        release.SetResult();
        await WpfTest.WaitUntilAsync(() => fixture.Dialogs.Count == 2 && concurrent == 0);
        Assert.Equal(1, maximum);
    });

    [Fact]
    public Task ManualSelection_PersistsBeforeDialogCompletes_PreservesCustomNameAndLaunchData() => WpfTest.RunAsync(async () =>
    {
        var game = new Game { Name = "Mi nombre personalizado", LaunchPath = @"C:\Games\MK.exe", Arguments = "--windowed",
            WorkingDirectory = @"C:\Games", IsNameCustomized = true };
        await using var fixture = new Fixture(games: [game]);
        var image = TestHelpers.CreatePng(400, 600);
        fixture.Dialogs.OnOpen = async (_, apply, manual, token) =>
        {
            Assert.False(manual);
            Assert.True(await apply(new(image, new(42, "Mortal Kombat 1"), "Mortal Kombat 1"), token));
            Assert.Equal(42, Assert.Single(fixture.Library.Load().Games).SteamGridDbGameId);
        };
        var card = Assert.Single(fixture.ViewModel.Games);
        await fixture.ViewModel.SearchCoverAsync(card);
        var reloaded = Assert.Single(new LibraryStore(fixture.Paths).Load().Games);
        Assert.Equal(game.Name, reloaded.Name);
        Assert.Equal(game.Arguments, reloaded.Arguments);
        Assert.Equal(game.WorkingDirectory, reloaded.WorkingDirectory);
        Assert.Equal(game.LaunchPath, reloaded.LaunchPath);
        Assert.NotNull(reloaded.CoverFileName);
        Assert.True(File.Exists(card.CoverPath));
        Assert.Equal((480, 720), TestHelpers.ReadImageSize(card.CoverPath!));
    });

    [Fact]
    public Task FailedManualCommit_DoesNotAdoptIdentityOrDiscardExistingCustomCover() => WpfTest.RunAsync(async () =>
    {
        var game = new Game { Name = "Personalizado", LaunchPath = @"C:\Games\MK.exe", CoverFileName = "custom.png" };
        await using var fixture = new Fixture(games: [game]);
        await fixture.Library.SaveAsync([game]);
        var image = TestHelpers.CreatePng(400, 600);
        fixture.Dialogs.OnOpen = async (_, apply, _, token) =>
        {
            using var fileLock = File.Open(fixture.Paths.LibraryFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.False(await apply(new(image, new(42, "Mortal Kombat 1"), "Mortal Kombat 1"), token));
        };
        await fixture.ViewModel.SearchCoverAsync(fixture.ViewModel.Games[0]);
        Assert.Equal("custom.png", game.CoverFileName);
        Assert.Null(game.SteamGridDbGameId);
        Assert.Empty(Directory.GetFiles(fixture.Paths.CoversDirectory));
    });

    [Fact]
    public Task ExistingCustomCover_AutomaticActionDoesNotQueryOrReplace() => WpfTest.RunAsync(async () =>
    {
        var game = new Game { Name = "Personalizado", CoverFileName = "custom.png" };
        await using var fixture = new Fixture(games: [game]);
        await fixture.ViewModel.FindCoverAsync(fixture.ViewModel.Games[0]);
        Assert.Equal("custom.png", game.CoverFileName);
        Assert.Empty(fixture.Handler.Requests);
        Assert.Equal(0, fixture.Dialogs.Count);
    });

    [Fact]
    public Task UserCoverChosenDuringLookup_Wins_AndConcurrentLookupIsNotRepeated() => WpfTest.RunAsync(async () =>
    {
        var game = new Game { Name = "MK", LaunchPath = @"C:\Games\MK.exe" };
        await using var fixture = new Fixture(games: [game]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Intercept = async (request, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return DefaultResponse(request);
        };
        var card = fixture.ViewModel.Games[0];
        var search = fixture.ViewModel.FindCoverAsync(card);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(card.IsFindingCover);
            Assert.True(card.IsWorking);
            Assert.False(card.IsBusy); // Local customization must remain available during network lookup.
            Assert.Equal("Buscando portada…", card.ActivityLabel);
            await fixture.ViewModel.FindCoverAsync(card);
            Assert.Single(fixture.Handler.Requests);
            var image = Path.Combine(fixture.Paths.Root, "my-cover.png");
            await File.WriteAllBytesAsync(image, TestHelpers.CreatePng(400, 600));
            await fixture.ViewModel.ReplaceCoverFromDropAsync(card, image);
            var selectedCover = card.Model.CoverFileName;
            release.SetResult();
            await search;
            Assert.False(card.IsFindingCover);
            Assert.Equal(selectedCover, card.Model.CoverFileName);
            Assert.Equal(selectedCover, Assert.Single(fixture.Library.Load().Games).CoverFileName);
            Assert.Null(card.Model.SteamGridDbGameId);
            Assert.Equal(0, fixture.Dialogs.Count);
        }
        finally { release.TrySetResult(); }
    });

    [Fact]
    public Task LegacyExecutableName_IsResolvedOnlyOnExplicitRequest_WithoutRenamingIt() => WpfTest.RunAsync(async () =>
    {
        var game = new Game { Name = "MK", LaunchPath = @"C:\Games\MK.exe" };
        await using var fixture = new Fixture(games: [game]);
        Assert.Empty(fixture.Handler.Requests);
        await fixture.ViewModel.FindCoverAsync(fixture.ViewModel.Games[0]);
        Assert.Equal("MK", game.Name);
        Assert.Equal("Mortal Kombat 1", game.CoverSearchName);
        Assert.Equal(42, game.SteamGridDbGameId);
        Assert.Contains(fixture.Handler.Requests, uri => Uri.UnescapeDataString(uri.AbsolutePath).EndsWith("Mortal Kombat 1"));
        Assert.Equal("MK", Assert.Single(fixture.Library.Load().Games).Name);
    });

    [Fact]
    public Task EditingTitle_InvalidatesAssociationButKeepsCustomCoverAndAllowsNewSearch() => WpfTest.RunAsync(async () =>
    {
        var game = new Game { Name = "Old title", LaunchPath = @"C:\MK.exe", CoverFileName = "custom.png",
            SteamAppId = 123, SteamGridDbGameId = 42, SteamGridDbName = "Old title", CoverSearchName = "Old title" };
        await using var fixture = new Fixture(games: [game]);
        fixture.Dialogs.EditResult = new("Mortal Kombat 1", game.LaunchPath, LaunchKind.Executable, null, null);
        await fixture.ViewModel.EditAsync(fixture.ViewModel.Games[0]);
        var saved = Assert.Single(fixture.Library.Load().Games);
        Assert.True(saved.IsNameCustomized);
        Assert.Equal("Mortal Kombat 1", saved.CoverSearchName);
        Assert.Null(saved.SteamGridDbGameId);
        Assert.Equal("custom.png", saved.CoverFileName);
        Assert.Equal(@"C:\MK.exe", saved.LaunchPath);
        fixture.Dialogs.OnOpen = (copy, _, _, _) =>
        {
            Assert.Equal("Mortal Kombat 1", copy.CoverSearchName);
            return Task.CompletedTask;
        };
        await fixture.ViewModel.SearchCoverAsync(fixture.ViewModel.Games[0]);
        Assert.Equal(1, fixture.Dialogs.Count);
    });

    [Fact]
    public Task CloseWhileFallbackIsOpen_CancelsAndDrains() => WpfTest.RunAsync(async () =>
    {
        await using var fixture = new Fixture(response: _ => Json("""{"success":true,"data":[]}"""));
        fixture.Dialogs.OnOpen = (_, _, _, token) => Task.Delay(Timeout.InfiniteTimeSpan, token);
        await fixture.ImportAsync("MK.exe");
        await WpfTest.WaitUntilAsync(() => fixture.Dialogs.Count == 1);
        await fixture.ViewModel.PrepareCloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fixture.ViewModel.Games[0].IsBusy);
        Assert.Single(fixture.Library.Load().Games);
    });

    [Fact]
    public Task GalleryFallback_StartsEmptyWithoutNetwork_AndManualSearchCanSucceed() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler(DefaultResponse);
        using var client = new SteamGridDbClient("fixture-key", handler);
        var applied = false;
        var gallery = new CoverGalleryViewModel(client, new Game { Name = "MK" }, (selection, _) =>
        {
            applied = true;
            Assert.Equal("Mortal Kombat 1", selection.SearchName);
            Assert.Equal(42, selection.Game.Id);
            return Task.FromResult(true);
        }, manualFallback: true);
        try
        {
            await gallery.InitializeAsync();
            Assert.Equal(string.Empty, gallery.SearchQuery);
            Assert.Contains("automáticamente", gallery.Status);
            Assert.Empty(handler.Requests);
            gallery.SearchQuery = "Mortal Kombat 1";
            await gallery.SearchAsync();
            var candidate = Assert.Single(gallery.Candidates);
            await WpfTest.WaitUntilAsync(() => candidate.Thumbnail is not null);
            Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.EndsWith("full.png"));
            gallery.Selected = candidate;
            await gallery.ConfirmAsync();
            Assert.True(applied);
            Assert.Single(handler.Requests, uri => uri.AbsolutePath.EndsWith("full.png"));
        }
        finally { await gallery.CloseAsync(); }
    });

    [Fact]
    public Task Gallery_AmbiguousResultsRequireExplicitGameSelection() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath.Contains("search/")
            ? Json("""{"success":true,"data":[{"id":1,"name":"Prey"},{"id":2,"name":"Prey"}]}""")
            : DefaultResponse(request));
        using var client = new SteamGridDbClient("fixture-key", handler);
        var gallery = new CoverGalleryViewModel(client, new Game { Name = "Prey" }, (_, _) => Task.FromResult(true));
        try
        {
            await gallery.SearchAsync();
            Assert.Equal(2, gallery.Matches.Count);
            Assert.Empty(gallery.Candidates);
            Assert.Null(gallery.SelectedGame);
            Assert.Single(handler.Requests);
            await gallery.SelectGameAsync(gallery.Matches[1]);
            Assert.Equal(2, gallery.SelectedGame?.Id);
            Assert.Contains(handler.Requests, uri => uri.AbsolutePath.EndsWith("grids/game/2"));
        }
        finally { await gallery.CloseAsync(); }
    });

    [Fact]
    public Task ConfirmedGallery_ReusesIdWithoutTextSearch_AndCancellationPreservesSelection() => WpfTest.RunAsync(async () =>
    {
        using var handler = new Handler(DefaultResponse);
        using var client = new SteamGridDbClient("fixture-key", handler);
        var game = new Game { Name = "Custom name", CoverFileName = "custom.png", SteamGridDbGameId = 42,
            SteamGridDbName = "Mortal Kombat 1", CoverSearchName = "Mortal Kombat 1" };
        var applied = false;
        var gallery = new CoverGalleryViewModel(client, game, (_, _) => { applied = true; return Task.FromResult(true); });
        await gallery.InitializeAsync();
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("search/"));
        await gallery.CloseAsync();
        Assert.False(applied);
        Assert.Equal("custom.png", game.CoverFileName);
        Assert.Equal("Custom name", game.Name);
    });

    private static HttpResponseMessage DefaultResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("search/")) return Json("""{"success":true,"data":[{"id":42,"name":"Mortal Kombat 1"}]}""");
        if (path.Contains("grids/game/")) return Json("""{"success":true,"data":[{"id":1,"url":"https://cdn.example.com/full.png","thumb":"https://cdn.example.com/thumb.png","width":600,"height":900}]}""");
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(TestHelpers.CreatePng(160, 240)) };
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class Fixture : IAsyncDisposable
    {
        public AppPaths Paths { get; } = new(TestHelpers.CreateTempDirectory());
        public LibraryStore Library { get; }
        public MainViewModel ViewModel { get; }
        public Dialogs Dialogs { get; } = new();
        public Handler Handler { get; }

        public Fixture(bool autoFetch = true, Func<HttpRequestMessage, HttpResponseMessage>? response = null,
            IReadOnlyList<Game>? games = null, string? apiKey = "fixture-key")
        {
            Paths.EnsureCreated();
            Library = new(Paths);
            Handler = new(response ?? DefaultResponse);
            var importer = new GameImporter(new NoShortcuts(), new GameIdentityResolver(new Metadata()));
            ViewModel = new(games ?? [], new AppSettings { AutoFetchCovers = autoFetch }, Library, new SettingsStore(Paths),
                importer, new GameLauncher(), new CoverStore(Paths), new SteamGridDbClient(apiKey, Handler), Dialogs, null);
        }
        public string CreateExecutable(string name)
        {
            var path = Path.Combine(Paths.Root, name);
            File.WriteAllText(path, "not executed");
            return path;
        }
        public async Task<string> ImportAsync(string name)
        {
            var path = CreateExecutable(name);
            await ViewModel.ImportFilesAsync([path]);
            return path;
        }
        public async ValueTask DisposeAsync()
        {
            await ViewModel.PrepareCloseAsync();
            Handler.Dispose();
            Directory.Delete(Paths.Root, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Intercept { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests.Add(request.RequestUri!); return Intercept?.Invoke(request, cancellationToken) ?? Task.FromResult(respond(request)); }
    }
    private sealed class NoShortcuts : IShortcutResolver
    { public ShortcutInfo Resolve(string shortcutPath) => throw new NotSupportedException(); }
    private sealed class Metadata : IGameMetadataReader
    { public LocalGameMetadata Read(Game game) => new(InstallationFolder: "Mortal Kombat 1"); }

    private sealed class Dialogs : IDialogService
    {
        public int Count { get; private set; }
        public bool LastWasFallback { get; private set; }
        public GameEditResult? EditResult { get; set; }
        public Func<Game, Func<CoverSelection, CancellationToken, Task<bool>>, bool, CancellationToken, Task>? OnOpen { get; set; }
        public async Task PickCoverFromGalleryAsync(Game game, SteamGridDbClient client,
            Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback, CancellationToken cancellationToken)
        {
            Count++;
            LastWasFallback = manualFallback;
            if (OnOpen is not null) await OnOpen(game, apply, manualFallback, cancellationToken);
        }
        public string[]? PickGameFiles() => null;
        public string? PickCoverFile() => null;
        public GameEditResult? EditGame(Game game) => EditResult;
        public bool ConfirmRemove(string gameName) => false;
        public AppSettings? EditPreferences(AppSettings settings) => null;
    }
}
