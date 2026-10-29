using Viciotek.Domain;

namespace Viciotek.Application;

public sealed record ImportProblem(string Path, string Message);

public sealed class ImportOutcome
{
    public List<Game> Added { get; } = [];
    public List<ImportProblem> Problems { get; } = [];

    public int DuplicateCount { get; set; }
}

public sealed class GameImporter
{
    private readonly IShortcutResolver _shortcutResolver;
    private readonly GameIdentityResolver? _identityResolver;

    public GameImporter(IShortcutResolver shortcutResolver, GameIdentityResolver? identityResolver = null)
    {
        _shortcutResolver = shortcutResolver;
        _identityResolver = identityResolver;
    }

    public ImportOutcome Import(IEnumerable<string> paths, IReadOnlyCollection<Game> existingGames)
    {
        var outcome = new ImportOutcome();
        _identityResolver?.BeginImport();
        var keys = new HashSet<string>(existingGames.Select(GameIdentity.CreateDuplicateKey), StringComparer.Ordinal);
        var nextOrder = existingGames.Count == 0 ? 0 : existingGames.Max(game => game.SortOrder) + 1;

        foreach (var rawPath in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = rawPath.Trim().Trim('"');
            try
            {
                if (Directory.Exists(path))
                {
                    outcome.Problems.Add(new ImportProblem(path, "Las carpetas no se agregan. Arrastrá un ejecutable o un acceso directo."));
                    continue;
                }

                if (!File.Exists(path))
                {
                    outcome.Problems.Add(new ImportProblem(path, "El archivo no existe."));
                    continue;
                }

                var extension = Path.GetExtension(path);
                if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    outcome.Problems.Add(new ImportProblem(path, "Formato no compatible. Usá .lnk, .exe o .url."));
                    continue;
                }

                var creation = extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                    ? CreateFromUrl(path)
                    : extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        ? CreateFromShortcut(path)
                        : CreateFromExecutable(path);

                if (creation.Game is null)
                {
                    outcome.Problems.Add(new ImportProblem(path, creation.Error ?? "No se pudo interpretar el destino."));
                    continue;
                }

                var game = creation.Game;
                var duplicateKey = GameIdentity.CreateDuplicateKey(game);
                if (!keys.Add(duplicateKey))
                {
                    outcome.DuplicateCount++;
                    outcome.Problems.Add(new ImportProblem(path, "Ya está en la biblioteca."));
                    continue;
                }

                game.SortOrder = nextOrder++;
                _identityResolver?.IdentifyNewGame(game, path);
                outcome.Added.Add(game);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                outcome.Problems.Add(new ImportProblem(path, "No se pudo leer el archivo: " + exception.Message));
            }
        }

        return outcome;
    }

    public Game ResolveIdentityForCoverSearch(Game game)
    {
        var copy = game.Clone();
        if (_identityResolver is null || copy.IsNameCustomized || copy.SteamGridDbGameId is > 0 || copy.IdentityCandidates.Count > 0) return copy;
        var importPath = copy.ShortcutPath ?? copy.LaunchPath;
        // Legacy libraries have no customization flag. Only enrich names that still match their import filename.
        if (GameTitle.Normalize(copy.Name) != GameTitle.Normalize(GameName.FromPath(importPath))) return copy;
        _identityResolver.BeginImport();
        _identityResolver.IdentifyNewGame(copy, importPath);
        copy.Name = game.Name;
        return copy;
    }

    private CreationResult CreateFromShortcut(string shortcutPath)
    {
        ShortcutInfo info;
        try
        {
            info = _shortcutResolver.Resolve(shortcutPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return new CreationResult(null, "Windows no pudo resolver este acceso directo.");
        }

        var target = ExpandTarget(info.TargetPath, shortcutPath);
        if (string.IsNullOrWhiteSpace(target))
        {
            return new CreationResult(null,
                "El acceso directo no apunta a un ejecutable que VICIOTEK pueda iniciar.");
        }

        if (!LaunchTargetRules.IsAllowedProgram(target))
        {
            return new CreationResult(null,
                "Por seguridad, el acceso directo debe apuntar a un archivo .exe o .com.");
        }

        var workingDirectory = ExpandWorkingDirectory(info.WorkingDirectory, shortcutPath, target);

        return new CreationResult(new Game
        {
            Id = Guid.NewGuid(),
            Name = GameName.FromPath(shortcutPath),
            LaunchPath = target,
            LaunchKind = LaunchKind.Executable,
            Arguments = string.IsNullOrWhiteSpace(info.Arguments) ? null : info.Arguments,
            WorkingDirectory = workingDirectory,
            ShortcutPath = Path.GetFullPath(shortcutPath),
            IconPath = Path.GetFullPath(shortcutPath),
            AddedAt = DateTimeOffset.UtcNow
        }, null);
    }

    private static CreationResult CreateFromUrl(string urlPath)
    {
        if (!UrlShortcutReader.TryRead(urlPath, out var info, out var readError) || info is null)
        {
            return new CreationResult(null, readError ?? "No se pudo leer el archivo .url.");
        }

        return new CreationResult(new Game
        {
            Id = Guid.NewGuid(),
            Name = GameName.FromPath(urlPath),
            LaunchPath = info.Url,
            LaunchKind = LaunchKind.Uri,
            ShortcutPath = Path.GetFullPath(urlPath),
            IconPath = Path.GetFullPath(urlPath),
            AddedAt = DateTimeOffset.UtcNow
        }, null);
    }

    private static CreationResult CreateFromExecutable(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        return new CreationResult(new Game
        {
            Id = Guid.NewGuid(),
            Name = GameName.FromPath(executablePath),
            LaunchPath = fullPath,
            LaunchKind = LaunchKind.Executable,
            Arguments = null,
            WorkingDirectory = Path.GetDirectoryName(fullPath),
            IconPath = fullPath,
            AddedAt = DateTimeOffset.UtcNow
        }, null);
    }

    private static string? ExpandTarget(string? target, string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.Combine(Path.GetDirectoryName(shortcutPath) ?? string.Empty, expanded);
        }

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? ExpandWorkingDirectory(string? directory, string shortcutPath, string target)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var expanded = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.Combine(Path.GetDirectoryName(shortcutPath) ?? string.Empty, expanded);
            }

            if (Directory.Exists(expanded))
            {
                return expanded;
            }
        }

        return Path.GetDirectoryName(target);
    }

    private sealed record CreationResult(Game? Game, string? Error);
}
