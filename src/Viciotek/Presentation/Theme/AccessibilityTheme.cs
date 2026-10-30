using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Viciotek.Presentation.Theme;

/// <summary>Event-driven system palette override; normal colors stay in Theme.xaml.</summary>
internal sealed class AccessibilityTheme : IDisposable
{
    private readonly ResourceDictionary _resources;
    private readonly ResourceDictionary _contrast = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _disposed;

    public AccessibilityTheme(ResourceDictionary resources)
    {
        _resources = resources;
        resources.MergedDictionaries.Add(_contrast);
        Apply(SystemParameters.HighContrast);
        SystemParameters.StaticPropertyChanged += OnSystemChanged;
    }

    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || e.PropertyName != nameof(SystemParameters.HighContrast)) return;
        if (_dispatcher.CheckAccess()) Apply(SystemParameters.HighContrast);
        else _dispatcher.BeginInvoke(() => { if (!_disposed) Apply(SystemParameters.HighContrast); });
    }

    internal void Apply(bool highContrast)
    {
        _contrast.Clear();
        if (!highContrast) return;
        _contrast["HighContrastVisibility"] = Visibility.Visible;
        foreach (var key in new[] { "BgBrush", "SurfaceBrush", "SurfaceRaisedBrush", "DialogBackgroundBrush", "SectionBackgroundBrush", "InputBackgroundBrush", "OverlayBrush", "DropOverlayBrush" })
            _contrast[key] = SystemColors.WindowBrush;
        foreach (var key in new[] { "NameOverlayBrush", "CardBusyBackgroundBrush", "CardProgressBackgroundBrush", "DropHintBackgroundBrush", "SearchSurfaceBrush", "ProviderIconBackgroundBrush", "PreviewLoadingBrush" })
            _contrast[key] = SystemColors.WindowBrush;
        _contrast["HighlightedTextBrush"] = SystemColors.HighlightTextBrush;
        foreach (var key in new[] { "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "BorderBrush", "BorderStrongBrush", "DangerBrush", "CardHoverBrush", "CardSelectionBrush", "SelectionBrush" })
            _contrast[key] = SystemColors.WindowTextBrush;
        foreach (var key in new[] { "AccentBrush", "ActionAccentBrush", "AccentMutedBrush", "CardAccentBrush", "SurfaceHoverBrush" })
            _contrast[key] = SystemColors.HighlightBrush;
        _contrast["ActionAccentBrush"] = SystemColors.HotTrackBrush;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemParameters.StaticPropertyChanged -= OnSystemChanged;
        _resources.MergedDictionaries.Remove(_contrast);
    }
}
