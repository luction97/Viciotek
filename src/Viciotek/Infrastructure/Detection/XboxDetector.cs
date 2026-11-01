using Viciotek.Application;
namespace Viciotek.Infrastructure.Detection;
/// <summary>Disabled until installed Xbox packages can be classified as games from authoritative product metadata.</summary>
public sealed class XboxDetector : IInstalledGameDetector
{
    public string Platform => "Xbox / Game Pass";
    public IReadOnlyList<DetectedGame> Detect() => [];
}
