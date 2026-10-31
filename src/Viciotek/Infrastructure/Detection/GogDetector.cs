using System.Text.Json;
using Viciotek.Application;
using Viciotek.Domain;

namespace Viciotek.Infrastructure.Detection;

public sealed class GogDetector : IInstalledGameDetector
{
    public string Platform => "GOG";

    public IReadOnlyList<DetectedGame> Detect()
    {
        var results = new List<DetectedGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var roots = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GOG.com\Galaxy");
            var path = key?.GetValue("clientInstallationPath") as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var parent = Path.GetDirectoryName(path.TrimEnd('\\'));
                if (!string.IsNullOrWhiteSpace(parent)) roots.Add(parent!);
            }
        }
        catch { }

        foreach (var drive in DriveHelper.GetFixedDrives())
        {
            roots.Add(Path.Combine(drive, "GOG Games"));
            roots.Add(Path.Combine(drive, "GOG Galaxy", "Games"));
            roots.Add(Path.Combine(drive, "Program Files (x86)", "GOG Galaxy", "Games"));
            roots.Add(Path.Combine(drive, "Program Files", "GOG Galaxy", "Games"));
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    foreach (var infoFile in Directory.EnumerateFiles(dir, "goggame-*.info"))
                    {
                        try
                        {
                            var json = File.ReadAllText(infoFile);
                            using var doc = JsonDocument.Parse(json);
                            var root2 = doc.RootElement;
                            var name = root2.TryGetProperty("name", out var n) ? n.GetString() : null;
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            string? exe = null, args = null, workingDir = dir;
                            if (root2.TryGetProperty("playTasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var task in tasks.EnumerateArray())
                                {
                                    if (task.TryGetProperty("isPrimary", out var ip) && ip.GetBoolean())
                                    {
                                        if (task.TryGetProperty("path", out var p)) exe = p.GetString();
                                        if (task.TryGetProperty("arguments", out var a)) args = a.GetString();
                                        if (task.TryGetProperty("workingDir", out var wd)) workingDir = wd.GetString() ?? dir;
                                        break;
                                    }
                                }
                                if (exe is null && tasks.GetArrayLength() > 0)
                                {
                                    var first = tasks[0];
                                    if (first.TryGetProperty("path", out var p)) exe = p.GetString();
                                    if (first.TryGetProperty("arguments", out var a)) args = a.GetString();
                                    if (first.TryGetProperty("workingDir", out var wd)) workingDir = wd.GetString() ?? dir;
                                }
                            }

                            if (string.IsNullOrWhiteSpace(exe)) continue;
                            var fullPath = Path.IsPathRooted(exe!) ? exe! : Path.Combine(workingDir!, exe!);
                            if (!File.Exists(fullPath)) continue;

                            var key = fullPath.ToUpperInvariant();
                            if (!seen.Add(key)) continue;

                            results.Add(new DetectedGame(
                                Name: name!,
                                Platform: "GOG",
                                LaunchPath: fullPath,
                                LaunchKind: LaunchKind.Executable,
                                Arguments: string.IsNullOrWhiteSpace(args) ? null : args,
                                WorkingDirectory: workingDir,
                                IconPath: fullPath,
                                PlatformId: Path.GetFileNameWithoutExtension(infoFile).Replace("goggame-", "")));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return results;
    }
}
