using System.Text.RegularExpressions;
using Viciotek.Application;
using Viciotek.Domain;

namespace Viciotek.Infrastructure.Detection;

public sealed class SteamDetector : IInstalledGameDetector
{
    public string Platform => "Steam";

    public IReadOnlyList<DetectedGame> Detect()
    {
        var results = new List<DetectedGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var steamapps in FindSteamappsFolders())
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 256 * 1024) continue;
                        var text = File.ReadAllText(file);
                        var idText = Value(text, "appid");
                        var name = Value(text, "name");
                        var directory = Value(text, "installdir");
                        if (!int.TryParse(idText, out var id) || id <= 0) continue;
                        if (Path.GetFileNameWithoutExtension(file) != $"appmanifest_{id}") continue;
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(directory)) continue;
                        if (directory.IndexOfAny(['/', '\\']) >= 0 || directory is "." or "..") continue;
                        // Exclude tools/runtimes/common Steam components
                        if (IsNonGame(id, name)) continue;

                        var installDir = Path.Combine(steamapps, "common", directory);
                        if (!Directory.Exists(installDir)) continue;

                        var exe = FindLaunchExecutable(installDir);
                        if (exe is null) continue;

                        var key = exe.ToUpperInvariant();
                        if (!seen.Add(key)) continue;

                        results.Add(new DetectedGame(
                            Name: name!,
                            Platform: "Steam",
                            LaunchPath: exe,
                            LaunchKind: LaunchKind.Executable,
                            WorkingDirectory: Path.GetDirectoryName(exe),
                            IconPath: exe,
                            PlatformId: id.ToString()));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return results;
    }

    private static IEnumerable<string> FindSteamappsFolders()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Common Steam locations
        var candidates = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                candidates.Add(Path.Combine(steamPath, "steamapps"));
                // libraryfolders.vdf
                var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
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
            }
        }
        catch { }
        // Default locations
        candidates.Add(@"C:\Program Files (x86)\Steam\steamapps");
        candidates.Add(@"C:\Program Files\Steam\steamapps");

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

    private static string? FindLaunchExecutable(string installDir)
    {
        // Look for exe in root or bin subfolders, skip known non-game exes
        try
        {
            var exes = Directory.EnumerateFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly).ToList();
            if (exes.Count == 0)
            {
                foreach (var sub in new[] { "bin", "Binaries", "Win64", "x64" })
                {
                    var p = Path.Combine(installDir, sub);
                    if (Directory.Exists(p))
                        exes.AddRange(Directory.EnumerateFiles(p, "*.exe", SearchOption.TopDirectoryOnly));
                }
            }
            // Prefer exe matching folder name
            var folderName = Path.GetFileName(installDir);
            var match = exes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Equals(folderName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            // Otherwise first non-launcher exe
            return exes.FirstOrDefault(e => !IsLauncherExe(Path.GetFileName(e)));
        }
        catch { return null; }
    }

    private static bool IsNonGame(int appId, string name)
    {
        // Steam runtime/redistributable app ids and common tools
        var lower = name.ToLowerInvariant();
        if (lower.Contains("redistributable") || lower.Contains("runtime") || lower.Contains("sdk") ||
            lower.Contains("dedicated server") || lower.Contains("tool") || lower.Contains("editor")) return true;
        // Known Steam client apps
        if (appId is 228980 or 1007 or 250820) return true; // Steam CEF, Steamworks SDK, SteamVR? keep simple
        return false;
    }

    private static bool IsLauncherExe(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("launcher") || lower.Contains("updater") || lower.Contains("crash") ||
               lower.Contains("unins") || lower.Contains("redist");
    }

    private static string? Value(string text, string key)
    {
        var matches = Regex.Matches(text, "\"" + key + "\"\\s*\"([^\"\\r\\n]*)\"", RegexOptions.IgnoreCase);
        return matches.Count == 1 ? matches[0].Groups[1].Value : null;
    }
}
