namespace Viciotek.Domain;

public static class LaunchTargetRules
{
    public static readonly IReadOnlySet<string> AllowedProgramExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe",
            ".com"
        };

    public static bool IsAllowedProgram(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        AllowedProgramExtensions.Contains(Path.GetExtension(path.Trim()));
}
