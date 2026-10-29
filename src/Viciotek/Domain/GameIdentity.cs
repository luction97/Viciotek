namespace Viciotek.Domain;

public static class GameIdentity
{
    /// <summary>
    /// Duplicate detection is intentionally conservative: two entries that
    /// use the same executable with different arguments are legitimate.
    /// </summary>
    public static string CreateDuplicateKey(Game game)
    {
        var launchPath = game.LaunchPath?.Trim() ?? string.Empty;

        if (game.LaunchKind == LaunchKind.Uri)
        {
            var key = ProtocolPolicy.TryValidate(launchPath, out var uri, out _)
                ? uri.AbsoluteUri
                : launchPath;
            return "uri:" + key.ToUpperInvariant();
        }

        var normalizedPath = NormalizePath(launchPath);
        if (game.LaunchKind == LaunchKind.Shortcut)
        {
            return "shortcut:" + normalizedPath;
        }

        return "executable:" + normalizedPath + "|" + (game.Arguments?.Trim() ?? string.Empty);
    }

    public static string NormalizePath(string path)
    {
        var value = (path ?? string.Empty).Trim().Trim('"');
        if (value.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            value = Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Keep the original value for comparison.
        }

        return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
    }
}
