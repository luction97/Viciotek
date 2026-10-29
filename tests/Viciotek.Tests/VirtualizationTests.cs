using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Viciotek.Presentation.Controls;

namespace Viciotek.Tests;

public sealed class VirtualizationTests
{
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
