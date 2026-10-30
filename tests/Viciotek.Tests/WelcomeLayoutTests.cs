using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using Viciotek.Localization;

namespace Viciotek.Tests;

[Collection("Language")]
public sealed class WelcomeLayoutTests
{
    [Theory]
    [InlineData("es", 760, 440)]
    [InlineData("en", 760, 440)]
    [InlineData("es", 1280, 700)]
    [InlineData("en", 1920, 1000)]
    public Task Welcome_IsReadableAndFitsTheLibraryViewport(string language, double width, double height) => WpfTest.RunAsync(async () =>
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
        window.Width = width; window.Height = height;
        window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
        window.Left = -3000; window.ShowInTaskbar = false; window.ShowActivated = false;
        window.DataContext = new { ShowEmptyState = true };
        try
        {
            window.Show(); window.UpdateLayout();
            var root = (Grid)window.Content;
            var content = (StackPanel)root.Children[0];
            Assert.True(root.IsVisible);
            foreach (FrameworkElement child in content.Children)
            {
                var bounds = child.TransformToAncestor(root).TransformBounds(new Rect(child.RenderSize));
                Assert.InRange(bounds.Left, 0, width);
                Assert.InRange(bounds.Right, 0, width);
                Assert.InRange(bounds.Top, 0, height);
                Assert.InRange(bounds.Bottom, 0, height);
            }
            var texts = content.Children.OfType<TextBlock>().ToArray();
            Assert.Equal(3, texts.Length);
            Assert.True(texts[0].FontSize >= 32);
            Assert.True(texts[1].FontSize >= 20);
            Assert.True(texts[2].FontSize >= 15);
            var button = Assert.Single(content.Children.OfType<Button>());
            Assert.Equal(Language.Get("ChooseGame"), button.Content);
            Assert.True(button.ActualHeight >= 44);
            Assert.True(button.Focusable);
            Assert.True(button.IsTabStop);
            await Task.CompletedTask;
        }
        finally { window.Close(); Language.Use(previous); }
    });
}
