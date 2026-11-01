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

    public string[]? PickGameFiles(bool directly)
    {
        if (!directly) return PickGameFiles();
        var picker = AddGamesDialog.CreateFilePicker();
        return ShowOpenDialog(picker) == true ? picker.FileNames : null;
    }

    public string? PickCoverFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = L.Get("ChooseCover"),
            Multiselect = false,
            CheckFileExists = true,
            Filter = L.Get("ImagesPngJpgJpegWebpBmpGifTifTiffJfifAllFiles")
        };

        return ShowOpenDialog(dialog) == true ? dialog.FileName : null;
    }

    public CoverSource? ChooseCoverSource(bool connected, bool canChooseAutomatically)
    {
        var dialog = new CoverSourceDialog(connected, canChooseAutomatically);
        return ShowDialog(dialog) == true ? dialog.Result : null;
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
            L.Get("RemoveGame"),
            L.Get("0WillBeRemovedFromYourLibrary", gameName),
            L.Get("Remove2"));
        return ShowDialog(dialog) == true;
    }

    public IReadOnlyList<Viciotek.Application.DetectedGame>? ReviewImport(IReadOnlyList<Viciotek.Application.DetectedGame> games)
    {
        var dialog = new DetectInstalledGamesDialog(games);
        return ShowDialog(dialog) == true ? dialog.Selected : null;
    }
    public bool ConfirmRemoveMany(int count) => ShowDialog(new ConfirmDialog(L.Get("RemoveFromLibrary"),
        L.Get("ConfirmRemoveGames", count), L.Get("RemoveFromLibrary"))) == true;
    public bool ConfirmUndoImport(int count) => ShowDialog(new ConfirmDialog(L.Get("UndoLastImport"),
        L.Get("ConfirmUndoImport", count), L.Get("UndoLastImport"))) == true;

    public AppSettings? EditPreferences(AppSettings settings)
        => EditPreferences(settings, _ => { });

    public AppSettings? EditPreferences(AppSettings settings, Action<int> previewCoverSize)
    {
        var dialog = new PreferencesDialog(settings, _paths, _steamGrid);
        dialog.CoverSizePreview += previewCoverSize;
        return ShowDialog(dialog) == true ? dialog.Result : null;
    }

    public AppSettings? ConnectCovers(AppSettings settings)
    {
        var dialog = new PreferencesDialog(settings, _paths, _steamGrid, connectionOnly: true);
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
