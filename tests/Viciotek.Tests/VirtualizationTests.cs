using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Viciotek.Presentation.Controls;

namespace Viciotek.Tests;

public sealed class VirtualizationTests
{
    [Theory]
    [InlineData(500)]
    [InlineData(5000)]
    public Task CoverPresets_ResizeWhileScrolledWithoutOverflow(int count) => WpfTest.RunAsync(async () =>
    {
        var factory = new FrameworkElementFactory(typeof(VirtualizingWrapPanel));
        factory.SetValue(VirtualizingWrapPanel.ResponsiveProperty, true);
        var gallery = new GameGallery { Width = 1280, Height = 800,
            ItemsSource = Enumerable.Range(0, count).ToArray(), ItemsPanel = new ItemsPanelTemplate(factory) };
        ScrollViewer.SetCanContentScroll(gallery, true);
        var window = new Window { Content = gallery, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await WpfTest.WaitUntilAsync(() => FindPanel(gallery)?.RealizedItemCount > 0);
            var panel = FindPanel(gallery)!;
            foreach (var preset in new[] { 0, 1, 2, 3, 0 })
            foreach (var width in new[] { 400, 760, 1024, 1920, 760 })
            {
                var preferred = Viciotek.Infrastructure.Library.AppSettings.CoverWidthFor(preset);
                panel.ItemWidth = preferred;
                panel.ItemHeight = preferred * 1.5;
                gallery.Width = width;
                gallery.UpdateLayout();
                gallery.ScrollIntoView(gallery.Items[count - 1]);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                gallery.UpdateLayout();
                Assert.Equal(1.5, panel.EffectiveItemHeight / panel.EffectiveItemWidth, 6);
                Assert.InRange(panel.EffectiveItemWidth, 1, preferred * 1.15);
                var last = Assert.IsAssignableFrom<FrameworkElement>(gallery.ItemContainerGenerator.ContainerFromIndex(count - 1));
                var position = last.TranslatePoint(new Point(), panel);
                Assert.True(position.X >= 0 && position.X + last.ActualWidth <= panel.ViewportWidth + 0.1);
                Assert.True(position.Y >= -0.1 && position.Y + last.ActualHeight <= panel.ViewportHeight + 0.1);
                AssertBounded(panel);
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(500)]
    [InlineData(5000)]
    public Task ResponsiveGallery_KeepsLargeCoversAndBoundedContainers(int count) => WpfTest.RunAsync(async () =>
    {
        var factory = new FrameworkElementFactory(typeof(VirtualizingWrapPanel));
        factory.SetValue(VirtualizingWrapPanel.ResponsiveProperty, true);
        factory.SetValue(VirtualizingWrapPanel.ItemWidthProperty, 280d);
        factory.SetValue(VirtualizingWrapPanel.ItemHeightProperty, 420d);
        var gallery = new GameGallery { Width = 1280, Height = 800,
            ItemsSource = Enumerable.Range(0, count).Select(i => $"Game {i}").ToArray(),
            ItemsPanel = new ItemsPanelTemplate(factory) };
        ScrollViewer.SetCanContentScroll(gallery, true);
        var window = new Window { Content = gallery, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await WpfTest.WaitUntilAsync(() => FindPanel(gallery)?.RealizedItemCount > 0);
            var panel = FindPanel(gallery)!;
            foreach (var (width, columns) in new[] { (760, 2), (1024, 3), (1280, 4), (1920, 6), (760, 2) })
            {
                gallery.Width = width;
                gallery.UpdateLayout();
                await WpfTest.WaitUntilAsync(() => panel.Columns == columns);
                Assert.InRange(panel.EffectiveItemWidth, 280, 322);
                Assert.Equal(1.5, panel.EffectiveItemHeight / panel.EffectiveItemWidth, 6);
                AssertBounded(panel);
                gallery.ScrollIntoView(gallery.Items[count - 1]);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                gallery.UpdateLayout();
                Assert.NotNull(gallery.ItemContainerGenerator.ContainerFromIndex(count - 1));
                AssertBounded(panel);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InitiallyEmptyRemoteGallery_RealizesResultsWithoutResizeOrScroll() => WpfTest.RunAsync(async () =>
    {
        var items = new ObservableCollection<string>();
        var gallery = new ListBox
        {
            ItemsSource = items,
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingWrapPanel)))
        };
        ScrollViewer.SetCanContentScroll(gallery, true);
        var window = new Window { Width = 760, Height = 620, Content = gallery, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            items.Add("Downloaded result");
            await WpfTest.WaitUntilAsync(() => FindPanel(gallery)?.RealizedItemCount == 1);
            Assert.NotNull(gallery.ItemContainerGenerator.ContainerFromIndex(0));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Gallery_RealizesBoundedContainersAtStartupScrollResizeAndFilterReset() => WpfTest.RunAsync(async () =>
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, 500).Select(i => $"Game {i}"));
        var gallery = new GameGallery
        {
            Width = 1280,
            Height = 800,
            ItemsSource = items,
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingWrapPanel)))
        };
        ScrollViewer.SetCanContentScroll(gallery, true);
        // El contenido fija su ancho: los runners de CI usan una pantalla de
        // 1024x768 y Windows recorta la ventana, no el layout del panel.
        var window = new Window
        {
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Content = gallery,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        try
        {
            window.Show();
            await WpfTest.WaitUntilAsync(() => FindPanel(gallery)?.RealizedItemCount > 0);
            var panel = Assert.IsType<VirtualizingWrapPanel>(FindPanel(gallery));
            Assert.True(
                panel.Columns == 5,
                $"Se esperaban 5 columnas; gallery={gallery.ActualWidth}, panel={panel.ActualWidth}, columnas={panel.Columns}.");
            Assert.True(
                panel.RealizedItemCount == 20,
                $"Se esperaban 20 contenedores; realizados={panel.RealizedItemCount}.");
            AssertBounded(panel);

            foreach (var index in new[] { 250, 499, 0 })
            {
                gallery.ScrollIntoView(items[index]);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                gallery.UpdateLayout();
                Assert.NotNull(gallery.ItemContainerGenerator.ContainerFromIndex(index));
                AssertBounded(panel);
            }

            gallery.Width = 820;
            await WpfTest.WaitUntilAsync(() => panel.Columns == 3);
            Assert.True(
                panel.Columns == 3,
                $"Se esperaban 3 columnas; gallery={gallery.ActualWidth}, panel={panel.ActualWidth}, columnas={panel.Columns}.");
            AssertBounded(panel);

            items.Clear();
            await WpfTest.WaitUntilAsync(() => panel.RealizedItemCount == 0);
            items.Add("New game");
            await WpfTest.WaitUntilAsync(() => panel.RealizedItemCount == 1);
            Assert.NotNull(gallery.ItemContainerGenerator.ContainerFromIndex(0));
        }
        finally
        {
            window.Close();
        }
    });

    private static void AssertBounded(VirtualizingWrapPanel panel) =>
        Assert.InRange(panel.RealizedItemCount, 1, panel.Columns * 7);

    private static VirtualizingWrapPanel? FindPanel(DependencyObject root)
    {
        if (root is VirtualizingWrapPanel panel)
        {
            return panel;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindPanel(VisualTreeHelper.GetChild(root, i)) is { } child)
            {
                return child;
            }
        }

        return null;
    }
}
