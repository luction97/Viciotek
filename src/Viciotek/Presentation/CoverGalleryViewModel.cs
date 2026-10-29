using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;

namespace Viciotek.Presentation;

public sealed record CoverSelection(byte[] Bytes, SteamGridGame Game, string SearchName);

public sealed class CoverCandidate(SteamGridDbClient.SteamGridGrid grid) : ObservableObject
{
    public string Url => grid.Url;
    public string? ThumbnailUrl => grid.ThumbnailUrl;
    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail { get => _thumbnail; set => SetProperty(ref _thumbnail, value); }
    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
}

public sealed class CoverGalleryViewModel : ObservableObject
{
    private readonly SteamGridDbClient _client;
    private readonly Func<CoverSelection, CancellationToken, Task<bool>> _apply;
    private readonly CancellationTokenSource _lifetime;
    private CancellationTokenSource? _previewCancellation;
    private readonly SemaphoreSlim _downloadGate = new(2, 2);
    private Task _active = Task.CompletedTask;
    private Task _previews = Task.CompletedTask;
    private string _searchQuery;
    private string _matchedQuery = string.Empty;
    private string? _status;
    private bool _isLoading;
    private bool _closed;
    private CoverCandidate? _selected;
    private readonly SteamGridGame? _confirmed;

    public CoverGalleryViewModel(SteamGridDbClient client, Game game,
        Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback = false,
        CancellationToken cancellationToken = default)
    {
        _client = client;
        _apply = apply;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _searchQuery = manualFallback ? string.Empty : game.CoverSearchName ?? game.Name;
        _confirmed = !manualFallback && game.SteamGridDbGameId is > 0
            ? new(game.SteamGridDbGameId.Value, game.SteamGridDbName ?? game.Name) : null;
        _status = manualFallback ? "No encontramos una portada automáticamente." : null;
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading && !string.IsNullOrWhiteSpace(SearchQuery));
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => !IsLoading && Selected is not null && SelectedGame is not null);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set { if (SetProperty(ref _searchQuery, value)) SearchCommand.RaiseCanExecuteChanged(); }
    }
    public string? Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            SearchCommand.RaiseCanExecuteChanged();
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }
    public CoverCandidate? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) ConfirmCommand.RaiseCanExecuteChanged(); }
    }
    public SteamGridGame? SelectedGame { get; private set; }
    public ObservableCollection<SteamGridGame> Matches { get; } = [];
    public ObservableCollection<CoverCandidate> Candidates { get; } = [];
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand ConfirmCommand { get; }
    public event Action? Completed;

    public Task InitializeAsync()
    {
        if (_confirmed is null) return string.IsNullOrWhiteSpace(SearchQuery) ? Task.CompletedTask : SearchAsync();
        return RunAsync(async () =>
        {
            _matchedQuery = SearchQuery;
            Matches.Add(_confirmed);
            await LoadGridsAsync(_confirmed);
        });
    }

    public Task SearchAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        await ClearPreviewsAsync();
        Matches.Clear();
        SelectedGame = null;
        var query = SearchQuery.Trim();
        var matches = await _client.SearchGamesAsync(query, _lifetime.Token);
        _matchedQuery = query;
        foreach (var match in matches) Matches.Add(match);
        var exact = GameIdentitySearch.ExactMatches(query, matches);
        if (exact.Count == 1) await LoadGridsAsync(exact[0]);
        else Status = matches.Count == 0 ? "Sin resultados. Probá con otro nombre." : "Elegí el videojuego para ver sus portadas.";
    });

    public Task SelectGameAsync(SteamGridGame game) => RunAsync(async () =>
    {
        if (!Matches.Contains(game)) return;
        await ClearPreviewsAsync();
        await LoadGridsAsync(game);
    });

    private async Task LoadGridsAsync(SteamGridGame game)
    {
        SelectedGame = game;
        var grids = await _client.GetGridsAsync(game.Id, _lifetime.Token);
        foreach (var grid in grids) Candidates.Add(new(grid));
        Status = grids.Count == 0 ? "Este juego no tiene portadas verticales. Probá con otro nombre." : null;
        _previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _previews = Task.WhenAll(Candidates.Select(candidate => LoadPreviewAsync(candidate, _previewCancellation.Token)));
    }

    private async Task LoadPreviewAsync(CoverCandidate candidate, CancellationToken token)
    {
        // Missing provider thumbnails never trigger downloads of all full-resolution originals.
        if (candidate.ThumbnailUrl is null) return;
        var acquired = false;
        try
        {
            candidate.IsLoading = true;
            await _downloadGate.WaitAsync(token);
            acquired = true;
            var bytes = await _client.DownloadImageAsync(candidate.ThumbnailUrl, token);
            candidate.Thumbnail = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 160;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is SteamGridDbException or IOException or NotSupportedException or ArgumentException or InvalidOperationException)
        { /* The candidate remains selectable even if its small preview failed. */ }
        finally
        {
            candidate.IsLoading = false;
            if (acquired) _downloadGate.Release();
        }
    }

    public Task ConfirmAsync() => RunAsync(async () =>
    {
        var selected = Selected;
        var game = SelectedGame;
        if (selected is null || game is null) return;
        var bytes = await _client.DownloadImageAsync(selected.Url, _lifetime.Token);
        if (await _apply(new(bytes, game, _matchedQuery), _lifetime.Token)) Completed?.Invoke();
        else Status = "No se pudo guardar la portada. Intentá nuevamente.";
    });

    private Task RunAsync(Func<Task> action)
    {
        if (_closed || IsLoading) return Task.CompletedTask;
        IsLoading = true;
        Status = null;
        return _active = ExecuteAsync();

        async Task ExecuteAsync()
        {
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (SteamGridDbException exception) { Status = exception.Message; }
            catch (Exception) { Status = "No se pudo obtener la portada. Intentá nuevamente."; }
            finally { IsLoading = false; }
        }
    }

    private async Task ClearPreviewsAsync()
    {
        _previewCancellation?.Cancel();
        await _previews;
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        Selected = null;
        Candidates.Clear();
    }

    public async Task CloseAsync()
    {
        if (_closed) return;
        _closed = true;
        _lifetime.Cancel();
        await _active;
        await ClearPreviewsAsync();
        _downloadGate.Dispose();
        _lifetime.Dispose();
    }
}
