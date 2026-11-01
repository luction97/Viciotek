using Viciotek.Application;
namespace Viciotek.Infrastructure.Detection;
/// <summary>Publisher/uninstall entries do not prove that a product is a game. No generic registry fallback.</summary>
public sealed class RegistryPublisherDetector : IInstalledGameDetector
{
    public RegistryPublisherDetector(string platform, string[] publishers, string[]? nameKeywords = null) { Platform = platform; }
    public string Platform { get; }
    public IReadOnlyList<DetectedGame> Detect() => [];
}
