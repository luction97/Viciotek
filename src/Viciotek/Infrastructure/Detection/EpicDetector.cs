using System.Text.Json;
using Viciotek.Application;
using Viciotek.Domain;

namespace Viciotek.Infrastructure.Detection;

public sealed class EpicDetector : IInstalledGameDetector
{
    public string Platform => "Epic Games";

    public IReadOnlyList<DetectedGame> Detect()
    {
        var results = new List<DetectedGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestsDir)) return results;

        foreach (var file in Directory.EnumerateFiles(manifestsDir, "*.item"))
        {
            try
            {
                var json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var displayName = root.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : null;
                var installLocation = root.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;
                var launchExe = root.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() : null;
                var appName = root.TryGetProperty("AppName", out var an) ? an.GetString() : null;
                var appVersion = root.TryGetProperty("AppVersion", out var av) ? av.GetString() : null;

                if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLocation)) continue;
                if (string.IsNullOrWhiteSpace(launchExe)) continue;

                // Skip non-games: tools, editors, etc.
                if (IsNonGame(displayName!)) continue;

                var fullPath = Path.IsPathRooted(launchExe!) ? launchExe! : Path.Combine(installLocation!, launchExe!);
                if (!File.Exists(fullPath)) continue;

                var key = fullPath.ToUpperInvariant();
                if (!seen.Add(key)) continue;

                results.Add(new DetectedGame(
                    Name: displayName!,
                    Platform: "Epic Games",
                    LaunchPath: fullPath,
                    LaunchKind: LaunchKind.Executable,
                    WorkingDirectory: installLocation,
                    IconPath: fullPath,
                    PlatformId: appName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        return results;
    }

    private static bool IsNonGame(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("launcher") || lower.Contains("updater") || lower.Contains("prerequisite") ||
               lower.Contains("runtime") || lower.Contains("sdk") || lower.Contains("tool");
    }
}
