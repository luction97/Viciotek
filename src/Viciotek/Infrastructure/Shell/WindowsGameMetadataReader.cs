using System.Diagnostics;
using System.Text.RegularExpressions;
using Viciotek.Application;
using Viciotek.Domain;
using Microsoft.Win32;

namespace Viciotek.Infrastructure.Shell;

/// <summary>Bounded local reads; never starts a game, walks a drive, or uses Win32_Product.</summary>
public sealed class WindowsGameMetadataReader : IGameMetadataReader
{
    private readonly Dictionary<string, List<SteamInstall>> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private List<(string Path, string Name)>? _installed;
    private readonly object _gate = new();

    public void BeginImport()
    {
        lock (_gate)
        {
            _manifests.Clear();
            _installed = null;
        }
    }

    public LocalGameMetadata Read(Game game)
    {
        lock (_gate) return ReadCore(game);
    }

    private LocalGameMetadata ReadCore(Game game)
    {
        if (game.LaunchKind == LaunchKind.Uri)
            return new(SteamAppId: SteamIdFromUri(game.LaunchPath));

        if (Path.GetFileName(game.LaunchPath).Equals("steam.exe", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(game.Arguments ?? string.Empty, @"(?:^|\s)-applaunch\s+(\d+)(?:\s|$)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var id) && id > 0) return new(SteamAppId: id);
        }

        var ancestors = Ancestors(game.LaunchPath).ToList();
        var install = ReadSteamInstall(ancestors);
        string? product = null, description = null;
        try
        {
            var version = FileVersionInfo.GetVersionInfo(game.LaunchPath);
            product = version.ProductName;
            description = version.FileDescription;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { }

        var folder = ancestors.FirstOrDefault(path => !IsTechnicalFolder(Path.GetFileName(path)));
        var displayName = install is null ? ReadInstalledName(game.LaunchPath) : null;
        return new(install?.AppId, install?.Name, product, description, folder is null ? null : Path.GetFileName(folder), displayName);
    }

    public static int? SteamIdFromUri(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != "steam" ||
            (uri.Host != "run" && uri.Host != "rungameid")) return null;
        return int.TryParse(uri.AbsolutePath.Trim('/').Split('/')[0], out var id) && id > 0 ? id : null;
    }

    private static IEnumerable<string> Ancestors(string executable)
    {
        var directory = Path.GetDirectoryName(executable);
        for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(directory); depth++)
        {
            yield return directory;
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool IsTechnicalFolder(string name) => Regex.IsMatch(name,
        @"^(?:binaries|bin|win64|win32|x64|x86|engine|game|shipping|release|retail|pc|windows|windowsnoeditor)$", RegexOptions.IgnoreCase);

    private SteamInstall? ReadSteamInstall(IReadOnlyList<string> ancestors)
    {
        var steamapps = ancestors.FirstOrDefault(path => Path.GetFileName(path).Equals("steamapps", StringComparison.OrdinalIgnoreCase));
        if (steamapps is null) return null;
        if (!_manifests.TryGetValue(steamapps, out var installs))
        {
            installs = [];
            try
            {
                foreach (var file in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf").Take(2048))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 256 * 1024) continue;
                        var text = File.ReadAllText(file);
                        var idText = Value(text, "appid");
                        var name = Value(text, "name");
                        var directory = Value(text, "installdir");
                        if (!int.TryParse(idText, out var id) || id <= 0 ||
                            Path.GetFileNameWithoutExtension(file) != $"appmanifest_{id}" ||
                            !GameTitle.IsDescriptive(name) || string.IsNullOrWhiteSpace(directory) ||
                            directory.IndexOfAny(['/', '\\']) >= 0 || directory is "." or "..") continue;
                        installs.Add(new(id, name!, Path.Combine(steamapps, "common", directory)));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            // Only cached for this import batch, so newly installed games are visible next time.
            _manifests[steamapps] = installs;
        }
        var matches = installs.Where(install => ancestors.Contains(install.Directory, StringComparer.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? Value(string text, string key)
    {
        var matches = Regex.Matches(text, "\"" + key + "\"\\s*\"([^\"\\r\\n]*)\"", RegexOptions.IgnoreCase);
        return matches.Count == 1 ? matches[0].Groups[1].Value : null;
    }

    private string? ReadInstalledName(string executable)
    {
        _installed ??= ReadInstalledPrograms();
        var matches = _installed.Where(item => IsInside(executable, item.Path)).OrderByDescending(item => item.Path.Length).ToList();
        if (matches.Count == 0) return null;
        var best = matches.Where(item => item.Path.Length == matches[0].Path.Length).Select(item => item.Name).Distinct().ToList();
        return best.Count == 1 ? best[0] : null;
    }

    internal static bool IsInside(string executable, string directory) =>
        executable.StartsWith(directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static List<(string Path, string Name)> ReadInstalledPrograms()
    {
        var result = new List<(string, string)>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var key in uninstall.GetSubKeyNames().Take(4096))
                {
                    try
                    {
                        using var app = uninstall.OpenSubKey(key);
                        var location = app?.GetValue("InstallLocation") as string;
                        var name = app?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location) || !GameTitle.IsDescriptive(name)) continue;
                        var fullPath = Path.GetFullPath(location.Trim().Trim('"')).TrimEnd('\\');
                        if (fullPath.Length <= 3 || !GameTitle.IsDescriptive(Path.GetFileName(fullPath))) continue;
                        result.Add((fullPath, name!));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return result;
    }

    private sealed record SteamInstall(int AppId, string Name, string Directory);
}
