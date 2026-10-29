using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Viciotek.Infrastructure.Imaging;

namespace Viciotek.Presentation.Controls;

public sealed class CoverImage : Image
{
    public static readonly DependencyProperty CoverPathProperty = DependencyProperty.Register(
        nameof(CoverPath),
        typeof(string),
        typeof(CoverImage),
        new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty LoaderProperty = DependencyProperty.Register(
        nameof(Loader),
        typeof(CoverImageLoader),
        typeof(CoverImage),
        new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty CoverVersionProperty = DependencyProperty.Register(
        nameof(CoverVersion),
        typeof(string),
        typeof(CoverImage),
        new PropertyMetadata(null, OnSourceChanged));

    private CancellationTokenSource? _loadCancellation;
    private DispatcherOperation? _refreshOperation;
    private TaskCompletionSource? _loadingCompletion;
    internal Task LoadingTask => _loadingCompletion?.Task ?? Task.CompletedTask;

    public CoverImage()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? CoverPath
    {
        get => (string?)GetValue(CoverPathProperty);
        set => SetValue(CoverPathProperty, value);
    }

    public CoverImageLoader? Loader
    {
        get => (CoverImageLoader?)GetValue(LoaderProperty);
        set => SetValue(LoaderProperty, value);
    }

    public string? CoverVersion
    {
        get => (string?)GetValue(CoverVersionProperty);
        set => SetValue(CoverVersionProperty, value);
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((CoverImage)dependencyObject).ScheduleRefresh();

    private void OnLoaded(object sender, RoutedEventArgs e) => ScheduleRefresh();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        _refreshOperation?.Abort();
        _refreshOperation = null;
        _loadingCompletion?.TrySetResult();
        Source = null;
    }

    private void ScheduleRefresh()
    {
        if (!IsLoaded || _refreshOperation is not null)
        {
            return;
        }

        // Loader/path/version bindings often change in the same layout pass.
        // Coalesce them so an obsolete revision is never decoded needlessly.
        _loadingCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _refreshOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _refreshOperation = null;
            RefreshImage();
        }));
    }

    private async void RefreshImage()
    {
        var completion = _loadingCompletion;
        _loadCancellation?.Cancel();
        _loadCancellation = null;

        var loader = Loader;
        var path = CoverPath;
        if (!IsLoaded || loader is null || string.IsNullOrWhiteSpace(path))
        {
            Source = null;
            completion?.TrySetResult();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        try
        {
            var image = await loader.LoadAsync(path, CoverVersion, cancellation.Token).ConfigureAwait(true);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_loadCancellation, cancellation))
            {
                Source = image;
            }
        }
        catch (OperationCanceledException)
        {
            // The item was recycled while the image was being decoded.
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
            }

            cancellation.Dispose();
            completion?.TrySetResult();
        }
    }
}
