using Viciotek.Domain;

namespace Viciotek.Application;

public sealed record LocalGameMetadata(
    int? SteamAppId = null, string? SteamName = null, string? ProductName = null,
    string? FileDescription = null, string? InstallationFolder = null, string? InstalledDisplayName = null);

public interface IGameMetadataReader
{
    LocalGameMetadata Read(Game game);
    void BeginImport() { }
}

/// <summary>Local resolution only. Called on the import worker, never during startup.</summary>
public sealed class GameIdentityResolver(IGameMetadataReader metadataReader)
{
    public void BeginImport() => metadataReader.BeginImport();
    public void IdentifyNewGame(Game game, string importPath)
    {
        var metadata = metadataReader.Read(game);
        var shortcutName = game.ShortcutPath is null ? null : GameName.FromPath(game.ShortcutPath);
        var executableName = game.LaunchKind == LaunchKind.Executable ? Path.GetFileName(game.LaunchPath) : null;
        if (shortcutName is not null && GameTitle.IsAbbreviation(shortcutName)) shortcutName = null;
        var product = GameTitle.IsAbbreviation(metadata.ProductName ?? string.Empty) ? null : metadata.ProductName;
        var description = GameTitle.IsAbbreviation(metadata.FileDescription ?? string.Empty) ? null : metadata.FileDescription;
        var sources = new[] { metadata.SteamName, shortcutName, product,
            description, metadata.InstallationFolder, metadata.InstalledDisplayName,
            executableName is null ? game.Name : Path.GetFileNameWithoutExtension(executableName) };
        var candidates = sources.Where(GameTitle.IsDescriptive).Select(value => GameTitle.Clean(value!))
            .Where(GameTitle.IsDescriptive).DistinctBy(GameTitle.Normalize).Take(8).ToList();
        game.ExecutableName = executableName;
        game.ImportSource = Path.GetExtension(importPath).TrimStart('.').ToLowerInvariant();
        game.SteamAppId = metadata.SteamAppId;
        game.IdentityCandidates = candidates;
        game.CoverSearchName = candidates.FirstOrDefault() ?? game.Name;
        game.Name = game.CoverSearchName;
    }
}
