using System.Windows;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Storage;
using Microsoft.Win32;

namespace Viciotek.Presentation.Dialogs;

public sealed class DialogService : IDialogService
{
    private readonly AppPaths _paths;
    private readonly SteamGridDbClient? _steamGrid;

    public DialogService(AppPaths paths, SteamGridDbClient? steamGrid = null)
    {
        _paths = paths;
        _steamGrid = steamGrid;
    }

    public string[]? PickGameFiles()
    {
        var dialog = new AddGamesDialog();
        return ShowDialog(dialog) == true ? dialog.Files : null;
    }

    public string? PickCoverFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir portada",
            Multiselect = false,
            CheckFileExists = true,
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.jfif|Todos los archivos|*.*"
        };

        return ShowOpenDialog(dialog) == true ? dialog.FileName : null;
    }

    public async Task PickCoverFromGalleryAsync(Game game, SteamGridDbClient steamGrid,
        Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback, CancellationToken cancellationToken)
    {
        var viewModel = new CoverGalleryViewModel(steamGrid, game, apply, manualFallback, cancellationToken);
        var dialog = new CoverGalleryDialog(viewModel);
        using var registration = cancellationToken.Register(() => dialog.Dispatcher.BeginInvoke(new Action(dialog.Close)));
        try { if (!cancellationToken.IsCancellationRequested) ShowDialog(dialog); }
        finally { await viewModel.CloseAsync(); }
    }

    public GameEditResult? EditGame(Game game)
    {
        var dialog = new GameEditDialog(game);
        return ShowDialog(dialog) == true ? dialog.Result : null;
    }

    public bool ConfirmRemove(string gameName)
    {
        var dialog = new ConfirmDialog(
            "Quitar juego",
            $"«{gameName}» se quitará de tu biblioteca.",
            "Quitar");
        return ShowDialog(dialog) == true;
    }

    public AppSettings? EditPreferences(AppSettings settings)
    {
        var dialog = new PreferencesDialog(settings, _paths, _steamGrid);
        return ShowDialog(dialog) == true ? dialog.Result : null;
    }

    private bool? ShowDialog(Window dialog)
    {
        var owner = GetOwner();
        if (owner is null)
        {
            return dialog.ShowDialog();
        }

        dialog.Owner = owner;
        return dialog.ShowDialog();
    }

    private bool? ShowOpenDialog(OpenFileDialog dialog)
    {
        var owner = GetOwner();
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private static Window? GetOwner()
    {
        var window = System.Windows.Application.Current?.MainWindow;
        return window is { IsVisible: true } ? window : null;
    }
}
