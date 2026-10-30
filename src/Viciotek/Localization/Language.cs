using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Viciotek.Localization;

public sealed class Language : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("Viciotek.Localization.Texts", typeof(Language).Assembly);
    private static string _code = "es";
    public static Language Current { get; } = new();
    public static string Code => _code;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => Get(key);
    public static string Resolve(string? preference, CultureInfo systemCulture)
    {
        if (preference is "es" or "en") return preference;
        return systemCulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
    }
    public static void Use(string? preference, CultureInfo? systemCulture = null)
    {
        var code = Resolve(preference, systemCulture ?? CultureInfo.CurrentUICulture);
        if (code == _code) return;
        _code = code;
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs("Item[]"));
    }
    public static string Get(string key, params object?[] args)
    {
        var value = Resources.GetString(key, CultureInfo.GetCultureInfo(_code))
            ?? throw new MissingManifestResourceException($"Missing translation: {key}");
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }
}
