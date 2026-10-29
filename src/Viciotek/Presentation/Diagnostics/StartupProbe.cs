using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Viciotek.Presentation.Controls;

namespace Viciotek.Presentation.Diagnostics;

/// <summary>
/// Opt-in, event-driven developer measurement. No timers, files or frame
/// subscriptions are created in normal use. Rendering means WPF frame-ready,
/// not proof of physical GPU presentation on the monitor.
/// </summary>
internal sealed class StartupProbe
{
    private readonly MainWindow _window;
    private readonly GameGallery _gallery;
    private readonly string _output;
    private readonly int _scrollPasses;
    private readonly bool _exitWhenDone;
    private readonly CancellationTokenSource _closed = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double _processAgeMs;
    private bool _started;

    private StartupProbe(MainWindow window, string output)
    {
        _window = window;
        _gallery = (GameGallery)window.FindName("Gallery");
        _output = Path.GetFullPath(output);
        _scrollPasses = int.TryParse(Environment.GetEnvironmentVariable("VICIOTEK_METRICS_SCROLL_PASSES"), out var passes)
            ? Math.Clamp(passes, 0, 20) : 0;
        _exitWhenDone = Environment.GetEnvironmentVariable("VICIOTEK_METRICS_EXIT") == "1";
        using var process = Process.GetCurrentProcess();
        _processAgeMs = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds - _clock.Elapsed.TotalMilliseconds;
        window.ContentRendered += OnContentRendered;
        window.Closed += OnClosed;
    }

    public static void AttachIfRequested(MainWindow window)
    {
        var output = Environment.GetEnvironmentVariable("VICIOTEK_METRICS_FILE");
        if (!string.IsNullOrWhiteSpace(output)) _ = new StartupProbe(window, output);
    }

    private double ElapsedMs => Math.Round(_processAgeMs + _clock.Elapsed.TotalMilliseconds, 1);

    private async void OnContentRendered(object? sender, EventArgs args)
    {
        if (_started) return;
        _started = true;
        _window.ContentRendered -= OnContentRendered;
        try
        {
            var firstContent = ElapsedMs;
            var initialImages = await WaitForVisibleCoversAsync(initial: true);
            var coversReady = ElapsedMs;
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var initialPrivate = process.PrivateMemorySize64 / 1048576d;
            var initialWorkingSet = process.WorkingSet64 / 1048576d;
            var panel = Descendants(_gallery).OfType<VirtualizingWrapPanel>().FirstOrDefault();
            var maxRealized = panel?.RealizedItemCount ?? 0;
            var stepDurations = new List<double>();
            var failedLoads = initialImages.Count(image => image.Source is null);

            for (var pass = 0; pass < _scrollPasses && panel is not null; pass++)
            {
                var destination = pass % 2 == 0 ? Math.Max(0, panel.ExtentHeight - panel.ViewportHeight) : 0;
                var step = Math.Max(1, panel.ViewportHeight * 0.9);
                while (Math.Abs(panel.VerticalOffset - destination) > 0.1)
                {
                    _closed.Token.ThrowIfCancellationRequested();
                    var next = destination > panel.VerticalOffset
                        ? Math.Min(destination, panel.VerticalOffset + step)
                        : Math.Max(destination, panel.VerticalOffset - step);
                    var timer = Stopwatch.StartNew();
                    panel.SetVerticalOffset(next);
                    var images = await WaitForVisibleCoversAsync();
                    stepDurations.Add(Math.Round(timer.Elapsed.TotalMilliseconds, 1));
                    maxRealized = Math.Max(maxRealized, panel.RealizedItemCount);
                    failedLoads += images.Count(image => image.Source is null);
                }
            }
            process.Refresh();
            var sorted = stepDurations.Order().ToArray();
            var report = new
            {
                Version = typeof(StartupProbe).Assembly.GetName().Version?.ToString(3),
                ProcessId = Environment.ProcessId,
                MeasuredAt = DateTimeOffset.Now,
                Method = "Process creation to WPF frame-ready after visible cover decodes; not GPU presentation. Scroll steps include decode/layout/frame wait.",
                FirstContentRenderedMs = firstContent,
                VisibleCoversReadyMs = coversReady,
                VisibleCoverCount = initialImages.Count,
                FailedVisibleCoverLoads = failedLoads,
                LibraryCount = (_window.DataContext as MainViewModel)?.Games.Count ?? 0,
                MaxRealizedContainers = maxRealized,
                ScrollPasses = _scrollPasses,
                ScrollSteps = sorted.Length,
                ScrollStepReadyP95Ms = sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1],
                ScrollStepDurationsMs = stepDurations,
                InitialPrivateMiB = Math.Round(initialPrivate, 1),
                InitialWorkingSetMiB = Math.Round(initialWorkingSet, 1),
                AfterScrollPrivateMiB = Math.Round(process.PrivateMemorySize64 / 1048576d, 1),
                AfterScrollWorkingSetMiB = Math.Round(process.WorkingSet64 / 1048576d, 1)
            };
            await WriteAsync(report);
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            try { await WriteAsync(new { Error = exception.ToString() }); }
            catch (Exception) { /* Diagnostic output must never crash the app. */ }
        }
        finally
        {
            if (_exitWhenDone && !_closed.IsCancellationRequested) _window.Close();
        }
    }

    private async Task<List<CoverImage>> WaitForVisibleCoversAsync(bool initial = false)
    {
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        var viewport = new Rect(_gallery.RenderSize);
        var images = Descendants(_gallery).OfType<CoverImage>()
            .Where(image => image.IsVisible && !string.IsNullOrWhiteSpace(image.CoverPath) &&
                image.TransformToAncestor(_gallery).TransformBounds(new Rect(image.RenderSize)).IntersectsWith(viewport))
            .ToList();
        await Task.WhenAll(images.Select(image => image.LoadingTask)).WaitAsync(_closed.Token);
        _window.UpdateLayout();
        if (!initial || images.Count > 0) await NextFrameAsync();
        return images;
    }

    private async Task NextFrameAsync()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendering(object? sender, EventArgs args) => ready.TrySetResult();
        CompositionTarget.Rendering += OnRendering;
        try
        {
            _window.InvalidateVisual();
            await ready.Task.WaitAsync(_closed.Token);
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private Task WriteAsync<T>(T report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        return File.WriteAllTextAsync(_output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _window.ContentRendered -= OnContentRendered;
        _window.Closed -= OnClosed;
        _closed.Cancel();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
