using Microsoft.Win32;
using Viciotek.Application;
using Viciotek.Domain;

namespace Viciotek.Infrastructure.Detection;

public sealed class RegistryPublisherDetector : IInstalledGameDetector
{
    private readonly string _platform;
    private readonly string[] _publishers;
    private readonly string[] _nameKeywords;

    public RegistryPublisherDetector(string platform, string[] publishers, string[]? nameKeywords = null)
    {
        _platform = platform;
        _publishers = publishers;
        _nameKeywords = nameKeywords ?? [];
    }

    public string Platform => _platform;

    public IReadOnlyList<DetectedGame> Detect()
    {
        var results = new List<DetectedGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var keyName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var app = uninstall.OpenSubKey(keyName);
                        var displayName = app?.GetValue("DisplayName") as string;
                        var publisher = app?.GetValue("Publisher") as string;
                        var location = app?.GetValue("InstallLocation") as string;
                        var displayIcon = app?.GetValue("DisplayIcon") as string;

                        if (string.IsNullOrWhiteSpace(displayName)) continue;
                        if (IsNonGame(displayName!)) continue;

                        // Match publisher
                        var publisherMatch = _publishers.Any(p => (publisher ?? "").Contains(p, StringComparison.OrdinalIgnoreCase));
                        // Or match name keywords
                        var nameMatch = _nameKeywords.Length == 0 || _nameKeywords.Any(k => displayName!.Contains(k, StringComparison.OrdinalIgnoreCase));
                        if (!publisherMatch && !nameMatch) continue;
                        if (_nameKeywords.Length > 0 && !nameMatch) continue;

                        // Find executable
                        string? exe = null;
                        if (!string.IsNullOrWhiteSpace(location) && Directory.Exists(location))
                        {
                            exe = FindExe(location!);
                        }
                        if (exe is null && !string.IsNullOrWhiteSpace(displayIcon))
                        {
                            var iconPath = displayIcon!.Trim().Trim('"').Split(',')[0];
                            if (File.Exists(iconPath) && iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                exe = iconPath;
                        }
                        if (exe is null) continue;

                        var key = exe.ToUpperInvariant();
                        if (!seen.Add(key)) continue;

                        results.Add(new DetectedGame(displayName!, _platform, exe, LaunchKind.Executable, null, Path.GetDirectoryName(exe), exe));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return results;
    }

    private static string? FindExe(string location)
    {
        try
        {
            // First try root level
            var rootExes = Directory.EnumerateFiles(location, "*.exe", SearchOption.TopDirectoryOnly).ToList();
            var preferred = rootExes.FirstOrDefault(e =>
            {
                var n = Path.GetFileName(e).ToLowerInvariant();
                return !n.Contains("launcher") && !n.Contains("updater") && !n.Contains("unins") &&
                       !n.Contains("redist") && !n.Contains("crash") && !n.Contains("service");
            });
            if (preferred is not null) return preferred;
            // Search one level deep
            foreach (var sub in Directory.EnumerateDirectories(location))
            {
                var subExes = Directory.EnumerateFiles(sub, "*.exe", SearchOption.TopDirectoryOnly).ToList();
                var subPreferred = subExes.FirstOrDefault(e =>
                {
                    var n = Path.GetFileName(e).ToLowerInvariant();
                    return !n.Contains("launcher") && !n.Contains("updater") && !n.Contains("unins") &&
                           !n.Contains("redist") && !n.Contains("crash") && !n.Contains("service");
                });
                if (subPreferred is not null) return subPreferred;
            }
        }
        catch { }
        return null;
    }

    private static bool IsNonGame(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("launcher") || lower.Contains("updater") || lower.Contains("redistributable") ||
               lower.Contains("runtime") || lower.Contains("sdk") || lower.Contains("tool") ||
               lower.Contains("service") || lower.Contains("framework");
    }
}
