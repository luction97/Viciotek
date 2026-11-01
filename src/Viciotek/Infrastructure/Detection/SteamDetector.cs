using System.Text.RegularExpressions;
using Viciotek.Application;
using Viciotek.Domain;

namespace Viciotek.Infrastructure.Detection;

public sealed class SteamDetector : IInstalledGameDetector
{
    public string Platform => "Steam";

    private readonly IEnumerable<string>? _folders;
    private readonly string? _appInfo;
    public SteamDetector(IEnumerable<string>? folders = null, string? appInfo = null) { _folders = folders; _appInfo = appInfo; }
    public IReadOnlyList<DetectedGame> Detect()
    {
        var candidates = new Dictionary<int, (string Name, string Directory)>();
        var folders = (_folders ?? FindSteamappsFolders()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var steamapps in folders)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 256 * 1024) continue;
                        var text = File.ReadAllText(file); var directory = Value(text, "installdir"); var name = Value(text, "name");
                        if (!int.TryParse(Value(text, "appid"), out var id) || id <= 0 || Path.GetFileNameWithoutExtension(file) != $"appmanifest_{id}" ||
                            !uint.TryParse(Value(text, "StateFlags"), out var flags) || (flags & 4) == 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(directory) || directory.IndexOfAny(['/', '\\']) >= 0 || directory is "." or "..") continue;
                        var install = Path.Combine(steamapps, "common", directory); if (!Directory.Exists(install)) continue;
                        candidates.TryAdd(id, (name, install));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var ids = candidates.Keys.ToHashSet(); var confirmed = new HashSet<int>();
        var caches = _appInfo is not null ? new[] { _appInfo } : folders.Select(folder => Path.Combine(Path.GetDirectoryName(folder)!, "appcache", "appinfo.vdf"));
        foreach (var cache in caches.Distinct(StringComparer.OrdinalIgnoreCase)) confirmed.UnionWith(SteamAppTypes.ReadGames(cache, ids));
        return candidates.Where(pair => confirmed.Contains(pair.Key)).Select(pair => new DetectedGame(pair.Value.Name, Platform,
            $"steam://rungameid/{pair.Key}", LaunchKind.Uri, WorkingDirectory: pair.Value.Directory, PlatformId: pair.Key.ToString(), HighConfidence: true)).ToList();
    }
    private static IEnumerable<string> FindSteamappsFolders()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        // Registry: SteamPath
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                candidates.Add(Path.Combine(steamPath, "steamapps"));
                var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf)) ParseVdf(vdf, candidates);
            }
        }
        catch { }

        // Search all fixed drives
        foreach (var drive in DriveHelper.GetFixedDrives())
        {
            foreach (var root in new[] { "Program Files (x86)\\Steam", "Program Files\\Steam", "Steam", "SteamLibrary" })
            {
                var p = Path.Combine(drive, root);
                if (Directory.Exists(p))
                {
                    candidates.Add(Path.Combine(p, "steamapps"));
                    var vdf = Path.Combine(p, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf)) ParseVdf(vdf, candidates);
                }
            }
        }

        var result = new List<string>();
        foreach (var c in candidates)
        {
            try
            {
                if (Directory.Exists(c) && found.Add(c)) result.Add(c);
            }
            catch { }
        }
        return result;
    }

    private static void ParseVdf(string vdf, List<string> candidates)
    {
        try
        {
            foreach (var line in File.ReadAllLines(vdf))
            {
                var m = Regex.Match(line, "\"path\"\\s*\"([^\"]+)\"");
                if (m.Success)
                {
                    var p = m.Groups[1].Value.Replace("\\\\", "\\");
                    candidates.Add(Path.Combine(p, "steamapps"));
                }
            }
        }
        catch { }
    }

    private static string? Value(string text, string key)
    {
        var matches = Regex.Matches(text, "\"" + key + "\"\\s*\"([^\"\\r\\n]*)\"", RegexOptions.IgnoreCase);
        return matches.Count == 1 ? matches[0].Groups[1].Value : null;
    }
}
