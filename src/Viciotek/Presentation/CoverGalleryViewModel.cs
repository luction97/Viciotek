using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;

namespace Viciotek.Presentation;

public sealed record CoverSelection(byte[] Bytes, SteamGridGame Game, string SearchName);

public sealed class CoverCandidate(SteamGridDbClient.SteamGridGrid grid, SteamGridGame? game = null) : ObservableObject
{
    public SteamGridGame? Game => game;
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
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _searchCancellation;
    private Task _scheduled = Task.CompletedTask;
    private int _searchRevision;
    private Task _active = Task.CompletedTask;
    private Task _previews = Task.CompletedTask;
    private string _searchQuery;
    private string _matchedQuery = string.Empty;
    private string? _status;
    private bool _isLoading;
    private bool _closed;
    private CoverCandidate? _selected;
    private readonly SteamGridGame? _confirmed;
    private sealed class ResultSource(SteamGridGame game) { public SteamGridGame Game = game; public int Page; public bool Finished; }
    private readonly List<ResultSource> _sources = [];
    private readonly HashSet<(int Game, string Url)> _seen = [];
    private readonly Dictionary<CoverCandidate, Task> _previewRequests = [];
    private readonly LinkedList<CoverCandidate> _previewCache = [];
    private readonly HashSet<CoverCandidate> _visiblePreviews = [];
    private int _sourceIndex;
    public bool HasMoreResults => _sources.Any(source => !source.Finished);
    public AsyncRelayCommand LoadMoreCommand { get; }

    public CoverGalleryViewModel(SteamGridDbClient client, Game game,
        Func<CoverSelection, CancellationToken, Task<bool>> apply, bool manualFallback = false,
        CancellationToken cancellationToken = default)
    {
        _client = client;
        _apply = apply;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var title = game.CoverSearchName ?? game.Name;
        _searchQuery = manualFallback && (!GameTitle.IsDescriptive(title) || GameTitle.IsAbbreviation(title))
            ? string.Empty : title;
        _confirmed = !manualFallback && game.SteamGridDbGameId is > 0
            ? new(game.SteamGridDbGameId.Value, game.SteamGridDbName ?? game.Name) : null;
        _status = manualFallback ? L.Get("NoCoverWasFoundAutomatically") : null;
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => !IsLoading && HasMoreResults);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading && !string.IsNullOrWhiteSpace(SearchQuery));
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => !IsLoading && Selected is not null && (Selected.Game is not null || SelectedGame is not null));
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (IsApplying) return;
            if (!SetProperty(ref _searchQuery, value ?? string.Empty) || _closed) return;
            SearchCommand.RaiseCanExecuteChanged();
            _searchRevision++;
            _searchCancellation?.Cancel();
            _previewCancellation?.Cancel();
            CancelDebounce();
            _suggestionsRequested = true;
            _sources.Clear(); UpdatePaging();
            Matches.Clear();
            OnPropertyChanged(nameof(ShowSuggestions));
            Candidates.Clear();
            Selected = null;
            SelectedGame = null;
            Status = string.IsNullOrWhiteSpace(SearchQuery) ? null : L.Get("Searching");
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;
            _debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _scheduled = SearchAfterPauseAsync(_debounce.Token);
        }
    }
    private bool _suggestionsRequested;
    public bool ShowSuggestions => _suggestionsRequested && Matches.Count > 0;
    public void HideSuggestions() { _suggestionsRequested = false; OnPropertyChanged(nameof(ShowSuggestions)); }
    private void AcceptGame(SteamGridGame game)
    {
        HideSuggestions();
        SetProperty(ref _searchQuery, game.Name, nameof(SearchQuery));
        SearchCommand.RaiseCanExecuteChanged();
    }
    public string? Status { get => _status; private set => SetProperty(ref _status, value); }
    private bool _isApplying;
    public bool IsApplying { get => _isApplying; private set => SetProperty(ref _isApplying, value); }
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            SearchCommand.RaiseCanExecuteChanged();
            ConfirmCommand.RaiseCanExecuteChanged();
            LoadMoreCommand.RaiseCanExecuteChanged();
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
        _suggestionsRequested = _confirmed is null;
        if (_confirmed is null) return string.IsNullOrWhiteSpace(SearchQuery) ? Task.CompletedTask : SearchAsync();
        return RunAsync(async () =>
        {
            _matchedQuery = SearchQuery;
            Matches.Add(_confirmed);
            await LoadGridsAsync(_confirmed);
        });
    }

    private void CancelDebounce()
    {
        var pending = _debounce;
        _debounce = null;
        if (pending is null) return;
        pending.Cancel();
        pending.Dispose();
    }

    private async Task SearchAfterPauseAsync(CancellationToken token)
    {
        try { await Task.Delay(350, token); token.ThrowIfCancellationRequested(); await SearchAsync(); }
        catch (OperationCanceledException) { }
    }

    public Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        return RunLatestAsync(async (revision, token) =>
        {
            await ClearPreviewsAsync();
            if (token.IsCancellationRequested || revision != _searchRevision) return;
            _sources.Clear(); UpdatePaging();
            Matches.Clear();
            SelectedGame = null;
            if (query.Length == 0) return;
            var matches = await _client.SearchGamesAsync(query, token);
            if (token.IsCancellationRequested || revision != _searchRevision) return;
            _matchedQuery = query;
            foreach (var match in matches) Matches.Add(match);
            OnPropertyChanged(nameof(ShowSuggestions));
            SetSources(matches.GroupBy(game => GameTitle.Normalize(game.Name))
                .SelectMany(group => group.OrderByDescending(game => game.ReleaseYear)));
            if (matches.Count > 0) await LoadNextPageAsync(revision, token);
            else Status = L.Get("NoResultsTryAnotherName");
        });
    }

    public Task SelectGameAsync(SteamGridGame game)
    {
        if (_closed || IsApplying || !Matches.Contains(game)) return Task.CompletedTask;
        AcceptGame(game);
        return RunLatestAsync(async (revision, token) =>
        {
            await ClearPreviewsAsync();
            await LoadGridsAsync(game, revision, token);
        });
    }

    private Task RunLatestAsync(Func<int, CancellationToken, Task> action)
    {
        if (_closed || IsApplying) return Task.CompletedTask;
        CancelDebounce();
        _searchCancellation?.Cancel();
        var revision = ++_searchRevision;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchCancellation = cancellation;
        var previous = _active;
        return _active = ExecuteAsync();

        async Task ExecuteAsync()
        {
            try
            {
                await previous;
                if (cancellation.IsCancellationRequested || revision != _searchRevision) return;
                IsLoading = true;
                Status = L.Get("Searching");
                await action(revision, cancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (SteamGridDbException exception) { if (revision == _searchRevision) Status = exception.Message; }
            catch (Exception) { if (revision == _searchRevision) Status = L.Get("CouldNotSearchPleaseTryAgain"); }
            finally
            {
                if (ReferenceEquals(_searchCancellation, cancellation)) { _searchCancellation = null; IsLoading = false; }
                cancellation.Dispose();
            }
        }
    }
    private void SetSources(IEnumerable<SteamGridGame> games)
    {
        _sources.Clear(); _sources.AddRange(games.Select(game => new ResultSource(game)));
        _sourceIndex = 0; _seen.Clear(); UpdatePaging();
    }
    private void UpdatePaging() { OnPropertyChanged(nameof(HasMoreResults)); LoadMoreCommand.RaiseCanExecuteChanged(); }
    public Task LoadMoreAsync() => IsLoading || !HasMoreResults ? Task.CompletedTask : RunLatestAsync(LoadNextPageAsync);

    private async Task LoadGridsAsync(SteamGridGame game, int? revision = null, CancellationToken? requestToken = null)
    {
        SetSources([game]);
        await LoadNextPageAsync(revision ?? _searchRevision, requestToken ?? _searchCancellation?.Token ?? _lifetime.Token);
    }

    private async Task LoadNextPageAsync(int revision, CancellationToken token)
    {
        var sources = _sources.ToArray();
        if (sources.Length == 0) return;
        var index = _sourceIndex;
        for (var i = 0; i < sources.Length && sources[index % sources.Length].Finished; i++) index++;
        var source = sources[index % sources.Length];
        if (source.Finished) return;
        var page = await _client.GetGridPageAsync(source.Game.Id, source.Page, token);
        if (_closed || token.IsCancellationRequested || revision != _searchRevision) return;
        var added = new List<CoverCandidate>();
        foreach (var grid in page.Grids)
            if (_seen.Add((source.Game.Id, grid.Url))) { var candidate = new CoverCandidate(grid, source.Game); Candidates.Add(candidate); added.Add(candidate); }
        source.Page++;
        source.Finished = !page.HasMore || (page.Grids.Count > 0 && added.Count == 0);
        _sourceIndex = index + 1;
        SelectedGame = sources.Length == 1 ? source.Game : Candidates.Select(candidate => candidate.Game!).DistinctBy(game => game.Id).Take(2).Count() == 1 ? Candidates[0].Game : null;
        Status = Candidates.Count == 0 ? HasMoreResults ? null : L.Get("ThisGameHasNoPortraitCoversTryAnotherName")
            : sources.Length == 1 ? L.Get("CoversForGame", source.Game.DisplayName) : L.Get("SearchCoverResults", _matchedQuery);
        UpdatePaging();
        foreach (var candidate in added.Take(24)) _ = EnsurePreviewAsync(candidate);
    }

    public Task EnsurePreviewAsync(CoverCandidate candidate)
    {
        if (_closed || !Candidates.Contains(candidate) || candidate.ThumbnailUrl is null) return Task.CompletedTask;
        if (candidate.Thumbnail is not null) { TouchPreview(candidate); return Task.CompletedTask; }
        if (_previewRequests.TryGetValue(candidate, out var pending) && !pending.IsCompleted) return pending;
        _previewCancellation ??= CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var task = LoadPreviewAsync(candidate, _previewCancellation.Token);
        _previewRequests[candidate] = task;
        _previews = Task.WhenAll(_previews, task);
        return task;
    }
    public void SetPreviewVisible(CoverCandidate candidate, bool visible)
    {
        if (visible) { _visiblePreviews.Add(candidate); _ = EnsurePreviewAsync(candidate); }
        else { _visiblePreviews.Remove(candidate); if (candidate.Thumbnail is not null) TouchPreview(candidate); }
    }
    private void TouchPreview(CoverCandidate candidate)
    {
        _previewCache.Remove(candidate); _previewCache.AddLast(candidate);
        while (_previewCache.Count > 48)
        {
            var oldest = _previewCache.First;
            while (oldest is not null && _visiblePreviews.Contains(oldest.Value)) oldest = oldest.Next;
            if (oldest is null) break;
            oldest.Value.Thumbnail = null; _previewCache.Remove(oldest);
        }
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
            TouchPreview(candidate);
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
        IsApplying = true;
        try
        {
            var selected = Selected;
            var game = selected?.Game ?? SelectedGame;
            if (selected is null || game is null) return;
            var bytes = await _client.DownloadImageAsync(selected.Url, _lifetime.Token);
            if (await _apply(new(bytes, game, _matchedQuery), _lifetime.Token)) Completed?.Invoke();
            else Status = L.Get("CouldNotSaveTheCoverPleaseTryAgain");
        }
        finally { IsApplying = false; }
    });

    private Task RunAsync(Func<Task> action)
    {
        if (_closed || IsLoading) return Task.CompletedTask;
        IsLoading = true;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchCancellation = cancellation;
        var revision = _searchRevision;
        Status = null;
        return _active = ExecuteAsync();

        async Task ExecuteAsync()
        {
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (SteamGridDbException exception) { if (revision == _searchRevision) Status = exception.Message; }
            catch (Exception) { if (revision == _searchRevision) Status = L.Get("CouldNotGetTheCoverPleaseTryAgain"); }
            finally
            {
                if (ReferenceEquals(_searchCancellation, cancellation)) { _searchCancellation = null; IsLoading = false; }
                cancellation.Dispose();
            }
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
        _previewRequests.Clear(); _previewCache.Clear(); _visiblePreviews.Clear();
    }

    public async Task CloseAsync()
    {
        if (_closed) return;
        _closed = true;
        _lifetime.Cancel();
        CancelDebounce();
        await Task.WhenAll(_active, _scheduled);
        await ClearPreviewsAsync();
        _downloadGate.Dispose();
        _lifetime.Dispose();
    }
}
