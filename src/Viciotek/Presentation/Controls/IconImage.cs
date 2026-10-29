using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Viciotek.Infrastructure.Shell;

namespace Viciotek.Presentation.Controls;

public sealed class IconImage : Image
{
    public static readonly DependencyProperty IconPathProperty = DependencyProperty.Register(
        nameof(IconPath),
        typeof(string),
        typeof(IconImage),
        new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty LoaderProperty = DependencyProperty.Register(
        nameof(Loader),
        typeof(IconService),
        typeof(IconImage),
        new PropertyMetadata(null, OnSourceChanged));

    private CancellationTokenSource? _loadCancellation;

    public IconImage()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? IconPath
    {
        get => (string?)GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public IconService? Loader
    {
        get => (IconService?)GetValue(LoaderProperty);
        set => SetValue(LoaderProperty, value);
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((IconImage)dependencyObject).RefreshImage();

    private void OnLoaded(object sender, RoutedEventArgs e) => RefreshImage();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        Source = null;
    }

    private async void RefreshImage()
    {
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        Source = null;

        var loader = Loader;
        var path = IconPath;
        if (!IsLoaded || loader is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        try
        {
            var image = await loader.LoadAsync(path, cancellation.Token).ConfigureAwait(true);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_loadCancellation, cancellation))
            {
                Source = image;
            }
        }
        catch (OperationCanceledException)
        {
            // The item was recycled while the icon was being extracted.
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
            }

            cancellation.Dispose();
        }
    }
}
