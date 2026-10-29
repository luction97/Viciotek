using System.Windows.Media;
using System.Windows.Media.Imaging;
using Viciotek.Infrastructure.Caching;

namespace Viciotek.Infrastructure.Imaging;

/// <summary>
/// Loads cached cover thumbnails off the UI thread at display resolution.
/// The small LRU keeps scrolling smooth without accumulating full-size images.
/// </summary>
public sealed class CoverImageLoader
{
    private const int DecodePixelWidth = 320;
    private readonly LruCache<(string Path, string? Version), BitmapSource> _cache = new(32);
    private readonly SemaphoreSlim _decodeGate = new(2, 2);
    private readonly Func<string, BitmapSource?> _decode;
    private readonly CoverStore? _covers;

    public CoverImageLoader() : this(Decode)
    {
    }

    public CoverImageLoader(CoverStore covers) : this(Decode) => _covers = covers;

    internal CoverImageLoader(Func<string, BitmapSource?> decode) => _decode = decode;

    public Task<BitmapSource?> LoadAsync(string? path, CancellationToken cancellationToken = default) =>
        LoadAsync(path, version: null, cancellationToken);

    public async Task<BitmapSource?> LoadAsync(string? path, string? version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // Thumbnail paths are stable per game. The original cover's unique
        // filename is the revision, so replacement never reuses stale pixels.
        var key = (path, version);
        if (_cache.TryGet(key, out var cached))
        {
            return cached;
        }

        // Limit transient WIC buffers and skip queued work for scrolled-away
        // cards. File I/O stays off the UI thread, including missing files.
        await _decodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGet(key, out cached))
            {
                return cached;
            }

            if (_covers is not null && !string.IsNullOrWhiteSpace(version))
            {
                try
                {
                    path = await _covers.EnsureThumbnailAsync(version, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is CoverImageException or IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            }

            var image = await Task.Run(() => _decode(path), cancellationToken).ConfigureAwait(false);
            if (image is not null)
            {
                _cache.Set(key, image);
            }

            return image;
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private static BitmapSource? Decode(string path)
    {
        try
        {
            // Read the file into memory first: decoding directly from the path
            // can keep a file handle open if the image is malformed, blocking
            // later replacement. OnLoad decodes eagerly, so the stream can be
            // disposed as soon as the independent bitmap is built.
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.DecodePixelWidth = DecodePixelWidth;
            image.EndInit();
            image.Freeze();
            return Detach(image);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException
                or FileFormatException
                or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies the decoded pixels into an independent bitmap. BitmapImage can
    /// retain its full-resolution WIC source even when DecodePixelWidth is set;
    /// detaching avoids keeping two copies of every visible cover in memory.
    /// </summary>
    private static BitmapSource Detach(BitmapImage image)
    {
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var format = PixelFormats.Bgra32;
        var stride = (width * format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * height];

        BitmapSource source = image;
        if (image.Format != format)
        {
            source = new FormatConvertedBitmap(image, format, null, 0);
        }

        source.CopyPixels(pixels, stride, 0);
        var detached = BitmapSource.Create(
            width,
            height,
            image.DpiX,
            image.DpiY,
            format,
            null,
            pixels,
            stride);
        detached.Freeze();
        return detached;
    }
}
