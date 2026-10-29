using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Viciotek.Domain;

public static class GameTitle
{
    private static readonly Dictionary<string, string> RomanNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["i"] = "1", ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6",
        ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10"
    };

    public static string Clean(string value) => Regex.Replace(
        Regex.Replace(value.Replace('_', ' ').Replace('.', ' '),
            @"(?:[- ](?:win64|win32|x64|x86|shipping|release))+$", "", RegexOptions.IgnoreCase),
        @"\s+", " ").Trim();

    public static string Normalize(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark || character is '™' or '®') continue;
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }
        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(word => RomanNumbers.GetValueOrDefault(word, word)));
    }

    public static bool IsDescriptive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200) return false;
        var normalized = Normalize(value);
        return normalized.Length >= 2 && !Guid.TryParse(value, out _) &&
            !Regex.IsMatch(normalized, @"^(?:game|game exe|launcher|start|setup|application|bootstrap|ue4|ue5|ue4game|ue5game|unreal.*|unity.*|cryengine.*|engine|binaries|win64|win32|x64|x86|bin|shipping|release|steam|steamapps|steamlibrary|steam library|common|games|juegos|program files.*|microsoft.*|windows.*|default.*|my project.*|project\d*|desktop|downloads|documents|temp|users|appdata|local|roaming|programdata|test|stub)$");
    }

    public static bool IsAbbreviation(string value) => Regex.IsMatch(Normalize(value), @"^[a-z]{1,3}\d*$");
}
