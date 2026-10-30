using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Viciotek.Tests;

public sealed class WelcomeAnimationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Entrance_KeepsActionsAvailableAndReleasesAnimationClocks(bool enabled) => WpfTest.RunAsync(async () =>
    {
        var clicks = 0;
        var choose = new Button { Content = "Choose game" };
        choose.Click += (_, _) => clicks++;
        var content = new StackPanel { Children = { new TextBlock { Text = "Welcome" }, choose } };
        var window = new Window
        {
            Content = content, Width = 760, Height = 540,
            Left = -3000, ShowActivated = false, ShowInTaskbar = false
        };
        try
        {
            window.Show(); window.UpdateLayout();
            var size = content.RenderSize;
            MainWindow.BeginWelcomeEntrance(content, enabled);
            Assert.Equal(enabled, content.HasAnimatedProperties);
            Assert.True(choose.IsEnabled && choose.IsHitTestVisible && choose.Focusable);
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, clicks);
            window.UpdateLayout();
            Assert.Equal(size, content.RenderSize); // Render movement must not reflow the layout.
            if (enabled)
            {
                var offset = Assert.IsType<TranslateTransform>(content.RenderTransform);
                Assert.True(offset.HasAnimatedProperties);
                await WpfTest.WaitUntilAsync(() => !content.HasAnimatedProperties);
                Assert.False(offset.HasAnimatedProperties);
                Assert.Equal(0, offset.Y);
            }
            Assert.Equal(1, content.Opacity);
            Assert.True(choose.IsEnabled);
        }
        finally { window.Close(); }
    });
}
