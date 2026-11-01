using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Viciotek.Presentation.Controls;

namespace Viciotek.Tests;

public sealed class CardHoverBehaviorTests
{
    [Fact]
    public Task EnabledBeforeLoad_ScalesWithoutLayoutChangesAndReturnsOnLeave() => WpfTest.RunAsync(async () =>
    {
        var card = new Border { Width = 200, Height = 300 };
        CardHoverBehavior.SetEnabled(card, true);
        var window = new Window { Content = card, Left = -3000, WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            await WpfTest.WaitUntilAsync(() => card.RenderTransform is TransformGroup g && ((TranslateTransform)g.Children[1]).Y <= -5.99);
            var transform = Assert.IsType<TranslateTransform>(((TransformGroup)card.RenderTransform).Children[1]);
            Assert.Equal(1.015, ((ScaleTransform)((TransformGroup)card.RenderTransform).Children[0]).ScaleX, 3);
            Assert.NotNull(card.Effect);
            Assert.Equal(200, card.ActualWidth);
            Assert.Equal(300, card.ActualHeight);

            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            await WpfTest.WaitUntilAsync(() => Math.Abs(transform.Y) < 0.01);
            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });

            // Disabling motion must cancel even an in-flight animation.
            CardHoverBehavior.SetEnabled(card, false);
            Assert.Equal(0, transform.Y);
            Assert.False(transform.HasAnimatedProperties);
            Assert.Null(card.Effect);
            var scale = (ScaleTransform)((TransformGroup)card.RenderTransform).Children[0];
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(1, scale.ScaleY);
            Assert.False(scale.HasAnimatedProperties);
            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Assert.Equal(0, transform.Y);
            Assert.False(transform.HasAnimatedProperties);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EnabledAfterLoad_UnloadClearsMotion() => WpfTest.RunAsync(async () =>
    {
        var card = new Border { Width = 200, Height = 300 };
        var window = new Window { Content = card, Left = -3000, WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            CardHoverBehavior.SetEnabled(card, true);
            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            await WpfTest.WaitUntilAsync(() => card.RenderTransform is TransformGroup g && ((TranslateTransform)g.Children[1]).Y <= -5.99);
            card.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            var transform = Assert.IsType<TranslateTransform>(((TransformGroup)card.RenderTransform).Children[1]);
            Assert.Equal(0, transform.Y);
            Assert.False(transform.HasAnimatedProperties);
        }
        finally { window.Close(); }
    });
}
