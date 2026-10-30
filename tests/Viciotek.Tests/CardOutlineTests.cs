using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace Viciotek.Tests;

public sealed class CardOutlineTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument ReadTheme() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "UiFixtures", "Theme.xaml"));

    private static XDocument ReadWindow() => XDocument.Load(
        Path.Combine(AppContext.BaseDirectory, "UiFixtures", "MainWindow.xaml"));

    [Fact]
    public void Card_HasOneSharedOutlineInsideAnimatedRootWithFocusPriority()
    {
        var document = ReadWindow();
        var itemStyle = document.Descendants(Ui + "Style").Single(e =>
            (string?)e.Attribute(Xaml + "Key") == "GameCardItemStyle");
        Assert.Empty(itemStyle.Descendants(Ui + "Border"));

        var template = document.Descendants(Ui + "DataTemplate").Single(e =>
            (string?)e.Attribute(Xaml + "Key") == "GameCardTemplate");
        var outline = template.Descendants(Ui + "Border").Single(e =>
            (string?)e.Attribute(Xaml + "Name") == "CardOutline");
        var root = template.Elements().Single();
        Assert.Same(root, outline.Parent);
        Assert.Contains(root.Attributes(), a => a.Name.LocalName == "CardHoverBehavior.Enabled");
        Assert.Equal("False", (string?)outline.Attribute("IsHitTestVisible"));
        Assert.Equal("10", (string?)outline.Attribute("CornerRadius"));
        // No other layer may draw a competing stroke (including the drop overlay).
        Assert.DoesNotContain(template.Descendants().Where(e => e != outline), e =>
            e.Attribute("BorderThickness") is not null || e.Elements(Ui + "Border.Style")
                .Descendants(Ui + "Setter").Any(s => (string?)s.Attribute("Property") == "BorderThickness"));

        var triggers = outline.Descendants(Ui + "DataTrigger").ToArray();
        Assert.Equal(4, triggers.Length);
        Assert.Contains("IsMouseOver", triggers[0].Attribute("Binding")!.Value);
        Assert.Contains("IsSelected", triggers[1].Attribute("Binding")!.Value);
        Assert.Contains("IsKeyboardFocusWithin", triggers[2].Attribute("Binding")!.Value);
        Assert.Contains("IsCoverDropTarget", triggers[3].Attribute("Binding")!.Value);
        var brushes = new[] { "CardHoverBrush", "CardSelectionBrush", "TextPrimaryBrush", "CardAccentBrush" };
        for (var index = 0; index < triggers.Length; index++)
        {
            Assert.Contains(triggers[index].Elements(Ui + "Setter"), s =>
                (string?)s.Attribute("Property") == "BorderBrush" &&
                (string?)s.Attribute("Value") == $"{{DynamicResource {brushes[index]}}}");
        }
        var accent = ReadTheme().Descendants(Ui + "SolidColorBrush").Single(e =>
            (string?)e.Attribute(Xaml + "Key") == "CardAccentBrush");
        Assert.Equal("#149DFF", (string?)accent.Attribute("Color"));
        Assert.Contains(triggers[2].Elements(Ui + "Setter"), s =>
            (string?)s.Attribute("Property") == "BorderBrush" &&
            (string?)s.Attribute("Value") == "{DynamicResource TextPrimaryBrush}");
    }

    [Fact]
    public Task Selection_UsesSameBoundsAndTransformAsCover() => WpfTest.RunAsync(async () =>
    {
        var document = ReadWindow();
        var outlineXml = document.Descendants(Ui + "Border").Single(e =>
            (string?)e.Attribute(Xaml + "Name") == "CardOutline");
        var itemStyle = document.Descendants(Ui + "Style").Single(e =>
            (string?)e.Attribute(Xaml + "Key") == "GameCardItemStyle");
        var cardBrushes = ReadTheme().Descendants(Ui + "SolidColorBrush").Where(e =>
            ((string?)e.Attribute(Xaml + "Key"))?.StartsWith("Card", StringComparison.Ordinal) == true);
        // Exercise the production style and outline, not a separately authored imitation.
        var item = (ListBoxItem)XamlReader.Parse($$"""
            <ListBoxItem xmlns="{{Ui}}" xmlns:x="{{Xaml}}" xmlns:loc="clr-namespace:Viciotek.Localization;assembly=Viciotek">
                <ListBoxItem.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source="/Viciotek;component/Presentation/Theme/Theme.xaml" /></ResourceDictionary.MergedDictionaries>
                    {{string.Join(Environment.NewLine, cardBrushes)}}
                    <SolidColorBrush x:Key="TextPrimaryBrush" Color="#F3F4F6" />
                    {{itemStyle}}
                </ResourceDictionary></ListBoxItem.Resources>
                <Grid x:Name="CardRoot">
                    <Grid.RenderTransform><TranslateTransform Y="-6" /></Grid.RenderTransform>
                    <Border x:Name="Cover" CornerRadius="10" Background="Black" />
                    {{outlineXml}}
                </Grid>
            </ListBoxItem>
            """);
        item.Style = (Style)item.Resources["GameCardItemStyle"];
        var window = new Window { Content = item, Width = 400, Height = 500, Left = -3000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            var outline = (Border)item.FindName("CardOutline");
            var cover = (Border)item.FindName("Cover");
            var root = (Grid)item.FindName("CardRoot");
            item.IsSelected = true;
            await WpfTest.WaitUntilAsync(() => outline.BorderThickness == new Thickness(1.5));
            Assert.Same(item.Resources["CardSelectionBrush"], outline.BorderBrush);
            Assert.Equal(cover.ActualWidth, outline.ActualWidth);
            Assert.Equal(cover.ActualHeight, outline.ActualHeight);
            Assert.Equal(cover.TranslatePoint(new Point(), item), outline.TranslatePoint(new Point(), item));
            Assert.Same(root, VisualTreeHelper.GetParent(outline));
            Assert.Equal(-6, Assert.IsType<TranslateTransform>(root.RenderTransform).Y);
            item.IsSelected = false;
            await WpfTest.WaitUntilAsync(() => outline.BorderThickness == new Thickness(1));
        }
        finally { window.Close(); }
    });
}
