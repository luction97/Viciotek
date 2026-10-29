using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
using Viciotek.Presentation.Controls;
using Viciotek.Presentation.Dialogs;

namespace Viciotek.Tests;

public sealed class MainViewModelCoverDropTests
{
    [Fact]
    public Task ReplaceCoverFromDrop_UpdatesCardPersistsAndCropsToTwoByThree() =>
        WpfTest.RunAsync(RunAsync);

    [Fact]
    public Task ReplaceCoverFromDrop_RefreshesAnAlreadyVisibleCachedCover() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        Window? window = null;
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            var covers = new CoverStore(paths);
            var game = new Game { Name = "Test", LaunchPath = @"C:\game.exe" };
            var initial = await covers.SetFromBytesAsync(game.Id, TestHelpers.CreatePng(480, 720), null);
            game.CoverFileName = initial.CoverFileName;

            using var viewModel = CreateViewModel(paths, covers, game);
            var card = Assert.Single(viewModel.Games);
            var image = new CoverImage { Loader = new CoverImageLoader(), DataContext = card };
            image.SetBinding(CoverImage.CoverPathProperty, new Binding(nameof(GameCardViewModel.CoverPath)));
            image.SetBinding(CoverImage.CoverVersionProperty, new Binding(nameof(GameCardViewModel.CoverVersion)));
            window = new Window { Content = image, Width = 200, Height = 300, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            await WpfTest.WaitUntilAsync(() => image.Source is BitmapSource);
            var previousBitmap = image.Source;

            var replacement = Path.Combine(directory, "replacement.png");
            await File.WriteAllBytesAsync(replacement, TestHelpers.CreatePng(1200, 600, (_, _) => ((byte)0, (byte)200, (byte)0)));
            await viewModel.ReplaceCoverFromDropAsync(card, replacement);

            await WpfTest.WaitUntilAsync(() => image.Source is BitmapSource && !ReferenceEquals(previousBitmap, image.Source));
            var bitmap = Assert.IsAssignableFrom<BitmapSource>(image.Source);
            var pixel = new byte[4];
            new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(160, 240, 1, 1), pixel, 4, 0);
            Assert.InRange(pixel[1], 150, 255);
            Assert.InRange(pixel[2], 0, 90);

            var greenBitmap = image.Source;
            var greenVersion = card.CoverVersion;
            await File.WriteAllTextAsync(replacement, "invalid image");
            await viewModel.ReplaceCoverFromDropAsync(card, replacement);
            Assert.True(viewModel.ToastIsError);
            Assert.Equal(greenVersion, card.CoverVersion);
            Assert.Same(greenBitmap, image.Source);

            await File.WriteAllBytesAsync(replacement, TestHelpers.CreatePng(600, 1200, (_, _) => ((byte)200, (byte)0, (byte)0)));
            await viewModel.ReplaceCoverFromDropAsync(card, replacement);
            await WpfTest.WaitUntilAsync(() => image.Source is BitmapSource && !ReferenceEquals(greenBitmap, image.Source));
            bitmap = Assert.IsAssignableFrom<BitmapSource>(image.Source);
            new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(160, 240, 1, 1), pixel, 4, 0);
            Assert.InRange(pixel[0], 150, 255);
            Assert.InRange(pixel[1], 0, 90);
            Assert.False(viewModel.ToastIsError);
        }
        finally
        {
            window?.Close();
            Directory.Delete(directory, recursive: true);
        }
    });

    private static MainViewModel CreateViewModel(AppPaths paths, CoverStore covers, Game game) => new(
        [game], new AppSettings(), new LibraryStore(paths), new SettingsStore(paths),
        new GameImporter(new NoopShortcutResolver()), new GameLauncher(), covers,
        new SteamGridDbClient(null), new UnusedDialogService(), startupWarning: null);

    [Fact]
    public Task Initialization_DoesNotFetchCoversEvenWhenConfigured() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            using var handler = new CountingHttpHandler();
            using var viewModel = new MainViewModel(
                [new Game { Name = "Hades" }],
                new AppSettings { AutoFetchCovers = true, SteamGridDbApiKey = "test-not-a-real-key" },
                new LibraryStore(paths), new SettingsStore(paths),
                new GameImporter(new NoopShortcutResolver()), new GameLauncher(), new CoverStore(paths),
                new SteamGridDbClient("test-not-a-real-key", handler), new UnusedDialogService(), startupWarning: null);
            viewModel.ShowPendingNotice();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.True(viewModel.IsSteamGridConfigured);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    });

    private sealed class CountingHttpHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public Task ExpiredToast_CanBeReplacedAndViewModelDisposedRepeatedly() => WpfTest.RunAsync(async () =>
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            using var viewModel = CreateViewModel(paths, new CoverStore(paths), new Game());
            viewModel.ShowImageDropHint();
            await WpfTest.WaitUntilAsync(() => !viewModel.IsToastVisible);

            viewModel.ShowImageDropHint();
            Assert.True(viewModel.IsToastVisible);
            viewModel.Dispose();
            viewModel.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    });

    private static async Task RunAsync()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            var libraryStore = new LibraryStore(paths);
            var settingsStore = new SettingsStore(paths);
            var executable = Path.Combine(directory, "Hades.exe");
            await File.WriteAllTextAsync(executable, "stub");

            var game = new Game
            {
                Name = "Hades",
                LaunchPath = executable,
                LaunchKind = LaunchKind.Executable,
                WorkingDirectory = directory,
                IconPath = executable,
                SortOrder = 0
            };
            await libraryStore.SaveAsync([game]);

            using var viewModel = new MainViewModel(
                [game],
                new AppSettings(),
                libraryStore,
                settingsStore,
                new GameImporter(new NoopShortcutResolver()),
                new GameLauncher(),
                new CoverStore(paths),
                new SteamGridDbClient(null),
                new UnusedDialogService(),
                startupWarning: null);

            var card = Assert.Single(viewModel.Games);
            viewModel.SetCoverDropTarget(card);
            Assert.True(card.IsCoverDropTarget);
            viewModel.SetCoverDropTarget(null);
            Assert.False(card.IsCoverDropTarget);

            var imagePath = Path.Combine(directory, "wide-cover.png");
            await File.WriteAllBytesAsync(
                imagePath,
                TestHelpers.CreatePng(1200, 600, (x, _) =>
                    x < 400 ? ((byte)0, (byte)0, (byte)255)
                    : x < 800 ? ((byte)0, (byte)200, (byte)0)
                    : ((byte)255, (byte)0, (byte)0)));

            await viewModel.ReplaceCoverFromDropAsync(card, imagePath);

            Assert.NotNull(card.Model.CoverFileName);
            Assert.NotNull(card.CoverPath);
            Assert.True(File.Exists(card.CoverPath));
            Assert.Equal((480, 720), TestHelpers.ReadImageSize(card.CoverPath!));
            Assert.False(card.IsBusy);
            Assert.False(viewModel.IsToastVisible);
            Assert.False(viewModel.ToastIsError);

            var reloaded = new LibraryStore(paths).Load().Games;
            var saved = Assert.Single(reloaded);
            Assert.Equal(card.Model.CoverFileName, saved.CoverFileName);

            var center = TestHelpers.ReadPixel(card.CoverPath!, 240, 360);
            Assert.InRange(center.G, 150, 255);
            Assert.InRange(center.R, 0, 90);
            Assert.InRange(center.B, 0, 90);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class NoopShortcutResolver : IShortcutResolver
    {
        public ShortcutInfo Resolve(string shortcutPath) => new(null, null, null);
    }

    private sealed class UnusedDialogService : IDialogService
    {
        public string[]? PickGameFiles() => throw new InvalidOperationException("No se esperaba un diálogo.");

        public string? PickCoverFile() => throw new InvalidOperationException("No se esperaba un diálogo.");

        public Task PickCoverFromGalleryAsync(Game game, SteamGridDbClient steamGrid,
            Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback, CancellationToken cancellationToken) => Task.CompletedTask;

        public GameEditResult? EditGame(Game game) => throw new InvalidOperationException("No se esperaba un diálogo.");

        public bool ConfirmRemove(string gameName) => throw new InvalidOperationException("No se esperaba un diálogo.");

        public AppSettings? EditPreferences(AppSettings settings) => throw new InvalidOperationException("No se esperaba un diálogo.");
    }
}
