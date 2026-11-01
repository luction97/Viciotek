using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Viciotek.Domain;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Input;
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
    private bool _welcomeEntranceHandled;
    private bool _welcomeEntrancePending;
    private readonly DispatcherTimer _gamepadTimer;

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
        ContentRendered += OnFirstContentRendered;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;

        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.ScrollToTopRequested += OnScrollToTopRequested;
        _viewModel.ScrollToItemRequested += OnScrollToItemRequested;

        UpdateCaptionButtons();

        _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _gamepadTimer.Tick += OnGamepadTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_welcomeEntranceHandled)
        {
            _welcomeEntranceHandled = true;
            _welcomeEntrancePending = _viewModel.ShowEmptyState && _viewModel.AnimationsEnabled && SystemParameters.ClientAreaAnimation;
            if (_welcomeEntrancePending) WelcomeContent.Opacity = 0;
        }
        _viewModel.ShowPendingNotice();
        // Keep search opt-in: opening the library must not show a caret or select text.
        Keyboard.Focus(this);
        _gamepadTimer.Start();
        // Inicializar DirectInput con el HWND de la ventana para joysticks genéricos.
        var hwnd = new WindowInteropHelper(this).Handle;
        DirectInputJoystick.Initialize(hwnd);
        // Detectar joystick al iniciar: si hay dispositivo conectado, seleccionar el primer juego.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var hasJoystick = false;
            for (var i = 0; i < 4; i++)
            {
                if (XInputGamepad.GetState(i).Connected != 0) { hasJoystick = true; break; }
            }
            if (!hasJoystick)
            {
                hasJoystick = WinMMJoystick.GetState().Connected != 0;
            }
            if (!hasJoystick)
            {
                hasJoystick = DirectInputJoystick.GetState().Connected != 0;
            }
            if (hasJoystick && Gallery.SelectedItem is null && _viewModel.GamesView.Cast<GameCardViewModel>().Any())
            {
                var first = _viewModel.GamesView.Cast<GameCardViewModel>().First();
                Gallery.ScrollToItem(first);
                Gallery.SelectedItem = first;
            }
        });
    }

    private void OnFirstContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnFirstContentRendered;
        if (!_welcomeEntrancePending) return;
        _welcomeEntrancePending = false;
        WelcomeContent.Opacity = 1;
        if (_viewModel.ShowEmptyState)
            BeginWelcomeEntrance(WelcomeContent, _viewModel.AnimationsEnabled && SystemParameters.ClientAreaAnimation);
    }

    internal static void BeginWelcomeEntrance(FrameworkElement content, bool enabled)
    {
        if (!enabled) return;
        var offset = new TranslateTransform();
        content.RenderTransform = offset;
        var duration = TimeSpan.FromMilliseconds(420);
        var fade = new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => ClearWelcomeEntrance(content);
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
        content.BeginAnimation(OpacityProperty, fade);
    }

    private static void ClearWelcomeEntrance(FrameworkElement content)
    {
        content.BeginAnimation(OpacityProperty, null);
        if (content.RenderTransform is TranslateTransform offset)
            offset.BeginAnimation(TranslateTransform.YProperty, null);
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
        ClearWelcomeEntrance(WelcomeContent);
        ContentRendered -= OnFirstContentRendered;
        _gamepadTimer.Stop();
        DirectInputJoystick.Shutdown();
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

    private void OnGamepadTick(object? sender, EventArgs e)
    {
        XInputGamepadState state = default;
        var xinputFound = false;
        for (var i = 0; i < 4; i++)
        {
            var s = XInputGamepad.GetState(i);
            if (s.Connected != 0)
            {
                state = s;
                xinputFound = true;
                break;
            }
        }

        if (!xinputFound)
        {
            var winmm = WinMMJoystick.GetState();
            if (winmm.Connected == 0)
            {
                var di = DirectInputJoystick.GetState();
                if (di.Connected == 0)
                {
                    return;
                }
                // DirectInput: normalize axes. DirectInput axes are typically -1000..1000 or 0..65535.
                var diX = NormalizeDirectInputAxis(di.AxisX);
                var diY = NormalizeDirectInputAxis(di.AxisY);
                var diDirection = GamepadInput.ResolveMoveDirection(diX, diY, 0);
                if (diDirection != GamepadMoveDirection.None)
                {
                    MoveGameSelection(diDirection);
                    return;
                }
                // Launch button: first button
                if (di.Buttons != null && di.Buttons.Length > 0 && (di.Buttons[0] & 0x80) != 0)
                {
                    LaunchSelectedGame();
                }
                return;
            }

            var direction = GamepadInput.ResolveMoveDirectionWinMM(winmm.X, winmm.Y, winmm.Pov);
            if (direction != GamepadMoveDirection.None)
            {
                MoveGameSelection(direction);
                return;
            }

            if ((winmm.Buttons & WinMMJoystick.LaunchButton) == WinMMJoystick.LaunchButton)
            {
                LaunchSelectedGame();
            }
            return;
        }

        var xdirection = GamepadInput.ResolveMoveDirection(state.AxisLeftX, state.AxisLeftY, state.Buttons);
        if (xdirection != GamepadMoveDirection.None)
        {
            MoveGameSelection(xdirection);
            return;
        }

        if ((state.Buttons & GamepadInput.LaunchButton) == GamepadInput.LaunchButton)
        {
            LaunchSelectedGame();
        }
    }

    private void MoveGameSelection(GamepadMoveDirection direction)
    {
        if (direction == GamepadMoveDirection.None)
        {
            return;
        }

        var cards = _viewModel.GamesView.Cast<GameCardViewModel>().ToList();
        if (cards.Count == 0)
        {
            return;
        }

        var selected = Gallery.SelectedItem as GameCardViewModel;
        var index = cards.FindIndex(card => ReferenceEquals(card, selected));
        if (index < 0)
        {
            // No hay selección: el primer movimiento del joystick selecciona el primer juego.
            var first = cards[0];
            Gallery.ScrollToItem(first);
            Gallery.SelectedItem = first;
            return;
        }

        var step = direction is GamepadMoveDirection.Down or GamepadMoveDirection.Right ? 1 : -1;
        var targetIndex = index + step;
        if (targetIndex < 0 || targetIndex >= cards.Count)
        {
            return;
        }

        var target = cards[targetIndex];
        Gallery.ScrollToItem(target);
        Gallery.SelectedItem = target;
    }

    private static short NormalizeDirectInputAxis(int value)
    {
        // DirectInput axes can be -1000..1000 or 0..65535.
        if (value >= 0 && value <= 65535)
        {
            return (short)(value - 32768);
        }
        // Scale -1000..1000 to -32768..32767
        return (short)(value * 32);
    }

    private void AddGamesMenu_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        AddGamesPopup.PlacementTarget = button;
        AddGamesPopup.IsOpen = true;
    }

    private void LaunchSelectedGame()
    {
        if (_viewModel.SelectedGame is { } card && card.PlayCommand.CanExecute(null))
        {
            card.PlayCommand.Execute(null);
        }
    }

    private void UpdateCaptionButtons()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.SetBinding(ToolTipProperty, new System.Windows.Data.Binding { Source = L.Current,
            Path = new PropertyPath("[" + (maximized ? "Restore" : "Maximize") + "]") });
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
        // Explicit actions use the latest text, including a pending delayed edit.
        if (SearchBox.IsKeyboardFocusWithin && e.Key is Key.Enter or Key.Escape or Key.Down)
            SearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
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
