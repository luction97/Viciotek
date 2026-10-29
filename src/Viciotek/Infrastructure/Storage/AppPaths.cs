using System.Text.Json;

namespace Viciotek.Infrastructure.Storage;

public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
        CoversDirectory = Path.Combine(root, "covers");
        ThumbsDirectory = Path.Combine(CoversDirectory, "thumbs");
        LibraryFile = Path.Combine(root, "library.json");
        SettingsFile = Path.Combine(root, "settings.json");
    }

    public string Root { get; }
    public string CoversDirectory { get; }
    public string ThumbsDirectory { get; }
    public string LibraryFile { get; }
    public string SettingsFile { get; }

    public static AppPaths CreateDefault()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("VICIOTEK_DATA_DIR");
        var root = !string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.GetFullPath(overrideRoot.Trim().Trim('"'))
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Viciotek");

        return new AppPaths(root);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CoversDirectory);
        Directory.CreateDirectory(ThumbsDirectory);
    }
}

public sealed record JsonReadResult<T>(T? Value, string? Warning);

public sealed class AtomicJsonFile<T> where T : class
{
    private readonly string _path;
    private readonly string _backupPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AtomicJsonFile(string path)
    {
        _path = path;
        _backupPath = path + ".bak";
    }

    public string FilePath => _path;

    public JsonReadResult<T> Read()
    {
        if (!File.Exists(_path))
        {
            if (!File.Exists(_backupPath))
            {
                return new JsonReadResult<T>(null, null);
            }

            return TryReadFile(_backupPath, "Se recuperó una copia de seguridad de los datos.");
        }

        try
        {
            var json = File.ReadAllText(_path);
            var value = JsonSerializer.Deserialize<T>(json, AppJson.Options);
            if (value is null)
            {
                throw new JsonException("El documento está vacío.");
            }

            return new JsonReadResult<T>(value, null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            PreserveCorruptFile();
            if (File.Exists(_backupPath))
            {
                return TryReadFile(_backupPath, "Los datos estaban dañados. Se recuperó la última copia segura.");
            }

            return new JsonReadResult<T>(null, "Los datos guardados estaban dañados y no se pudieron recuperar.");
        }
    }

    public async Task WriteAsync(T value, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, AppJson.Options);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => WriteJson(json), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void WriteNow(T value)
    {
        var json = JsonSerializer.Serialize(value, AppJson.Options);
        _writeLock.Wait();
        try
        {
            WriteJson(json);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void WriteJson(string json)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _path + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private JsonReadResult<T> TryReadFile(string path, string warning)
    {
        try
        {
            var json = File.ReadAllText(path);
            var value = JsonSerializer.Deserialize<T>(json, AppJson.Options);
            return value is null
                ? new JsonReadResult<T>(null, "No se pudieron recuperar los datos guardados.")
                : new JsonReadResult<T>(value, warning);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new JsonReadResult<T>(null, "No se pudieron recuperar los datos guardados.");
        }
    }

    private void PreserveCorruptFile()
    {
        try
        {
            var target = Path.Combine(
                Path.GetDirectoryName(_path) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(_path)}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(_path, target, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A backup is still attempted. A failure to rename must not block startup.
        }
    }

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
}
