using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Viciotek.Infrastructure.Shell;
using Viciotek.Presentation.Controls;
using Viciotek.Presentation.Theme;

namespace Viciotek.Tests;

public sealed class AccessibilityRefinementTests
{
    [Fact]
    public Task ContrastOverride_UpdatesExistingControlsAndRestoresNormalPalette() => WpfTest.RunAsync(async () =>
    {
        var host = new Border();
        host.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Application.LoadComponent(
            new Uri("/Viciotek;component/Presentation/Theme/Theme.xaml", UriKind.Relative)));
        host.SetResourceReference(Border.BackgroundProperty, "BgBrush");
        using var theme = new AccessibilityTheme(host.Resources);
        theme.Apply(false);
        var normal = Assert.IsType<SolidColorBrush>(host.Background).Color;
        theme.Apply(true);
        await WpfTest.WaitUntilAsync(() => ((SolidColorBrush)host.Background).Color == SystemColors.WindowColor);
        Assert.Same(SystemColors.WindowTextBrush, host.FindResource("TextSecondaryBrush"));
        theme.Apply(false);
        await WpfTest.WaitUntilAsync(() => ((SolidColorBrush)host.Background).Color == normal);
    });

    [Theory]
    [InlineData(1.25, 1536, 864)]
    [InlineData(1.5, 1280, 720)]
    [InlineData(2, 960, 540)]
    public void MonitorWorkArea_UsesItsOwnDpi(double scale, double width, double height)
    {
        Assert.Equal(new Size(width, height), WindowAppearance.WorkAreaSizeInDips(1920, 1080, scale));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Hover_RespectsBothMotionPreferences(bool app, bool windows, bool expected) =>
        Assert.Equal(expected, CardHoverBehavior.ShouldAnimate(app, windows));
}
