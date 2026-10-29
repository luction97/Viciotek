using Viciotek.Domain;
using Viciotek.Infrastructure.Caching;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Storage;

namespace Viciotek.Tests;

public sealed class CoverStoreTests : IDisposable
{
    private readonly string _directory = TestHelpers.CreateTempDirectory();
    private readonly AppPaths _paths;
    private readonly CoverStore _store;

    public CoverStoreTests()
    {
        _paths = new AppPaths(_directory);
        _paths.EnsureCreated();
        _store = new CoverStore(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(1200, 900)]
    [InlineData(600, 1200)]
    [InlineData(1000, 1000)]
    [InlineData(480, 720)]
    public async Task SetFromBytes_AlwaysProducesExact2By3Thumbnail(int width, int height)
    {
        var gameId = Guid.NewGuid();
        var bytes = TestHelpers.CreatePng(width, height);

        var update = await _store.SetFromBytesAsync(gameId, bytes, null);

        Assert.True(File.Exists(update.ThumbnailPath));
        Assert.True(File.Exists(Path.Combine(_paths.CoversDirectory, update.CoverFileName)));
        Assert.Equal((480, 720), TestHelpers.ReadImageSize(update.ThumbnailPath));
    }

    [Fact]
    public async Task SetFromBytes_LandscapeImage_IsCenterCropped()
    {
        var gameId = Guid.NewGuid();
        var bytes = TestHelpers.CreatePng(1200, 600, (x, _) =>
            x < 400 ? ((byte)0, (byte)0, (byte)255)
            : x < 800 ? ((byte)0, (byte)200, (byte)0)
            : ((byte)255, (byte)0, (byte)0));

        var update = await _store.SetFromBytesAsync(gameId, bytes, null);
        var pixel = TestHelpers.ReadPixel(update.ThumbnailPath, 240, 360);

        Assert.InRange(pixel.G, 150, 255);
        Assert.InRange(pixel.R, 0, 90);
        Assert.InRange(pixel.B, 0, 90);
    }

    [Fact]
    public async Task SetFromBytes_PreparesIndependentFilesWithoutDeletingTheCommittedCover()
    {
        var gameId = Guid.NewGuid();
        var first = await _store.SetFromBytesAsync(gameId, TestHelpers.CreatePng(480, 720), null);
        var firstPath = Path.Combine(_paths.CoversDirectory, first.CoverFileName);
        Assert.True(File.Exists(firstPath));

        var second = await _store.SetFromBytesAsync(gameId, TestHelpers.CreatePng(720, 480), first.CoverFileName);

        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(first.ThumbnailPath));
        Assert.NotEqual(first.ThumbnailPath, second.ThumbnailPath);
        Assert.True(File.Exists(Path.Combine(_paths.CoversDirectory, second.CoverFileName)));
        Assert.Equal((480, 720), TestHelpers.ReadImageSize(second.ThumbnailPath));
    }

    [Fact]
    public async Task EnsureThumbnail_RegeneratesMissingCacheFromOriginalWithoutChangingItsName()
    {
        var game = new Game();
        var update = await _store.SetFromBytesAsync(game.Id, TestHelpers.CreatePng(1200, 600), null);
        game.CoverFileName = update.CoverFileName;
        File.Delete(update.ThumbnailPath);
        var loader = new CoverImageLoader(_store);
        var bitmap = await loader.LoadAsync(_store.ThumbnailPathFor(game), game.CoverFileName);
        Assert.NotNull(bitmap);
        Assert.Equal((320, 480), (bitmap.PixelWidth, bitmap.PixelHeight));
        Assert.Equal((480, 720), TestHelpers.ReadImageSize(update.ThumbnailPath));
        Assert.True(File.Exists(Path.Combine(_paths.CoversDirectory, update.CoverFileName)));
    }

    [Fact]
    public async Task EnsureThumbnail_LegacyLibraryWithOnlyOriginalGetsARevisionedCache()
    {
        var game = new Game();
        game.CoverFileName = $"{game.Id:N}-{Guid.NewGuid():N}.png";
        await File.WriteAllBytesAsync(Path.Combine(_paths.CoversDirectory, game.CoverFileName), TestHelpers.CreatePng(600, 1200));
        var regenerated = await _store.EnsureThumbnailAsync(game.CoverFileName);
        Assert.Equal(_store.ThumbnailPathFor(game), regenerated);
        Assert.Equal((480, 720), TestHelpers.ReadImageSize(regenerated));
    }

    [Fact]
    public async Task SetFromBytes_InvalidDataThrowsFriendlyException()
    {
        var exception = await Assert.ThrowsAsync<CoverImageException>(
            () => _store.SetFromBytesAsync(Guid.NewGuid(), [1, 2, 3, 4], null));

        Assert.Contains("imagen", exception.Message);
    }

    [Fact]
    public async Task SetFromFile_MissingFileThrowsFriendlyException()
    {
        var exception = await Assert.ThrowsAsync<CoverImageException>(
            () => _store.SetFromFileAsync(Guid.NewGuid(), Path.Combine(_directory, "gone.png"), null));

        Assert.Contains("no existe", exception.Message);
    }

    [Fact]
    public async Task DeleteFiles_RemovesCoverAndThumbnail()
    {
        var game = new Game { Id = Guid.NewGuid() };
        var update = await _store.SetFromBytesAsync(game.Id, TestHelpers.CreatePng(480, 720), null);
        game.CoverFileName = update.CoverFileName;

        _store.DeleteFiles(game);

        Assert.False(File.Exists(Path.Combine(_paths.CoversDirectory, update.CoverFileName)));
        Assert.False(File.Exists(update.ThumbnailPath));
    }

    [Fact]
    public async Task ThumbnailPathFor_UsesCoverRevision()
    {
        var game = new Game { Id = Guid.Parse("11111111-2222-3333-4444-555555555555") };
        var update = await _store.SetFromBytesAsync(game.Id, TestHelpers.CreatePng(480, 720), null);
        game.CoverFileName = update.CoverFileName;

        Assert.Equal(
            update.ThumbnailPath,
            _store.ThumbnailPathFor(game));
    }
}

public sealed class CoverImageLoaderTests : IDisposable
{
    private readonly string _directory = TestHelpers.CreateTempDirectory();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_BoundsDecodeConcurrencyAndSkipsCancelledQueuedCards()
    {
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4);
        bitmap.Freeze();
        var loader = new CoverImageLoader(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("No se libero el decoder de prueba.");
            }
            return bitmap;
        });
        using var cancellation = new CancellationTokenSource();
        var active = new[] { loader.LoadAsync("first"), loader.LoadAsync("second") };
        var queued = Enumerable.Range(0, 6).Select(i => loader.LoadAsync($"queued-{i}", cancellation.Token)).ToArray();
        try
        {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
            Assert.Equal(2, Volatile.Read(ref calls));
            cancellation.Cancel();
            foreach (var task in queued)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            }
        }
        finally
        {
            release.Set();
        }
        await Task.WhenAll(active);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task LoadAsync_DecodesAtDisplayWidthAndCaches()
    {
        var path = Path.Combine(_directory, "cover.png");
        await File.WriteAllBytesAsync(path, TestHelpers.CreatePng(480, 720));
        var loader = new CoverImageLoader();

        var first = await loader.LoadAsync(path);
        var second = await loader.LoadAsync(path);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(320, first.PixelWidth);
        Assert.Equal(480, first.PixelHeight);
        Assert.True(first.IsFrozen);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task LoadAsync_NewVersionReloadsAnOverwrittenThumbnailWithoutLockingTheFile()
    {
        var path = Path.Combine(_directory, "cover.png");
        await File.WriteAllBytesAsync(path, TestHelpers.CreatePng(480, 720));
        var loader = new CoverImageLoader();
        var first = await loader.LoadAsync(path, "v1");

        await File.WriteAllBytesAsync(path, TestHelpers.CreatePng(480, 720, (_, _) => ((byte)0, (byte)200, (byte)0)));
        var second = await loader.LoadAsync(path, "v2");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Same(second, await loader.LoadAsync(path, "v2"));
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task LoadAsync_BadFileReturnsNull()
    {
        var path = Path.Combine(_directory, "bad.png");
        await File.WriteAllTextAsync(path, "not an image");
        var loader = new CoverImageLoader();

        Assert.Null(await loader.LoadAsync(path));
        Assert.Null(await loader.LoadAsync(Path.Combine(_directory, "missing.png")));
        Assert.Null(await loader.LoadAsync(null));
    }
}

public sealed class LruCacheTests
{
    [Fact]
    public void Set_EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGet("a", out _));

        cache.Set("c", 3);

        Assert.True(cache.TryGet("a", out var a));
        Assert.Equal(1, a);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Set_UpdatesExistingValueWithoutGrowing()
    {
        var cache = new LruCache<string, int>(1);
        cache.Set("a", 1);
        cache.Set("a", 2);

        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(2, value);
    }
}
