using System.Windows.Media;
using System.Windows.Media.Imaging;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Presentation.Controls;

namespace Viciotek.Tests;

public sealed class CoverSearchContinuityTests
{
    [Fact]
    public Task FilteringRealVirtualizedGallery_KeepsWarmImagesAtLayoutWithoutExtraDecodes() => WpfTest.RunAsync(async () =>
    {
        var calls = 0;
        var loader = new CoverImageLoader(_ =>
        {
            calls++;
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            bitmap.Freeze();
            return bitmap;
        });
        var items = Enumerable.Range(0, 75).Select(i => new { Path = "game-" + i, Name = "Game " + i }).ToArray();
        var displayed = new Dictionary<string, BitmapSource>();
        foreach (var item in items) displayed[item.Path] = (await loader.LoadAsync(item.Path, "revision"))!;
        var factory = new System.Windows.FrameworkElementFactory(typeof(CoverImage));
        factory.SetValue(CoverImage.LoaderProperty, loader);
        factory.SetValue(CoverImage.CoverVersionProperty, "revision");
        factory.SetBinding(CoverImage.CoverPathProperty, new System.Windows.Data.Binding("Path"));
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(items);
        var gallery = new System.Windows.Controls.ListBox { ItemsSource = view,
            ItemTemplate = new System.Windows.DataTemplate { VisualTree = factory },
            ItemsPanel = new System.Windows.Controls.ItemsPanelTemplate(new System.Windows.FrameworkElementFactory(typeof(VirtualizingWrapPanel))) };
        System.Windows.Controls.ScrollViewer.SetCanContentScroll(gallery, true);
        var window = new System.Windows.Window { Content = gallery, Width = 1000, Height = 700, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            foreach (var query in new[] { "Game", "Game 1", "Game 12", "missing", "Game 7", "" })
            {
                view.Filter = item => ((string)item.GetType().GetProperty("Name")!.GetValue(item)!).Contains(query);
                gallery.UpdateLayout();
                foreach (var image in Images(gallery))
                    if (image.IsVisible) Assert.Same(displayed[image.CoverPath!], image.Source);
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            Assert.Equal(75, calls);
        }
        finally { window.Close(); }
        GC.KeepAlive(displayed);
    });

    private static IEnumerable<CoverImage> Images(System.Windows.DependencyObject root)
    {
        if (root is CoverImage image) yield return image;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Images(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    [Fact]
    public Task RecreatedCachedCards_HaveTheirBitmapBeforeLoadedEvenBeyondStrongCache() => WpfTest.RunAsync(async () =>
    {
        var calls = 0;
        var loader = new CoverImageLoader(_ =>
        {
            Interlocked.Increment(ref calls);
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            bitmap.Freeze();
            return bitmap;
        });
        var displayed = new List<BitmapSource>();
        for (var i = 0; i < 75; i++)
            displayed.Add((await loader.LoadAsync("cover-" + i, "revision"))!);
        for (var i = 0; i < displayed.Count; i++)
        {
            var recreated = new CoverImage { Loader = loader, CoverPath = "cover-" + i, CoverVersion = "revision" };
            Assert.Same(displayed[i], recreated.Source);
        }
        Assert.Equal(75, calls);
        GC.KeepAlive(displayed);
    });

    [Fact]
    public Task CachedRevisionChange_AdoptsCorrectBitmapImmediately() => WpfTest.RunAsync(async () =>
    {
        var loader = new CoverImageLoader(_ =>
        {
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            bitmap.Freeze();
            return bitmap;
        });
        var old = await loader.LoadAsync("cover", "old");
        var replacement = await loader.LoadAsync("cover", "new");
        var image = new CoverImage { Loader = loader, CoverPath = "cover", CoverVersion = "old" };
        Assert.Same(old, image.Source);
        image.CoverVersion = "new";
        Assert.Same(replacement, image.Source);
    });
}
