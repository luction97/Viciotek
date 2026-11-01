using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using Viciotek.Localization;

namespace Viciotek.Tests;

[Collection("Language")]
public sealed class WelcomeLayoutTests
{
    [Fact]
    public void UndoImport_IsOnlyInLibraryBackgroundMenu()
    {
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "UiFixtures", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var cardStyle = source.Descendants().Single(e => (string?)e.Attribute(x + "Key") == "GameCardItemStyle");
        Assert.DoesNotContain(cardStyle.Descendants().Attributes("Command"), attribute => attribute.Value.Contains("UndoImportCommand"));
        var gallery = source.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "Gallery");
        Assert.Contains(gallery.Descendants().Attributes("Command"), attribute => attribute.Value.Contains("UndoImportCommand"));
    }
    [Theory]
    [InlineData("es", 760, 440, false)]
    [InlineData("en", 760, 440, false)]
    [InlineData("es", 1280, 700, false)]
    [InlineData("en", 1920, 1000, false)]
    [InlineData("es", 760, 440, true)]
    [InlineData("en", 760, 440, true)]
    [InlineData("es", 760, 440, true, 1.25)]
    [InlineData("en", 760, 440, true, 1.5)]
    public Task Welcome_IsReadableAndFitsTheLibraryViewport(string language, double width, double height, bool importStep, double scale = 1) => WpfTest.RunAsync(async () =>
    {
        var previous = Language.Code;
        Language.Use(language);
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "UiFixtures", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var welcome = source.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "EmptyLibrary");
        var markup = $$"""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:loc="clr-namespace:Viciotek.Localization;assembly=Viciotek">
                <Window.Resources><ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="/Viciotek;component/Presentation/Theme/Theme.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                    <BooleanToVisibilityConverter x:Key="BoolToVisibility" />
                </ResourceDictionary></Window.Resources>
                {{welcome}}
            </Window>
            """;
        var window = (Window)XamlReader.Parse(markup);
        window.Width = width * scale; window.Height = height * scale;
        ((FrameworkElement)window.Content).LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
        window.Left = -3000; window.ShowInTaskbar = false; window.ShowActivated = false;
        window.DataContext = new { ShowEmptyState = true, ShowWelcomeConnection = !importStep, ShowWelcomeImport = importStep };
        try
        {
            window.Show(); window.UpdateLayout();
            var root = (Grid)window.Content;
            var content = (StackPanel)root.Children[0];
            Assert.True(root.IsVisible);
            foreach (var child in Descendants(content).OfType<FrameworkElement>().Where(child => child.IsVisible))
            {
                var bounds = child.TransformToAncestor(root).TransformBounds(new Rect(child.RenderSize));
                Assert.InRange(bounds.Left, 0, width);
                Assert.InRange(bounds.Right, 0, width);
                Assert.InRange(bounds.Top, 0, height);
                Assert.InRange(bounds.Bottom, 0, height);
            }
            var texts = Descendants(content).OfType<TextBlock>().Where(text => text.IsVisible).ToArray();
            Assert.Equal(importStep ? 3 : 2, texts.Length);
            Assert.True(texts[0].FontSize >= 32);
            var buttons = Descendants(content).OfType<Button>().Where(button => button.IsVisible).ToArray();
            Assert.Equal(2, buttons.Length);
            Assert.Equal(Language.Get(importStep ? "DetectInstalledGames" : "ConnectCoverService"), buttons[0].Content);
            Assert.Equal(Language.Get(importStep ? "AddManually" : "NotNow"), buttons[1].Content);
            var primaryButton = buttons.First();
            Assert.True(primaryButton.ActualHeight >= 44);
            Assert.True(primaryButton.Focusable);
            Assert.True(primaryButton.IsTabStop);
            await Task.CompletedTask;
        }
        finally { window.Close(); Language.Use(previous); }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
