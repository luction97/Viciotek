namespace Viciotek.Domain;

/// <summary>
/// A single entry of the personal library. It only stores metadata and paths;
/// removing a game never touches the game installation.
/// </summary>
public sealed class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string LaunchPath { get; set; } = string.Empty;
    public LaunchKind LaunchKind { get; set; } = LaunchKind.Executable;
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? ShortcutPath { get; set; }
    public string? IconPath { get; set; }
    public string? CoverFileName { get; set; }
    public string? ExecutableName { get; set; }
    public string? ImportSource { get; set; }
    public int? SteamAppId { get; set; }
    public int? SteamGridDbGameId { get; set; }
    public string? SteamGridDbName { get; set; }
    public string? CoverSearchName { get; set; }
    public List<string> IdentityCandidates { get; set; } = [];
    public bool IsNameCustomized { get; set; }
    public long SortOrder { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    public Game Clone()
    {
        var clone = (Game)MemberwiseClone();
        clone.IdentityCandidates = [.. IdentityCandidates];
        return clone;
    }
}
