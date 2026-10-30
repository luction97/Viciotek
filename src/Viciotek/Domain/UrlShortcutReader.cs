namespace Viciotek.Domain;

public sealed record UrlShortcutInfo(string Url, string? IconFile, int IconIndex);

public static class UrlShortcutReader
{
    private const int MaxFileSize = 64 * 1024;

    public static bool TryRead(string path, out UrlShortcutInfo? info, out string? error)
    {
        info = null;
        error = null;

        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                error = L.Get("TheFileDoesNotExist");
                return false;
            }

            if (fileInfo.Length > MaxFileSize)
            {
                error = L.Get("TheUrlFileIsTooLarge");
                return false;
            }

            string? url = null;
            string? iconFile = null;
            var iconIndex = 0;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"');

                if (key.Equals("URL", StringComparison.OrdinalIgnoreCase))
                {
                    url = value;
                }
                else if (key.Equals("IconFile", StringComparison.OrdinalIgnoreCase))
                {
                    iconFile = value;
                }
                else if (key.Equals("IconIndex", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(value, out iconIndex);
                }
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                error = L.Get("TheUrlFileDoesNotContainADestination");
                return false;
            }

            if (!ProtocolPolicy.TryValidate(url, out _, out var protocolError))
            {
                error = protocolError;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(iconFile))
            {
                iconFile = Environment.ExpandEnvironmentVariables(iconFile);
                if (!Path.IsPathRooted(iconFile))
                {
                    iconFile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, iconFile));
                }

                if (!File.Exists(iconFile))
                {
                    iconFile = null;
                }
            }

            info = new UrlShortcutInfo(url.Trim(), iconFile, iconIndex);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            error = L.Get("CouldNotReadTheUrlFile");
            return false;
        }
    }
}
