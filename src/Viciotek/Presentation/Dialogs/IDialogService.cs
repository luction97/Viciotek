using Viciotek.Domain;
using Viciotek.Application;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Library;

namespace Viciotek.Presentation.Dialogs;
public enum CoverSource { Local, SteamGridDb, Automatic }

public sealed record GameEditResult(
    string Name,
    string Target,
    LaunchKind LaunchKind,
    string? Arguments,
    string? WorkingDirectory);

public interface IDialogService
{
    string[]? PickGameFiles();
    string[]? PickGameFiles(bool directly) => PickGameFiles();
    string? PickCoverFile();
    CoverSource? ChooseCoverSource(bool connected, bool canChooseAutomatically) => CoverSource.Local;
    Task PickCoverFromGalleryAsync(Game game, SteamGridDbClient steamGrid,
        Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback, CancellationToken cancellationToken);
    GameEditResult? EditGame(Game game);
    bool ConfirmRemove(string gameName);
    bool ConfirmRemoveMany(int count) => ConfirmRemove(L.Get("SelectedGameCount", count));
    bool ConfirmUndoImport(int count) => ConfirmRemoveMany(count);
    IReadOnlyList<DetectedGame>? ReviewImport(IReadOnlyList<DetectedGame> games) => null;
    AppSettings? EditPreferences(AppSettings settings);
    AppSettings? ConnectCovers(AppSettings settings) => EditPreferences(settings);
    AppSettings? EditPreferences(AppSettings settings, Action<int> previewCoverSize) => EditPreferences(settings);
}
