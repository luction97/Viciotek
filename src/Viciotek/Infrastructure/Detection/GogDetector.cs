using System.Text.Json;
using Viciotek.Application;
using Viciotek.Domain;

namespace Viciotek.Infrastructure.Detection;

public sealed class GogDetector : IInstalledGameDetector
{
    private readonly IEnumerable<string>? _roots;
    public GogDetector(IEnumerable<string>? roots = null) { _roots = roots; }
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

        foreach (var root in (_roots ?? roots).Distinct(StringComparer.OrdinalIgnoreCase))
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
                            if (new FileInfo(infoFile).Length > 1024 * 1024) continue;
                            var json = File.ReadAllText(infoFile);
                            using var doc = JsonDocument.Parse(json);
                            var root2 = doc.RootElement;
                            var name = root2.TryGetProperty("name", out var n) ? n.GetString() : null;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            var id=Path.GetFileNameWithoutExtension(infoFile).Replace("goggame-", "");
                            if(!long.TryParse(id,out var gameId)||gameId<=0)continue;
                            if(!root2.TryGetProperty("gameId",out var declared)||declared.GetString()!=id)continue;
                            if(root2.TryGetProperty("rootGameId",out var baseGame)&&baseGame.GetString()!=id)continue;

                            string? exe = null, args = null, workingDir = dir;
                            if (root2.TryGetProperty("playTasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var task in tasks.EnumerateArray())
                                {
                                    if (task.TryGetProperty("isPrimary", out var ip) && ip.ValueKind == JsonValueKind.True && task.TryGetProperty("type", out var type) && type.GetString() == "FileTask")
                                    {
                                        if (task.TryGetProperty("path", out var p)) exe = p.GetString();
                                        if (task.TryGetProperty("arguments", out var a)) args = a.GetString();
                                        if (task.TryGetProperty("workingDir", out var wd)) workingDir = wd.GetString() ?? dir;
                                        break;
                                    }
                                }

                            }

                            if (string.IsNullOrWhiteSpace(exe)) continue;
                            var fullPath = Path.IsPathRooted(exe!) ? exe! : Path.Combine(dir, exe!);
                            fullPath=Path.GetFullPath(fullPath);
                            if(!fullPath.StartsWith(Path.GetFullPath(dir).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)||!LaunchTargetRules.IsAllowedProgram(fullPath)||!File.Exists(fullPath))continue;

                            workingDir=string.IsNullOrWhiteSpace(workingDir)?dir:Path.GetFullPath(Path.IsPathRooted(workingDir)?workingDir:Path.Combine(dir,workingDir));
                            if(!Directory.Exists(workingDir))continue;
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
                                PlatformId: id, HighConfidence: true));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return results;
    }
}
