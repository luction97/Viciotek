using Viciotek.Domain;
using Viciotek.Infrastructure.Storage;
using System.Text.Json;

namespace Viciotek.Infrastructure.Library;

public sealed class AppSettings
{
    public string? SteamGridDbApiKey { get; set; }
    public string? ProtectedApiKey { get; set; }
    public bool AutoFetchCovers { get; set; } = true;
    public bool AnimationsEnabled { get; set; } = true;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

public sealed class SettingsStore
{
    private readonly AtomicJsonFile<AppSettings> _file;
    private readonly string _backupPath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SettingsStore(AppPaths paths)
    {
        _file = new AtomicJsonFile<AppSettings>(paths.SettingsFile);
        _backupPath = paths.SettingsFile + ".bak";
    }

    public (AppSettings Settings, string? Warning) Load()
    {
        var result = _file.Read();
        var settings = result.Value ?? new AppSettings();
        var legacyPlaintext = !string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey);
        settings.WindowWidth = Math.Clamp(settings.WindowWidth, 720, 10000);
        settings.WindowHeight = Math.Clamp(settings.WindowHeight, 480, 10000);
        settings.SteamGridDbApiKey = string.IsNullOrWhiteSpace(settings.ProtectedApiKey)
            ? (string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey) ? null : settings.SteamGridDbApiKey.Trim())
            : ProtectedSecret.Unprotect(settings.ProtectedApiKey);
        if (!string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
        {
            settings.SteamGridDbApiKey = settings.SteamGridDbApiKey.Trim();
        }

        var warning = result.Warning;
        try
        {
            if (legacyPlaintext && settings.SteamGridDbApiKey is not null) SaveNow(settings);
            else ProtectLegacyBackup();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            warning = string.Join(" ", new[] { warning, "No se pudo proteger una configuración antigua. Revisá los permisos de la carpeta de datos." }
                .Where(message => !string.IsNullOrWhiteSpace(message)));
        }
        return (settings, warning);
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var prepared = PrepareForStorage(settings);
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _file.WriteAsync(prepared).ConfigureAwait(false);
            await Task.Run(ProtectLegacyBackup).ConfigureAwait(false);
        }
        finally { _saveGate.Release(); }
    }

    public void SaveNow(AppSettings settings)
    {
        var prepared = PrepareForStorage(settings);
        _saveGate.Wait();
        try { _file.WriteNow(prepared); ProtectLegacyBackup(); }
        finally { _saveGate.Release(); }
    }

    private void ProtectLegacyBackup()
    {
        if (!File.Exists(_backupPath)) return;
        var backup = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_backupPath), AppJson.Options);
        if (backup is null || string.IsNullOrWhiteSpace(backup.SteamGridDbApiKey)) return;
        var protectedBackup = PrepareForStorage(backup);
        if (ProtectedSecret.Unprotect(protectedBackup.ProtectedApiKey) != backup.SteamGridDbApiKey.Trim())
            throw new InvalidOperationException("No se pudo verificar la protección de la configuración antigua.");
        var temporary = _backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, protectedBackup, AppJson.Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _backupPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static AppSettings PrepareForStorage(AppSettings settings)
    {
        var clone = settings.Clone();
        var trimmed = string.IsNullOrWhiteSpace(clone.SteamGridDbApiKey)
            ? null
            : clone.SteamGridDbApiKey.Trim();
        clone.SteamGridDbApiKey = null;
        clone.ProtectedApiKey = ProtectedSecret.Protect(trimmed);
        if (trimmed is not null && ProtectedSecret.Unprotect(clone.ProtectedApiKey) != trimmed)
            throw new InvalidOperationException("No se pudo verificar la protección de la clave.");
        return clone;
    }
}

public sealed class LibraryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<Game> Games { get; set; } = [];
}

public sealed class LibraryStore
{
    private readonly AtomicJsonFile<LibraryDocument> _file;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Func<LibraryDocument, CancellationToken, Task> _writeAsync;

    public LibraryStore(AppPaths paths)
    {
        _file = new AtomicJsonFile<LibraryDocument>(paths.LibraryFile);
        _writeAsync = (document, token) => _file.WriteAsync(document, token);
    }

    internal LibraryStore(AppPaths paths, Func<LibraryDocument, CancellationToken, Task> writeAsync) : this(paths) =>
        _writeAsync = writeAsync;

    public (List<Game> Games, string? Warning) Load()
    {
        var result = _file.Read();
        var games = result.Value?.Games ?? [];
        var sanitized = Sanitize(games);
        return (sanitized, result.Warning);
    }

    public async Task SaveAsync(IReadOnlyList<Game> games, CancellationToken cancellationToken = default)
    {
        var snapshot = new LibraryDocument
        {
            Games = games.Select(game => game.Clone()).ToList()
        };

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    internal IReadOnlyList<Game>? GetRetainedGames()
    {
        var games = new List<Game>();
        foreach (var path in new[] { _file.FilePath, _file.FilePath + ".bak" })
        {
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                var document = JsonSerializer.Deserialize<LibraryDocument>(File.ReadAllText(path), AppJson.Options);
                if (document?.Games is null)
                {
                    return null;
                }
                games.AddRange(document.Games.Where(game => game is not null));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                return null; // Uncertain references: skip cleanup instead of losing recoverable covers.
            }
        }
        return games;
    }

    public void SaveNow(IReadOnlyList<Game> games)
    {
        var snapshot = new LibraryDocument
        {
            Games = games.Select(game => game.Clone()).ToList()
        };

        _saveGate.Wait();
        try
        {
            _file.WriteNow(snapshot);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static List<Game> Sanitize(List<Game> games)
    {
        var valid = games
            .Where(game => game is not null)
            .ToList();

        var seenIds = new HashSet<Guid>();
        foreach (var game in valid)
        {
            if (game.Id == Guid.Empty || !seenIds.Add(game.Id))
            {
                game.Id = Guid.NewGuid();
                seenIds.Add(game.Id);
            }

            game.Name = string.IsNullOrWhiteSpace(game.Name)
                ? GameName.FromPath(game.LaunchPath)
                : game.Name.Trim();
            game.LaunchPath = game.LaunchPath?.Trim() ?? string.Empty;
            game.Arguments = string.IsNullOrWhiteSpace(game.Arguments) ? null : game.Arguments;
            game.WorkingDirectory = string.IsNullOrWhiteSpace(game.WorkingDirectory) ? null : game.WorkingDirectory;
            game.ShortcutPath = string.IsNullOrWhiteSpace(game.ShortcutPath) ? null : game.ShortcutPath;
            game.IconPath = string.IsNullOrWhiteSpace(game.IconPath) ? null : game.IconPath;
            game.IdentityCandidates = (game.IdentityCandidates ?? []).Where(GameTitle.IsDescriptive).Take(8).ToList();
            if (game.SteamAppId is <= 0) game.SteamAppId = null;
            if (game.SteamGridDbGameId is <= 0) game.SteamGridDbGameId = null;
            game.CoverFileName = string.IsNullOrWhiteSpace(game.CoverFileName)
                ? null
                : Path.GetFileName(game.CoverFileName);
        }

        var ordered = valid
            .OrderBy(game => game.SortOrder)
            .ThenBy(game => game.AddedAt)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].SortOrder = index;
        }

        return ordered;
    }
}
