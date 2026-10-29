using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Library;

namespace Viciotek.Presentation.Dialogs;

public sealed record GameEditResult(
    string Name,
    string Target,
    LaunchKind LaunchKind,
    string? Arguments,
    string? WorkingDirectory);

public interface IDialogService
{
    string[]? PickGameFiles();
    string? PickCoverFile();
    Task PickCoverFromGalleryAsync(Game game, SteamGridDbClient steamGrid,
        Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback, CancellationToken cancellationToken);
    GameEditResult? EditGame(Game game);
    bool ConfirmRemove(string gameName);
    AppSettings? EditPreferences(AppSettings settings);
}
