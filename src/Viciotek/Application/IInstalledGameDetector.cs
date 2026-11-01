using Viciotek.Domain;

namespace Viciotek.Application;

public sealed record DetectedGame(
    string Name,
    string Platform,
    string LaunchPath,
    LaunchKind LaunchKind = LaunchKind.Executable,
    string? Arguments = null,
    string? WorkingDirectory = null,
    string? IconPath = null,
    string? PlatformId = null,
    bool HighConfidence = false,
    string? ExecutablePath = null);

public interface IInstalledGameDetector
{
    string Platform { get; }
    IReadOnlyList<DetectedGame> Detect();
}
