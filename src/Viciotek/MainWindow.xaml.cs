using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Shell;
using Viciotek.Presentation;
using Viciotek.Presentation.Controls;

namespace Viciotek;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".jfif"
    };

    private readonly MainViewModel _viewModel;
    private WindowState _lastVisibleState = WindowState.Normal;
    private bool _geometrySaved;
    private bool _closeInProgress;

    public MainWindow(
        MainViewModel viewModel,
        CoverImageLoader coverLoader,
        IconService iconLoader,
        AppSettings settings)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        Gallery.CoverLoader = coverLoader;
        Gallery.IconLoader = iconLoader;

        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
            _lastVisibleState = WindowState.Maximized;
        }
        else
        {
            _lastVisibleState = WindowState.Normal;
        }

        SourceInitialized += (_, _) =>
        {
            WindowAppearance.Apply(this);
            WindowAppearance.RestrictMaximizedToWorkArea(this);
        };
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;

        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.ScrollToTopRequested += OnScrollToTopRequested;
        _viewModel.ScrollToItemRequested += OnScrollToItemRequested;

        UpdateCaptionButtons();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowPendingNotice();
        SearchBox.Focus();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            _lastVisibleState = WindowState;
        }

        UpdateCaptionButtons();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_geometrySaved)
        {
            return;
        }

        e.Cancel = true;
        if (_closeInProgress) return;
        _closeInProgress = true;
        IsEnabled = false;
        var maximized = _lastVisibleState == WindowState.Maximized;
        var width = ActualWidth;
        var height = ActualHeight;

        if (maximized)
        {
            var restore = RestoreBounds;
            if (restore.Width > 0 && restore.Height > 0)
            {
                width = restore.Width;
                height = restore.Height;
            }
        }

        await _viewModel.PrepareCloseAsync();
        await Dispatcher.Yield(DispatcherPriority.Background);
        _viewModel.SaveAllNow(width, height, maximized);
        _geometrySaved = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.ScrollToTopRequested -= OnScrollToTopRequested;
        _viewModel.ScrollToItemRequested -= OnScrollToItemRequested;
        _viewModel.Dispose();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnScrollToTopRequested(object? sender, EventArgs e) =>
        Gallery.ScrollToTop();

    private void OnScrollToItemRequested(GameCardViewModel card)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                Gallery.ScrollToItem(card);
                Gallery.SelectedItem = card;
            });
    }

    private void UpdateCaptionButtons()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Gallery_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel.SelectedGame = Gallery.SelectedItem as GameCardViewModel;

    private void Gallery_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
    }

    private void Gallery_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;

        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not GameCardViewModel card || !card.PlayCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        card.PlayCommand.Execute(null);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down)
        {
            return;
        }

        if (FocusFirstResult())
        {
            e.Handled = true;
        }
    }

    private bool FocusFirstResult()
    {
        if (_viewModel.GamesView.IsEmpty)
        {
            return false;
        }

        var first = _viewModel.GamesView.Cast<object>().FirstOrDefault();
        if (first is not GameCardViewModel card)
        {
            return false;
        }

        Gallery.SelectedItem = card;
        Gallery.ScrollToItem(card);
        Gallery.Focus();
        return true;
    }

    protected override async void OnPreviewKeyDown(KeyEventArgs e)
    {
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var focusedButton = Keyboard.FocusedElement is ButtonBase;

        if (control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.O)
        {
            if (_viewModel.AddGamesCommand.CanExecute(null))
            {
                _viewModel.AddGamesCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.OemComma)
        {
            if (_viewModel.OpenPreferencesCommand.CanExecute(null))
            {
                _viewModel.OpenPreferencesCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && Gallery.SelectedItem is GameCardViewModel selectedCard)
        {
            if (selectedCard.EditCommand.CanExecute(null))
            {
                selectedCard.EditCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && !focusedButton)
        {
            if (SearchBox.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                await _viewModel.LaunchSearchResultAsync();
                return;
            }

            if (Gallery.SelectedItem is GameCardViewModel card && card.PlayCommand.CanExecute(null))
            {
                card.PlayCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(_viewModel.SearchQuery))
            {
                _viewModel.SearchQuery = string.Empty;
                SearchBox.Focus();
                e.Handled = true;
            }
            else if (Gallery.SelectedItem is not null)
            {
                Gallery.SelectedItem = null;
                e.Handled = true;
            }
            else
            {
                Keyboard.ClearFocus();
            }
        }

        base.OnPreviewKeyDown(e);
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!_viewModel.IsDropEnabled || !TryGetDroppedPaths(e.Data, out var paths))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            HideDropUi();
            return;
        }

        e.Handled = true;

        var imagesOnly = paths.All(IsImagePath);
        var card = FindCardAt(e.GetPosition(Gallery));

        if (imagesOnly)
        {
            e.Effects = card is not null && paths.Count == 1 && !card.IsBusy
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            HideDropOverlay();
            _viewModel.SetCoverDropTarget(e.Effects == DragDropEffects.Copy ? card : null);
            return;
        }

        _viewModel.SetCoverDropTarget(null);
        var hasGames = paths.Any(path => Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".exe" or ".url");
        e.Effects = hasGames ? DragDropEffects.Copy : DragDropEffects.None;
        if (hasGames)
        {
            ShowDropOverlay();
        }
        else
        {
            HideDropOverlay();
        }
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!_viewModel.IsDropEnabled || !TryGetDroppedPaths(e.Data, out var paths))
        {
            HideDropUi();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var imagesOnly = paths.All(IsImagePath);
        var card = FindCardAt(e.GetPosition(Gallery));
        HideDropUi();
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        if (imagesOnly && card is not null)
        {
            if (paths.Count != 1 || card.IsBusy)
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            await _viewModel.ReplaceCoverFromDropAsync(card, paths[0]);
            return;
        }

        await _viewModel.HandleDroppedFilesAsync(paths);
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(this);
        if (position.X < 0 || position.Y < 0 ||
            position.X > ActualWidth || position.Y > ActualHeight)
        {
            HideDropUi();
        }
    }

    private void ShowDropOverlay() => DropOverlay.Visibility = Visibility.Visible;

    private void HideDropOverlay() => DropOverlay.Visibility = Visibility.Collapsed;

    private void HideDropUi()
    {
        HideDropOverlay();
        _viewModel.SetCoverDropTarget(null);
    }

    private GameCardViewModel? FindCardAt(Point positionInGallery)
    {
        if (!Gallery.IsVisible || Gallery.ActualWidth <= 0 || Gallery.ActualHeight <= 0)
        {
            return null;
        }

        if (positionInGallery.X < 0 || positionInGallery.Y < 0 ||
            positionInGallery.X > Gallery.ActualWidth || positionInGallery.Y > Gallery.ActualHeight)
        {
            return null;
        }

        var hit = Gallery.InputHitTest(positionInGallery) as DependencyObject;
        return FindAncestor<ListBoxItem>(hit)?.DataContext as GameCardViewModel;
    }

    private static bool TryGetDroppedPaths(IDataObject data, out IReadOnlyList<string> paths)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            paths = files;
            return true;
        }

        paths = Array.Empty<string>();
        return false;
    }

    private static bool IsImagePath(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
