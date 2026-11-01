using System.Text.Json;
using Viciotek.Application;
using Viciotek.Domain;
namespace Viciotek.Infrastructure.Detection;
public sealed class EpicDetector : IInstalledGameDetector
{
    private readonly string _manifests;
    public EpicDetector(string? manifests = null) { _manifests = manifests ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests"); }
    public string Platform => "Epic Games";
    public IReadOnlyList<DetectedGame> Detect()
    {
        var games = new List<DetectedGame>(); if (!Directory.Exists(_manifests)) return games;
        foreach (var file in Directory.EnumerateFiles(_manifests, "*.item"))
        {
            try
            {
                if (new FileInfo(file).Length > 1024 * 1024) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(file)); var root = doc.RootElement;
                string? Get(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (!root.TryGetProperty("AppCategories", out var categories) || categories.ValueKind != JsonValueKind.Array || !categories.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == "games")) continue;
                if (!root.TryGetProperty("bIsIncompleteInstall", out var incomplete) || incomplete.ValueKind != JsonValueKind.False) continue;
                if (!root.TryGetProperty("bIsApplication", out var application) || application.ValueKind != JsonValueKind.True) continue;
                var name = Get("DisplayName"); var install = Get("InstallLocation"); var exe = Get("LaunchExecutable"); var id = Get("AppName"); var ns = Get("CatalogNamespace"); var item = Get("CatalogItemId");
                if (new[] { name, install, exe, id, ns, item }.Any(string.IsNullOrWhiteSpace)) continue;
                if (root.TryGetProperty("MainGameAppName", out var parent) && parent.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(parent.GetString()) && parent.GetString() != id) continue;
                var full = Path.GetFullPath(Path.Combine(install!, exe!)); var basePath = Path.GetFullPath(install!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) || !LaunchTargetRules.IsAllowedProgram(full) || !File.Exists(full)) continue;
                var uri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(ns!)}:{Uri.EscapeDataString(item!)}:{Uri.EscapeDataString(id!)}?action=launch&silent=true";
                games.Add(new(name!, Platform, uri, LaunchKind.Uri, WorkingDirectory: install, IconPath: full, PlatformId: id, HighConfidence: true, ExecutablePath: full));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { }
        }
        return games.DistinctBy(g => g.PlatformId).ToList();
    }
}
