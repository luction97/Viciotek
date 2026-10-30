using System.Diagnostics.CodeAnalysis;

namespace Viciotek.Domain;

/// <summary>
/// Only launcher protocols explicitly known by VICIOTEK may be launched.
/// This prevents a .url file from invoking arbitrary shell handlers.
/// </summary>
public static class ProtocolPolicy
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam",
        "com.epicgames.launcher",
        "uplay",
        "ubisoftconnect",
        "origin",
        "origin2",
        "ea",
        "link2ea",
        "goggalaxy",
        "battlenet",
        "blizzard",
        "rockstar",
        "rockstargames",
        "xbox"
    };

    public static IReadOnlyCollection<string> SupportedSchemes => AllowedSchemes;

    public static bool TryValidate(string value, [NotNullWhen(true)] out Uri? uri, out string? error)
    {
        uri = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = L.Get("TheDestinationIsEmpty");
            return false;
        }

        var candidate = value.Trim();
        if (candidate.ContainsAnyControlCharacters())
        {
            error = L.Get("TheDestinationContainsInvalidCharacters");
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
        {
            error = L.Get("TheDestinationIsNotAValidUrl");
            return false;
        }

        if (!AllowedSchemes.Contains(parsed.Scheme))
        {
            error = L.Get("The0ProtocolIsNotAllowed", parsed.Scheme) +
                    L.Get("ViciotekOpensSupportedLauncherShortcutsOnly");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = L.Get("TheUrlContainsCredentialsAndIsNotSafe");
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool ContainsAnyControlCharacters(this string value) =>
        value.Any(char.IsControl);
}
