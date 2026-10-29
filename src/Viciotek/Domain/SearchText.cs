using System.Globalization;
using System.Text;

namespace Viciotek.Domain;

public static class SearchText
{
    /// <summary>
    /// Case- and accent-insensitive search key. "Pokémon" and "pokemon"
    /// produce the same value, while digits and symbols are preserved.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool Matches(string? gameName, string? query)
    {
        var normalizedQuery = Normalize(query);
        return normalizedQuery.Length == 0 ||
               Normalize(gameName).Contains(normalizedQuery, StringComparison.Ordinal);
    }
}
