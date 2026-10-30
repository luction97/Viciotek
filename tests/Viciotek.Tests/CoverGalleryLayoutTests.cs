using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Presentation;
using Viciotek.Presentation.Controls;

namespace Viciotek.Tests;

public sealed class CoverGalleryLayoutTests
{
    [Theory]
    [InlineData(1000, 960, 10)]
    [InlineData(1000, 900, 10)]
    [InlineData(1000, 780, 10)]
    [InlineData(1000, 700, 5)]
    [InlineData(760, 600, 3)]
    public Task Results_GetPriorityAndRemainVirtualized(double width, double height, int minimumVisible) => WpfTest.RunAsync(async () =>
    {
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "UiFixtures", "CoverGalleryDialog.xaml"));
        var content = source.Root!.Elements().Single();
        foreach (var attribute in content.DescendantsAndSelf().Attributes().Where(a =>
                     a.Name.LocalName is "Click" or "KeyDown" or "MouseLeftButtonDown" or "SelectionChanged" or "PreviewKeyDown" or "PreviewMouseLeftButtonUp" or "IsKeyboardFocusWithinChanged").ToArray())
            attribute.Remove(); // Exercise production layout without dialog code-behind or network.
        var markup = $$"""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:loc="clr-namespace:Viciotek.Localization;assembly=Viciotek"
                    xmlns:presentation="clr-namespace:Viciotek.Presentation;assembly=Viciotek"
                    xmlns:controls="clr-namespace:Viciotek.Presentation.Controls;assembly=Viciotek">
                <Window.Resources><ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="/Viciotek;component/Presentation/Theme/Theme.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                    <BooleanToVisibilityConverter x:Key="BoolToVisibility" />
                </ResourceDictionary></Window.Resources>
                {{content}}
            </Window>
            """;
        markup = markup.Replace("clr-namespace:Viciotek.Presentation.Controls\"", "clr-namespace:Viciotek.Presentation.Controls;assembly=Viciotek\"")
            .Replace("clr-namespace:Viciotek.Presentation\"", "clr-namespace:Viciotek.Presentation;assembly=Viciotek\"");
        var window = (Window)XamlReader.Parse(markup);
        using var client = new SteamGridDbClient(null);
        var vm = new CoverGalleryViewModel(client, new Game(), (_, _) => Task.FromResult(true), true);
        for (var i = 0; i < 20; i++) vm.Matches.Add(new(i + 1, $"Edition {i}"));
        for (var i = 0; i < 500; i++) vm.Candidates.Add(new(new(i + 1, $"https://example.invalid/{i}.png", 600, 900)));
        window.DataContext = vm;
        window.Width = width; window.Height = height;
        window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
        window.Left = -3000; window.ShowInTaskbar = false; window.ShowActivated = false;
        window.UseLayoutRounding = true;
        try
        {
            window.Show(); window.UpdateLayout();
            var results = (ListBox)window.FindName("CoverResults");
            var suggestions = (ListBox)window.FindName("Suggestions");
            var panel = Find<VirtualizingWrapPanel>(results);
            Assert.Equal(0, suggestions.ActualHeight);
            Assert.True(panel.ViewportHeight > suggestions.ActualHeight * 4);
            var visible = Enumerable.Range(0, vm.Candidates.Count)
                .Select(i => results.ItemContainerGenerator.ContainerFromIndex(i)).OfType<ListBoxItem>()
                .Count(item => { var point = item.TranslatePoint(new Point(), panel); return point.Y >= -0.1 && point.Y + item.ActualHeight <= panel.ViewportHeight + 0.1; });
            Assert.True(visible >= minimumVisible, $"Only {visible} complete covers at {width}x{height}; viewport {panel.ViewportWidth}x{panel.ViewportHeight}");
            Assert.InRange(panel.RealizedItemCount, minimumVisible, 60);
            Assert.Equal(160, panel.EffectiveItemWidth);
            Assert.Equal(240, panel.EffectiveItemHeight);
            var footer = Find<Button>(window, b => Equals(b.Content, Viciotek.Localization.Language.Get("UseThisCover")));
            Assert.True(results.TranslatePoint(new Point(0, results.ActualHeight), window).Y <= footer.TranslatePoint(new Point(), window).Y);
            Assert.True(footer.TranslatePoint(new Point(0, footer.ActualHeight), window).Y <= height);
            panel.SetVerticalOffset(panel.ExtentHeight); window.UpdateLayout();
            await WpfTest.WaitUntilAsync(() => results.ItemContainerGenerator.ContainerFromIndex(499) is not null);
            var last = (ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(499);
            Assert.InRange(last.TranslatePoint(new Point(0, last.ActualHeight), panel).Y, 0, panel.ViewportHeight + 0.1);
        }
        finally { window.Close(); await vm.CloseAsync(); }
    });

    private static T Find<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        if (root is T found && (match is null || match(found))) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            try { return Find(VisualTreeHelper.GetChild(root, i), match); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException(typeof(T).Name);
    }
}
