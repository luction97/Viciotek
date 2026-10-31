using Viciotek.Application;

namespace Viciotek.Infrastructure.Detection;

public sealed class InstalledGameScanner(IEnumerable<IInstalledGameDetector> detectors)
{
    public IReadOnlyList<DetectedGame> ScanAll()
    {
        var results = new List<DetectedGame>();
        foreach (var detector in detectors)
        {
            try
            {
                results.AddRange(detector.Detect());
            }
            catch { }
        }
        return results;
    }

    public IReadOnlyList<DetectedGame> Scan(string platform)
    {
        var detector = detectors.FirstOrDefault(d => d.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase));
        return detector?.Detect() ?? [];
    }
}
