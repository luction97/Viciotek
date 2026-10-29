namespace Viciotek.Domain;

public static class GameName
{
    private static readonly string[] ShortcutSuffixes =
    [
        " - acceso directo",
        " - shortcut",
        " (shortcut)",
        " - link"
    ];

    public static string FromPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Juego sin título";
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in ShortcutSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^suffix.Length].TrimEnd();
                    changed = true;
                }
            }
        }

        return string.IsNullOrWhiteSpace(name) ? "Juego sin título" : name;
    }
}
