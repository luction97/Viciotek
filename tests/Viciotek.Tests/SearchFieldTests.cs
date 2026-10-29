using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Viciotek.Tests;

[CollectionDefinition("Search focus", DisableParallelization = true)]
public sealed class SearchFocusCollection;

[Collection("Search focus")]
public sealed class SearchFieldTests
{
    [Fact]
    public Task Placeholder_FollowsFocusAndTextWithoutOverlappingInput() => WpfTest.RunAsync(async () =>
    {
        var theme = new ResourceDictionary
        {
            Source = new Uri("/Viciotek;component/Presentation/Theme/Theme.xaml", UriKind.Relative)
        };
        var search = new TextBox { Style = (Style)theme["SearchTextBoxStyle"], MaxWidth = 360 };
        var other = new Button { Content = "Other" };
        var panel = new StackPanel();
        panel.Children.Add(other);
        panel.Children.Add(search);
        var window = new Window { Content = panel, Width = 400, Height = 200, Left = -3000, ShowInTaskbar = false };
        try
        {
            window.Show();
            other.Focus();
            window.UpdateLayout();
            var placeholder = (TextBlock)search.Template.FindName("SearchPlaceholder", search);
            var clear = (Button)search.Template.FindName("ClearButton", search);
            Assert.Equal(Visibility.Visible, placeholder.Visibility);
            Assert.Equal(Visibility.Collapsed, clear.Visibility);
            search.Focus();
            await WpfTest.WaitUntilAsync(() => search.IsKeyboardFocusWithin);
            Assert.Equal(Visibility.Collapsed, placeholder.Visibility);
            search.Text = "A very long game name with accents: Pokémon";
            other.Focus();
            Assert.Equal(Visibility.Collapsed, placeholder.Visibility);
            Assert.Equal(Visibility.Visible, clear.Visibility);
            search.Clear();
            Assert.Equal(Visibility.Visible, placeholder.Visibility);
            search.Focus();
            Assert.Equal(Visibility.Collapsed, placeholder.Visibility);
            window.Width = 280;
            window.UpdateLayout();
            Assert.InRange(search.ActualWidth, 100, 280);
            Assert.Equal(32, search.ActualHeight);
            window.Width = 800;
            window.UpdateLayout();
            Assert.Equal(360, search.ActualWidth);
        }
        finally { window.Close(); }
    });
}
