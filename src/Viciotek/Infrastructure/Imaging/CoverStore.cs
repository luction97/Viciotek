using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;
using Viciotek.Domain;
using Viciotek.Infrastructure.Storage;

namespace Viciotek.Infrastructure.Imaging;

public sealed record CoverUpdate(string CoverFileName, string ThumbnailPath);

public sealed class CoverImageException : Exception
{
    public CoverImageException(string message) : base(message)
    {
    }
}

/// <summary>
/// Copies user covers into app data and produces an exact 2:3 thumbnail.
/// The thumbnail is always center-cropped, so the gallery never shows gaps
/// regardless of the source image's aspect ratio.
/// </summary>
public sealed class CoverStore
{
    private const int ThumbnailWidth = 480;
    private const int ThumbnailHeight = 720;
    private const long MaxSourceBytes = 30L * 1024 * 1024;
    private const long MaxSourcePixels = 30_000_000;

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _processingGate = new(1, 1);

    public CoverStore(AppPaths paths)
    {
        _paths = paths;
    }

    public string ThumbnailPathFor(Game game) => string.IsNullOrWhiteSpace(game.CoverFileName)
        ? Path.Combine(_paths.ThumbsDirectory, game.Id.ToString("N") + ".jpg")
        : ThumbnailPathForCover(game.CoverFileName);

    private string ThumbnailPathForCover(string coverFileName) =>
        Path.Combine(_paths.ThumbsDirectory, Path.GetFileNameWithoutExtension(Path.GetFileName(coverFileName)) + ".jpg");

    public string? CoverPathFor(Game game)
    {
        if (string.IsNullOrWhiteSpace(game.CoverFileName))
        {
            return null;
        }

        return Path.Combine(_paths.CoversDirectory, Path.GetFileName(game.CoverFileName));
    }

    public string ThumbnailPathFor(Game game, bool onlyIfCoverExists) =>
        onlyIfCoverExists && string.IsNullOrWhiteSpace(game.CoverFileName)
            ? string.Empty
            : ThumbnailPathFor(game);

    public async Task<CoverUpdate> SetFromFileAsync(
        Guid gameId,
        string sourcePath,
        string? previousCoverFileName,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(sourcePath);
        if (!fileInfo.Exists)
        {
            throw new CoverImageException("La imagen ya no existe.");
        }

        if (fileInfo.Length > MaxSourceBytes)
        {
            throw new CoverImageException("La imagen es demasiado grande (máximo 30 MB).");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CoverImageException("No se pudo leer la imagen.");
        }

        return await SetFromBytesAsync(gameId, bytes, previousCoverFileName, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CoverUpdate> SetFromBytesAsync(
        Guid gameId,
        byte[] bytes,
        string? previousCoverFileName,
        CancellationToken cancellationToken = default)
    {
        if (bytes.Length == 0)
        {
            throw new CoverImageException("La imagen está vacía.");
        }

        if (bytes.Length > MaxSourceBytes)
        {
            throw new CoverImageException("La imagen es demasiado grande (máximo 30 MB).");
        }

        _paths.EnsureCreated();

        ProcessedImage processed;
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            processed = await Task.Run(
                () => ProcessImage(bytes, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _processingGate.Release();
        }

        var coverFileName = $"{gameId:N}-{Guid.NewGuid():N}{processed.Extension}";
        var coverPath = Path.Combine(_paths.CoversDirectory, coverFileName);
        var thumbPath = ThumbnailPathForCover(coverFileName);
        var thumbTempPath = thumbPath + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(coverPath, bytes, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(thumbTempPath, processed.ThumbnailBytes, cancellationToken)
                .ConfigureAwait(false);
            File.Move(thumbTempPath, thumbPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(coverPath);
            TryDelete(thumbTempPath);
            throw new CoverImageException("No se pudo guardar la portada.");
        }
        catch (OperationCanceledException)
        {
            TryDelete(coverPath);
            TryDelete(thumbTempPath);
            throw;
        }

        // Preparation is non-destructive. The caller must commit library.json
        // before old files can be collected; a failed save can discard this pair.
        return new CoverUpdate(coverFileName, thumbPath);
    }

    public Task<string> EnsureThumbnailAsync(string coverFileName, CancellationToken cancellationToken = default) =>
        EnsureThumbnailCoreAsync(Path.GetFileName(coverFileName), cancellationToken);

    private async Task<string> EnsureThumbnailCoreAsync(string coverFileName, CancellationToken cancellationToken)
    {
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var thumbnail = ThumbnailPathForCover(coverFileName);
                if (File.Exists(thumbnail))
                {
                    return thumbnail;
                }

                var original = Path.Combine(_paths.CoversDirectory, coverFileName);
                var info = new FileInfo(original);
                if (!info.Exists || info.Length > MaxSourceBytes)
                {
                    throw new CoverImageException("La portada original no está disponible.");
                }

                var processed = ProcessImage(File.ReadAllBytes(original), cancellationToken);
                Directory.CreateDirectory(_paths.ThumbsDirectory);
                var temporary = thumbnail + ".tmp";
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.WriteAllBytes(temporary, processed.ThumbnailBytes);
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporary, thumbnail, overwrite: true);
                }
                finally
                {
                    TryDelete(temporary);
                }
                return thumbnail;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _processingGate.Release();
        }
    }

    public void Discard(CoverUpdate update)
    {
        TryDelete(Path.Combine(_paths.CoversDirectory, Path.GetFileName(update.CoverFileName)));
        TryDelete(update.ThumbnailPath);
    }

    public void PruneUnreferenced(IReadOnlyList<Game> retainedGames)
    {
        // Retain references from BOTH primary JSON and .bak. Never delete
        // arbitrary files a user may have placed in the data directory.
        var originals = retainedGames.Where(game => !string.IsNullOrWhiteSpace(game.CoverFileName))
            .Select(game => Path.GetFileName(game.CoverFileName!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var thumbnails = originals.Select(name => Path.GetFileName(ThumbnailPathForCover(name)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var game in retainedGames.Where(game => !string.IsNullOrWhiteSpace(game.CoverFileName)))
        {
            thumbnails.Add(game.Id.ToString("N") + ".jpg"); // Legacy cache still usable by .bak/older data.
        }

        foreach (var file in Directory.EnumerateFiles(_paths.CoversDirectory))
        {
            var name = Path.GetFileName(file);
            if (IsGeneratedName(name) && !originals.Contains(name))
            {
                TryDelete(file);
            }
        }
        foreach (var file in Directory.EnumerateFiles(_paths.ThumbsDirectory))
        {
            var name = Path.GetFileName(file);
            if ((IsGeneratedName(name) || Regex.IsMatch(name, @"\A[0-9a-fA-F]{32}\.jpg\z")) && !thumbnails.Contains(name))
            {
                TryDelete(file);
            }
        }
    }

    private static bool IsGeneratedName(string name) =>
        Regex.IsMatch(name, @"\A[0-9a-fA-F]{32}-[0-9a-fA-F]{32}\.[a-zA-Z0-9]{1,5}\z");

    public void DeleteFiles(Game game)
    {
        var coverPath = CoverPathFor(game);
        if (coverPath is not null)
        {
            TryDelete(coverPath);
        }

        TryDelete(ThumbnailPathFor(game));
    }

    private static ProcessedImage ProcessImage(byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BitmapDecoder decoder;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or IOException or FileFormatException)
        {
            throw new CoverImageException("El archivo no es una imagen compatible.");
        }

        if (decoder.Frames.Count == 0)
        {
            throw new CoverImageException("La imagen no contiene ningún fotograma.");
        }

        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
        {
            throw new CoverImageException("La imagen no tiene dimensiones válidas.");
        }

        if ((long)frame.PixelWidth * frame.PixelHeight > MaxSourcePixels)
        {
            throw new CoverImageException("La imagen tiene demasiada resolución (máximo 30 megapíxeles).");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var crop = CalculateCenterCrop(frame.PixelWidth, frame.PixelHeight);
        BitmapSource thumbnailSource = frame;
        if (crop.Width != frame.PixelWidth || crop.Height != frame.PixelHeight)
        {
            thumbnailSource = new CroppedBitmap(frame, crop);
        }

        var scale = new ScaleTransform(
            ThumbnailWidth / (double)thumbnailSource.PixelWidth,
            ThumbnailHeight / (double)thumbnailSource.PixelHeight);
        var scaled = new TransformedBitmap(thumbnailSource, scale);
        scaled.Freeze();

        var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var output = new MemoryStream();
        encoder.Save(output);

        return new ProcessedImage(output.ToArray(), ChooseExtension(decoder));
    }

    private static Int32Rect CalculateCenterCrop(int width, int height)
    {
        const double targetRatio = ThumbnailWidth / (double)ThumbnailHeight;
        var sourceRatio = width / (double)height;

        if (sourceRatio > targetRatio)
        {
            var cropWidth = Math.Max(1, (int)Math.Round(height * targetRatio));
            return new Int32Rect(Math.Max(0, (width - cropWidth) / 2), 0, Math.Min(width, cropWidth), height);
        }

        var cropHeight = Math.Max(1, (int)Math.Round(width / targetRatio));
        return new Int32Rect(0, Math.Max(0, (height - cropHeight) / 2), width, Math.Min(height, cropHeight));
    }

    private static string ChooseExtension(BitmapDecoder decoder)
    {
        var extensions = decoder.CodecInfo?.FileExtensions?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (extensions is not null)
        {
            foreach (var ext in extensions)
            {
                if (IsSafeExtension(ext))
                {
                    return ext.ToLowerInvariant();
                }
            }
        }

        return ".png";
    }

    private static bool IsSafeExtension(string extension) =>
        extension.Length is >= 2 and <= 6 &&
        extension[0] == '.' &&
        extension.Skip(1).All(char.IsLetterOrDigit);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ProcessedImage(byte[] ThumbnailBytes, string Extension);
}
