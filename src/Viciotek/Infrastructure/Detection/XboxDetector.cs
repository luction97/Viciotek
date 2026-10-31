using System.Xml.Linq;
using Viciotek.Application;
using Viciotek.Domain;

namespace Viciotek.Infrastructure.Detection;

public sealed class XboxDetector : IInstalledGameDetector
{
    public string Platform => "Xbox / Game Pass";

    public IReadOnlyList<DetectedGame> Detect()
    {
        var results = new List<DetectedGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Game Pass PC games install under C:\XboxGames\<Game>\Content
        var xboxGames = @"C:\XboxGames";
        if (Directory.Exists(xboxGames))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(xboxGames))
                {
                    var content = Path.Combine(dir, "Content");
                    var searchRoot = Directory.Exists(content) ? content : dir;
                    var exe = FindExe(searchRoot);
                    if (exe is null) continue;
                    var name = Path.GetFileName(dir);
                    if (IsNonGame(name)) continue;
                    var key = exe.ToUpperInvariant();
                    if (!seen.Add(key)) continue;
                    results.Add(new DetectedGame(name, Platform, exe, LaunchKind.Executable, null, Path.GetDirectoryName(exe), exe));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        // WindowsApps: games with a real executable in their folder
        var windowsApps = @"C:\Program Files\WindowsApps";
        if (Directory.Exists(windowsApps))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(windowsApps))
                {
                    var manifest = Path.Combine(dir, "AppxManifest.xml");
                    if (!File.Exists(manifest)) continue;
                    try
                    {
                        var doc = XDocument.Load(manifest);
                        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                        var identity = doc.Descendants(ns + "Identity").FirstOrDefault();
                        var displayName = identity?.Attribute("Name")?.Value;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;
                        // Only consider if there's an Application with Executable
                        var app = doc.Descendants(ns + "Application").FirstOrDefault(a => a.Attribute("Executable") is not null);
                        if (app is null) continue;
                        var exeName = app.Attribute("Executable")?.Value;
                        if (string.IsNullOrWhiteSpace(exeName)) continue;
                        var exePath = Path.Combine(dir, exeName!);
                        if (!File.Exists(exePath)) continue;
                        // Filter out non-games
                        if (IsNonGame(displayName!)) continue;
                        var key = exePath.ToUpperInvariant();
                        if (!seen.Add(key)) continue;
                        results.Add(new DetectedGame(displayName!, Platform, exePath, LaunchKind.Executable, null, Path.GetDirectoryName(exePath), exePath));
                    }
                    catch { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        return results;
    }

    private static string? FindExe(string root)
    {
        try
        {
            foreach (var exe in Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories).Take(200))
            {
                var name = Path.GetFileName(exe).ToLowerInvariant();
                if (name.Contains("launcher") || name.Contains("unins") || name.Contains("crash") ||
                    name.Contains("redist") || name.Contains("setup")) continue;
                return exe;
            }
        }
        catch { }
        return null;
    }

    private static bool IsNonGame(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("runtime") || lower.Contains("framework") || lower.Contains("vcredist") ||
               lower.Contains("directx") || lower.Contains("launcher") || lower.Contains("service");
    }
}
