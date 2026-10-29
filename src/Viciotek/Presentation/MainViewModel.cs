using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Processes;
using Viciotek.Presentation.Dialogs;

namespace Viciotek.Presentation;

public sealed class MainViewModel : ObservableObject, IGameCardActions, IDisposable
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".jfif"
    };

    private static readonly HashSet<string> GameExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".exe", ".url"
    };

    private readonly LibraryStore _libraryStore;
    private readonly SettingsStore _settingsStore;
    private readonly GameImporter _importer;
    private readonly IGameLauncher _launcher;
    private readonly CoverStore _covers;
    private readonly SteamGridDbClient _steamGrid;
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _coverUpdateGate = new(1, 1);
    private readonly SemaphoreSlim _galleryGate = new(1, 1);
    private readonly SemaphoreSlim _automaticGate = new(1, 1);
    private readonly HashSet<Task> _operations = [];
    private readonly HashSet<Guid> _pendingCoverSearches = [];
    private Task? _closeTask;
    private bool _closing;
    private readonly string? _startupWarning;
    private CancellationTokenSource? _toastCancellation;
    private GameCardViewModel? _coverDropTarget;
    private bool _isImporting;
    private bool _isToastVisible;
    private bool _toastIsError;
    private bool _hasVisibleGames;
    private bool _launchRequested;
    private bool _disposed;
    private string _searchQuery = string.Empty;
    private string _normalizedSearchQuery = string.Empty;
    private string _toastMessage = string.Empty;

    public MainViewModel(
        IReadOnlyList<Game> games,
        AppSettings settings,
        LibraryStore libraryStore,
        SettingsStore settingsStore,
        GameImporter importer,
        IGameLauncher launcher,
        CoverStore covers,
        SteamGridDbClient steamGrid,
        IDialogService dialogs,
        string? startupWarning)
    {
        _settings = settings;
        _libraryStore = libraryStore;
        _settingsStore = settingsStore;
        _importer = importer;
        _launcher = launcher;
        _covers = covers;
        _steamGrid = steamGrid;
        _dialogs = dialogs;
        _startupWarning = startupWarning;

        Games = new ObservableCollection<GameCardViewModel>(
            games.Select(game => new GameCardViewModel(game, this, _covers)));
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;
        _hasVisibleGames = Games.Count > 0;

        AddGamesCommand = new AsyncRelayCommand(AddGamesFromDialogAsync);
        OpenPreferencesCommand = new AsyncRelayCommand(OpenPreferencesAsync);
        ClearSearchCommand = new RelayCommand(ClearSearch, () => SearchQuery.Length > 0);
    }

    public ObservableCollection<GameCardViewModel> Games { get; }
    public ICollectionView GamesView { get; }
    public AsyncRelayCommand AddGamesCommand { get; }
    public AsyncRelayCommand OpenPreferencesCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public bool IsSteamGridConfigured => _steamGrid.IsConfigured;
    public bool AnimationsEnabled => _settings.AnimationsEnabled;
    bool IGameCardActions.CanFindCover => IsSteamGridConfigured;
    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";
    public GameCardViewModel? SelectedGame { get; set; }

    public event EventHandler? CloseRequested;
    public event EventHandler? ScrollToTopRequested;
    public event Action<GameCardViewModel>? ScrollToItemRequested;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value ?? string.Empty))
            {
                return;
            }

            _normalizedSearchQuery = SearchText.Normalize(_searchQuery);
            GamesView.Refresh();
            UpdateViewState();
            OnPropertyChanged(nameof(NoResultsText));
            ClearSearchCommand.RaiseCanExecuteChanged();
            ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public string NoResultsText => "Sin resultados";

    public bool ShowEmptyState => Games.Count == 0;
    public bool ShowNoResults => Games.Count > 0 && !_hasVisibleGames;

    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (SetProperty(ref _isImporting, value))
            {
                OnPropertyChanged(nameof(IsDropEnabled));
            }
        }
    }

    public bool IsDropEnabled => !_isImporting && !_closing;
    public bool IsClosing => _closing;

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public bool ToastIsError
    {
        get => _toastIsError;
        private set => SetProperty(ref _toastIsError, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public void ShowPendingNotice()
    {
        if (!string.IsNullOrWhiteSpace(_startupWarning))
        {
            ShowToast(_startupWarning, isError: false);
        }
    }

    public void ShowImageDropHint() =>
        ShowToast("Elegí una portada para esta imagen.", isError: false);

    public void ShowUnsupportedDropHint() =>
        ShowToast("Solo se pueden agregar accesos .lnk, ejecutables .exe o archivos .url.", isError: true);

    public void SetCoverDropTarget(GameCardViewModel? card)
    {
        if (ReferenceEquals(_coverDropTarget, card))
        {
            return;
        }

        if (_coverDropTarget is not null)
        {
            _coverDropTarget.IsCoverDropTarget = false;
        }

        _coverDropTarget = card;
        if (card is not null && Games.Contains(card))
        {
            card.IsCoverDropTarget = true;
        }
    }

    public async Task HandleDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || _isImporting)
        {
            return;
        }

        var imagePaths = paths.Where(IsImagePath).ToList();
        var gamePaths = paths.Where(IsGamePath).ToList();

        if (gamePaths.Count == 0)
        {
            if (imagePaths.Count > 0)
            {
                ShowImageDropHint();
            }
            else
            {
                ShowUnsupportedDropHint();
            }

            return;
        }

        await ImportFilesAsync(gamePaths);
    }

    public Task ReplaceCoverFromDropAsync(GameCardViewModel card, string imagePath) =>
        RunOperationAsync(() => ReplaceCoverFromDropCoreAsync(card, imagePath));

    private async Task ReplaceCoverFromDropCoreAsync(GameCardViewModel card, string imagePath)
    {
        if (!Games.Contains(card) || !IsImagePath(imagePath))
        {
            ShowImageDropHint();
            return;
        }

        await ApplyCoverFromFileAsync(card, imagePath);
    }

    public async Task LaunchSelectedAsync()
    {
        if (SelectedGame is not null)
        {
            await PlayAsync(SelectedGame);
        }
    }

    public Task LaunchSearchResultAsync()
    {
        var card = SelectedGame is not null && GamesView.Contains(SelectedGame)
            ? SelectedGame
            : GamesView.Cast<GameCardViewModel>().FirstOrDefault();
        return card is null || !card.PlayCommand.CanExecute(null) ? Task.CompletedTask : PlayAsync(card);
    }

    public void SaveAllNow(double width, double height, bool maximized)
    {
        try
        {
            _settings.WindowWidth = Math.Clamp(width, 720, 10000);
            _settings.WindowHeight = Math.Clamp(height, 480, 10000);
            _settings.WindowMaximized = maximized;
            _settingsStore.SaveNow(_settings);
            _libraryStore.SaveNow(Games.Select(card => card.Model).ToList());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Closing must not be blocked by a storage failure.
        }
    }

    public Task PlayAsync(GameCardViewModel card) => RunOperationAsync(() => PlayCoreAsync(card));

    private async Task PlayCoreAsync(GameCardViewModel card)
    {
        if (_launchRequested || !Games.Contains(card))
        {
            return;
        }

        _launchRequested = true;
        card.IsLaunching = true;
        try
        {
            var result = await _launcher.LaunchAsync(card.Model);
            if (result.Success)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _launchRequested = false;
                card.IsLaunching = false;
                ShowToast(result.Error ?? "Windows no pudo iniciar el juego.", isError: true);
            }
        }
        catch (Exception exception)
        {
            _launchRequested = false;
            card.IsLaunching = false;
            ShowToast("No se pudo iniciar el juego: " + exception.Message, isError: true);
        }
    }

    public Task ChangeCoverAsync(GameCardViewModel card) => RunOperationAsync(() => ChangeCoverCoreAsync(card));

    private async Task ChangeCoverCoreAsync(GameCardViewModel card)
    {
        if (!Games.Contains(card))
        {
            return;
        }

        var imagePath = _dialogs.PickCoverFile();
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            await ApplyCoverFromFileAsync(card, imagePath);
        }
    }

    public Task FindCoverAsync(GameCardViewModel card) => RunOperationAsync(() => FindCoverCoreAsync(card));

    private async Task FindCoverCoreAsync(GameCardViewModel card)
    {
        if (!Games.Contains(card))
        {
            return;
        }

        if (!_steamGrid.IsConfigured)
        {
            ShowToast("Configurá tu clave de SteamGridDB en Preferencias (Ctrl+,) para buscar portadas.", isError: false);
            return;
        }

        if (card.Model.CoverFileName is not null || card.IsBusy) return;
        try
        {
            if (await TryAutomaticCoverAsync(card)) await SearchCoverCoreAsync(card, manualFallback: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SteamGridDbException exception)
        {
            ShowToast(exception.Message, isError: true);
        }
        catch (CoverImageException exception)
        {
            ShowToast(exception.Message, isError: true);
        }
        catch (Exception)
        {
            ShowToast("No se pudo obtener la portada automática.", isError: true);
        }
    }

    public Task EditAsync(GameCardViewModel card) => RunOperationAsync(() => EditCoreAsync(card));

    public Task SearchCoverAsync(GameCardViewModel card) => RunOperationAsync(() => SearchCoverCoreAsync(card));

    private async Task SearchCoverCoreAsync(GameCardViewModel card, bool manualFallback = false)
    {
        if (!Games.Contains(card))
        {
            return;
        }

        if (!_steamGrid.IsConfigured)
        {
            ShowToast("Configurá tu clave de SteamGridDB en Preferencias (Ctrl+,) para buscar portadas.", isError: false);
            return;
        }

        await _galleryGate.WaitAsync(_lifetime.Token);
        var ownsBusyState = false;
        try
        {
            if (!Games.Contains(card) || card.IsBusy || (manualFallback && card.Model.CoverFileName is not null)) return;
            card.SetBusy("Buscando portadas…");
            ownsBusyState = true;
            var before = card.Model.Clone();
            var resolved = await Task.Run(() => _importer.ResolveIdentityForCoverSearch(before), _lifetime.Token);
            await _dialogs.PickCoverFromGalleryAsync(resolved, _steamGrid,
                (selection, token) => CommitCoverAsync(card,
                    () => _covers.SetFromBytesAsync(card.Model.Id, selection.Bytes, card.Model.CoverFileName, token),
                    onlyIfMissing: manualFallback,
                    identity: new(selection.Game, selection.SearchName, []),
                    resolvedMetadata: resolved,
                    canCommit: () => !token.IsCancellationRequested && SameIdentity(card.Model, before)),
                manualFallback, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SteamGridDbException exception)
        {
            ShowToast(exception.Message, isError: true);
        }
        catch (CoverImageException exception)
        {
            ShowToast(exception.Message, isError: true);
        }
        catch (Exception)
        {
            ShowToast("No se pudo obtener la portada.", isError: true);
        }
        finally
        {
            if (ownsBusyState) card.IsBusy = false;
            _galleryGate.Release();
        }
    }

    private async Task EditCoreAsync(GameCardViewModel card)
    {
        if (!Games.Contains(card))
        {
            return;
        }

        var result = _dialogs.EditGame(card.Model.Clone());
        if (result is null)
        {
            return;
        }

        await _coverUpdateGate.WaitAsync(_lifetime.Token);
        try
        {
            if (!Games.Contains(card)) return;
            var edited = card.Model.Clone();
            edited.Name = result.Name;
            edited.LaunchPath = result.Target;
            edited.LaunchKind = result.LaunchKind;
            edited.Arguments = result.Arguments;
            edited.WorkingDirectory = result.WorkingDirectory;
            edited.IconPath = ResolveIconPath(edited, result);
            if (edited.Name != card.Name || edited.LaunchPath != card.Model.LaunchPath)
            {
                edited.IsNameCustomized = true;
                edited.CoverSearchName = edited.Name;
                edited.SteamGridDbGameId = null;
                edited.SteamGridDbName = null;
                if (edited.LaunchPath != card.Model.LaunchPath)
                {
                    edited.SteamAppId = null;
                    edited.IdentityCandidates.Clear();
                    edited.ExecutableName = edited.LaunchKind == LaunchKind.Executable ? Path.GetFileName(edited.LaunchPath) : null;
                }
            }
            var snapshot = Games.Select(item => ReferenceEquals(item, card) ? edited : item.Model.Clone()).ToList();
            var saved = await PersistAsync(snapshot);
            if (saved)
            {
                card.Model.Name = edited.Name;
                card.Model.LaunchPath = edited.LaunchPath;
                card.Model.LaunchKind = edited.LaunchKind;
                card.Model.Arguments = edited.Arguments;
                card.Model.WorkingDirectory = edited.WorkingDirectory;
                card.Model.IconPath = edited.IconPath;
                card.Model.IsNameCustomized = edited.IsNameCustomized;
                card.Model.CoverSearchName = edited.CoverSearchName;
                card.Model.SteamGridDbGameId = edited.SteamGridDbGameId;
                card.Model.SteamGridDbName = edited.SteamGridDbName;
                card.Model.SteamAppId = edited.SteamAppId;
                card.Model.IdentityCandidates = edited.IdentityCandidates;
                card.Model.ExecutableName = edited.ExecutableName;
                card.RefreshDetails();
                GamesView.Refresh();
                UpdateViewState();
                await PruneCoversAsync();
            }
            ReportSaveFailure(saved, "No se pudieron guardar los cambios.");
        }
        finally
        {
            _coverUpdateGate.Release();
        }
    }

    public Task RemoveAsync(GameCardViewModel card) => RunOperationAsync(() => RemoveCoreAsync(card));

    private async Task RemoveCoreAsync(GameCardViewModel card)
    {
        if (!Games.Contains(card))
        {
            return;
        }

        if (!_dialogs.ConfirmRemove(card.Name))
        {
            return;
        }

        await _coverUpdateGate.WaitAsync(_lifetime.Token);
        try
        {
            if (!Games.Contains(card)) return;
            var saved = await PersistAsync(Games.Where(item => !ReferenceEquals(item, card)).Select(item => item.Model.Clone()).ToList());
            if (saved)
            {
                Games.Remove(card);
                if (ReferenceEquals(SelectedGame, card)) SelectedGame = null;
                UpdateViewState();
                await PruneCoversAsync();
            }
            ReportSaveFailure(saved, "No se pudo guardar el cambio.");
        }
        finally
        {
            _coverUpdateGate.Release();
        }
    }

    public async Task AddGamesFromDialogAsync()
    {
        var files = _dialogs.PickGameFiles();
        if (files is { Length: > 0 })
        {
            await ImportFilesAsync(files);
        }
    }

    public Task ImportFilesAsync(IReadOnlyList<string> paths) => RunOperationAsync(() => ImportFilesCoreAsync(paths));

    private async Task ImportFilesCoreAsync(IReadOnlyList<string> paths)
    {
        if (_isImporting)
        {
            return;
        }

        IsImporting = true;
        var acquired = false;
        var addedCards = new List<GameCardViewModel>();
        try
        {
            await _coverUpdateGate.WaitAsync(_lifetime.Token);
            acquired = true;
            var existing = Games.Select(card => card.Model.Clone()).ToList();
            var outcome = await Task.Run(
                () => _importer.Import(paths, existing),
                _lifetime.Token);

            var saved = outcome.Added.Count == 0 || await PersistAsync(existing.Concat(outcome.Added).ToList());
            if (!saved)
            {
                ShowToast("No se pudieron guardar los juegos.", isError: true);
                return;
            }

            foreach (var game in outcome.Added)
            {
                var card = new GameCardViewModel(game, this, _covers);
                Games.Add(card);
                addedCards.Add(card);
            }

            if (addedCards.Count > 0 && SearchQuery.Length > 0)
            {
                SearchQuery = string.Empty;
            }

            UpdateViewState();

            var summary = BuildImportMessage(outcome, saved);
            ShowToast(summary.Message, summary.IsError);

            if (addedCards.Count > 0)
            {
                ScrollToItemRequested?.Invoke(addedCards[0]);
            }
            if (outcome.Added.Count > 0) await PruneCoversAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowToast("No se pudieron agregar los juegos: " + exception.Message, isError: true);
        }
        finally
        {
            if (acquired) _coverUpdateGate.Release();
            IsImporting = false;
            if (addedCards.Count > 0 && _settings.AutoFetchCovers && _steamGrid.IsConfigured)
                _ = RunOperationAsync(() => FetchAutomaticCoversAsync(addedCards));
        }
    }

    public Task OpenPreferencesAsync() => RunOperationAsync(OpenPreferencesCoreAsync);

    private async Task OpenPreferencesCoreAsync()
    {
        var updated = _dialogs.EditPreferences(_settings.Clone());
        if (updated is null)
        {
            return;
        }

        updated.SteamGridDbApiKey = string.IsNullOrWhiteSpace(updated.SteamGridDbApiKey)
            ? null
            : updated.SteamGridDbApiKey.Trim();
        try
        {
            await _settingsStore.SaveAsync(updated);
            _settings.SteamGridDbApiKey = updated.SteamGridDbApiKey;
            _settings.AutoFetchCovers = updated.AutoFetchCovers;
            _settings.AnimationsEnabled = updated.AnimationsEnabled;
            _steamGrid.UpdateApiKey(_settings.SteamGridDbApiKey);
            OnPropertyChanged(nameof(IsSteamGridConfigured));
            OnPropertyChanged(nameof(AnimationsEnabled));
            foreach (var card in Games) card.RefreshCoverAvailability();
            DismissToast();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowToast("No se pudieron guardar las preferencias.", isError: true);
        }
    }

    private async Task ApplyCoverFromFileAsync(GameCardViewModel card, string imagePath)
    {
        if (card.IsBusy || !Games.Contains(card))
        {
            return;
        }

        card.SetBusy("Actualizando portada…");
        try
        {
            var saved = await CommitCoverAsync(card, () => _covers.SetFromFileAsync(
                card.Model.Id, imagePath, card.Model.CoverFileName, _lifetime.Token));
            ReportSaveFailure(saved, "No se pudo guardar la portada.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (CoverImageException exception)
        {
            ShowToast(exception.Message, isError: true);
        }
        catch (Exception)
        {
            ShowToast("No se pudo actualizar la portada.", isError: true);
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    private async Task<bool> CommitCoverAsync(GameCardViewModel card, Func<Task<CoverUpdate>> prepare,
        bool onlyIfMissing = false, GameIdentityMatch? identity = null, Func<bool>? canCommit = null, Game? resolvedMetadata = null)
    {
        await _coverUpdateGate.WaitAsync(_lifetime.Token);
        try
        {
            if (!Games.Contains(card) || (onlyIfMissing && card.Model.CoverFileName is not null) || canCommit?.Invoke() == false) return false;
            var update = await prepare();
            var committed = false;
            try
            {
                if (canCommit?.Invoke() == false) return false;
                var replacement = card.Model.Clone();
                replacement.CoverFileName = update.CoverFileName;
                if (resolvedMetadata is not null)
                {
                    replacement.SteamAppId = resolvedMetadata.SteamAppId;
                    replacement.ExecutableName = resolvedMetadata.ExecutableName;
                    replacement.ImportSource = resolvedMetadata.ImportSource;
                    replacement.IdentityCandidates = [.. resolvedMetadata.IdentityCandidates];
                }
                if (identity?.Confirmed is { } confirmed)
                {
                    replacement.SteamGridDbGameId = confirmed.Id;
                    replacement.SteamGridDbName = confirmed.Name;
                    replacement.CoverSearchName = identity.SearchName;
                }
                var snapshot = Games.Select(item => ReferenceEquals(item, card) ? replacement : item.Model.Clone()).ToList();
                if (!await PersistAsync(snapshot)) return false;
                committed = true;
                card.Model.CoverFileName = update.CoverFileName;
                card.Model.SteamGridDbGameId = replacement.SteamGridDbGameId;
                card.Model.SteamGridDbName = replacement.SteamGridDbName;
                card.Model.CoverSearchName = replacement.CoverSearchName;
                card.Model.SteamAppId = replacement.SteamAppId;
                card.Model.ExecutableName = replacement.ExecutableName;
                card.Model.ImportSource = replacement.ImportSource;
                card.Model.IdentityCandidates = replacement.IdentityCandidates;
                card.RefreshCover();
                await PruneCoversAsync();
                return true;
            }
            finally
            {
                if (!committed) await Task.Run(() => _covers.Discard(update));
            }
        }
        finally
        {
            _coverUpdateGate.Release();
        }
    }

    private async Task FetchAutomaticCoversAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        // Let the import return and release its persistence gate before showing any modal.
        await Task.Yield();
        await _automaticGate.WaitAsync(_lifetime.Token);
        try
        {
            foreach (var card in cards)
            {
                if (!_settings.AutoFetchCovers || !_steamGrid.IsConfigured || _lifetime.IsCancellationRequested)
                {
                    return;
                }

                if (card.Model.CoverFileName is not null || !Games.Contains(card) || card.IsBusy)
                {
                    continue;
                }

                try
                {
                    if (await TryAutomaticCoverAsync(card) && _settings.AutoFetchCovers)
                        await SearchCoverCoreAsync(card, manualFallback: true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SteamGridDbException exception)
                {
                    ShowToast(exception.Message, isError: true);
                    return; // Do not retry the whole batch on auth, network, or rate-limit errors.
                }
                catch (CoverImageException exception) { ShowToast(exception.Message, isError: true); }
                catch (Exception) { ShowToast("No se pudo obtener la portada automática.", isError: true); return; }
            }
        }
        finally { _automaticGate.Release(); }
    }

    // true means manual input is needed; HTTP failures propagate and never open a fallback modal.
    private async Task<bool> TryAutomaticCoverAsync(GameCardViewModel card)
    {
        if (!_pendingCoverSearches.Add(card.Model.Id)) return false;
        card.IsFindingCover = true;
        try { return await TryAutomaticCoverCoreAsync(card); }
        finally
        {
            card.IsFindingCover = false;
            _pendingCoverSearches.Remove(card.Model.Id);
        }
    }

    private async Task<bool> TryAutomaticCoverCoreAsync(GameCardViewModel card)
    {
        var before = card.Model.Clone();
        var resolved = await Task.Run(() => _importer.ResolveIdentityForCoverSearch(before), _lifetime.Token);
        if (!Games.Contains(card) || card.IsBusy || !SameIdentity(card.Model, before) || card.Model.CoverFileName is not null) return false;
        var identity = await new GameIdentitySearch(_steamGrid).FindAsync(resolved, _lifetime.Token);
        if (!Games.Contains(card) || card.IsBusy || !SameIdentity(card.Model, before) || card.Model.CoverFileName is not null) return false;
        if (identity.Confirmed is null) return true;
        var grids = await _steamGrid.GetGridsAsync(identity.Confirmed.Id, _lifetime.Token);
        if (!SameIdentity(card.Model, before) || card.Model.CoverFileName is not null) return false;
        if (grids.Count == 0) return true;
        var bytes = await _steamGrid.DownloadImageAsync(grids[0].Url, _lifetime.Token);
        var saved = await CommitCoverAsync(card, () => _covers.SetFromBytesAsync(card.Model.Id, bytes, null, _lifetime.Token),
            onlyIfMissing: true, identity: identity, resolvedMetadata: resolved,
            canCommit: () => !card.IsBusy && SameIdentity(card.Model, before));
        if (!saved && SameIdentity(card.Model, before) && card.Model.CoverFileName is null)
            ShowToast("No se pudo guardar la portada.", isError: true);
        return false;
    }

    private static bool SameIdentity(Game current, Game previous) => current.Name == previous.Name &&
        current.LaunchPath == previous.LaunchPath && current.SteamGridDbGameId == previous.SteamGridDbGameId &&
        current.CoverFileName == previous.CoverFileName;

    private async Task<bool> PersistAsync(IReadOnlyList<Game> snapshot)
    {
        try
        {
            await _libraryStore.SaveAsync(snapshot, _lifetime.Token);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private async Task PruneCoversAsync()
    {
        if (_closing) return;
        try
        {
            await Task.Run(() =>
            {
                var retained = _libraryStore.GetRetainedGames();
                if (retained is not null) _covers.PruneUnreferenced(retained);
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cleanup failure is harmless: referenced originals are untouched.
        }
    }

    private Task RunOperationAsync(Func<Task> action)
    {
        if (_closing) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operations.Add(completion.Task);
        return ExecuteAsync();

        async Task ExecuteAsync()
        {
            try { await action(); }
            catch (OperationCanceledException) when (_closing) { }
            finally
            {
                _operations.Remove(completion.Task);
                completion.SetResult();
            }
        }
    }

    public Task PrepareCloseAsync()
    {
        if (_closeTask is not null) return _closeTask;
        _closing = true;
        OnPropertyChanged(nameof(IsDropEnabled));
        _lifetime.Cancel();
        DismissToast();
        return _closeTask = DrainAndDisposeAsync();
    }

    private async Task DrainAndDisposeAsync()
    {
        await Task.WhenAll(_operations.ToArray());
        if (_disposed) return;
        _disposed = true;
        _steamGrid.Dispose();
        _coverUpdateGate.Dispose();
        _galleryGate.Dispose();
        _automaticGate.Dispose();
        _lifetime.Dispose();
    }

    private ImportToast BuildImportMessage(ImportOutcome outcome, bool saved)
    {
        var errors = outcome.Problems.Count - outcome.DuplicateCount;
        string message;
        var isError = false;

        if (outcome.Added.Count > 0)
        {
            message = outcome.Added.Count == 1
                ? $"«{outcome.Added[0].Name}» agregado."
                : $"{outcome.Added.Count} juegos agregados.";
            if (outcome.DuplicateCount > 0)
            {
                message += outcome.DuplicateCount == 1
                    ? " 1 ya estaba en la biblioteca."
                    : $" {outcome.DuplicateCount} ya estaban en la biblioteca.";
            }

            if (errors > 0)
            {
                message += errors == 1
                    ? " 1 archivo no se pudo agregar."
                    : $" {errors} archivos no se pudieron agregar.";
            }
        }
        else if (outcome.DuplicateCount > 0 && errors == 0)
        {
            message = outcome.DuplicateCount == 1
                ? "Ese juego ya está en la biblioteca."
                : "Esos juegos ya están en la biblioteca.";
        }
        else if (outcome.Problems.Count > 0)
        {
            var first = outcome.Problems[0];
            message = $"{Path.GetFileName(first.Path)}: {first.Message}";
            if (outcome.Problems.Count > 1)
            {
                message += $" (+{outcome.Problems.Count - 1} más)";
            }

            isError = true;
        }
        else
        {
            message = "No se agregó ningún juego.";
            isError = true;
        }

        if (!saved)
        {
            message += " No se pudo guardar la biblioteca.";
            isError = true;
        }

        if (outcome.Added.Count > 0 && _settings.AutoFetchCovers && !_steamGrid.IsConfigured)
            message += " Configurá SteamGridDB en Preferencias para buscar portadas.";

        return new ImportToast(message, isError);
    }

    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    private bool FilterGame(object item) =>
        item is GameCardViewModel card && (_normalizedSearchQuery.Length == 0 ||
            card.SearchKey.Contains(_normalizedSearchQuery, StringComparison.Ordinal));

    private void ReportSaveFailure(bool saved, string message)
    {
        if (saved)
        {
            DismissToast();
        }
        else
        {
            ShowToast(message, isError: true);
        }
    }

    private void DismissToast()
    {
        _toastCancellation?.Cancel();
        IsToastVisible = false;
        ToastIsError = false;
    }

    private void UpdateViewState()
    {
        _hasVisibleGames = Games.Count > 0 && !GamesView.IsEmpty;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoResults));
        OnPropertyChanged(nameof(NoResultsText));
    }

    private static string? ResolveIconPath(Game game, GameEditResult result)
    {
        if (result.LaunchKind == LaunchKind.Executable)
        {
            return result.Target;
        }

        if (!string.IsNullOrWhiteSpace(game.ShortcutPath) && File.Exists(game.ShortcutPath))
        {
            return game.ShortcutPath;
        }

        return !string.IsNullOrWhiteSpace(game.IconPath) && File.Exists(game.IconPath)
            ? game.IconPath
            : null;
    }

    private void ShowToast(string message, bool isError)
    {
        if (_closing) return;
        _toastCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _toastCancellation = cancellation;

        ToastMessage = message;
        ToastIsError = isError;
        IsToastVisible = true;
        _ = HideToastAsync(cancellation, isError ? TimeSpan.FromSeconds(7) : TimeSpan.FromSeconds(4.5));
    }

    private async Task HideToastAsync(CancellationTokenSource cancellation, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                IsToastVisible = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_toastCancellation, cancellation))
            {
                _toastCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private static bool IsImagePath(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));

    private static bool IsGamePath(string path) =>
        GameExtensions.Contains(Path.GetExtension(path));

    public void Dispose() => _ = PrepareCloseAsync();

    private sealed record ImportToast(string Message, bool IsError);
}
