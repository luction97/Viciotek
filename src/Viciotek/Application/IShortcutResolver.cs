namespace Viciotek.Application;

public sealed record ShortcutInfo(
    string? TargetPath,
    string? Arguments,
    string? WorkingDirectory);

public interface IShortcutResolver
{
    ShortcutInfo Resolve(string shortcutPath);
}
