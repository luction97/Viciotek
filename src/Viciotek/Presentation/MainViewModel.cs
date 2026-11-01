using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Detection;
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
    private CancellationTokenSource _coverConnectionCancellation = new();
    private int _coverConnectionRevision;
    private bool _editingSettings;
    private bool _coverInvitationOffered;
    private bool _showCoverSetupHint;
    private readonly HashSet<Task> _operations = [];
    private readonly HashSet<Guid> _pendingCoverSearches = [];
    private Task? _closeTask;
    private bool _closing;
    private readonly string? _startupWarning;
    private readonly InstalledGameScanner? _installedScanner;
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
        string? startupWarning,
        InstalledGameScanner? installedScanner = null)
    {
        _settings = settings;
        _libraryStore = libraryStore;
        _lastImportBatchId = libraryStore.LastImportBatchId;
        _settingsStore = settingsStore;
        _importer = importer;
        _launcher = launcher;
        _covers = covers;
        _steamGrid = steamGrid;
        _dialogs = dialogs;
        _startupWarning = startupWarning;
        _installedScanner = installedScanner;
        _coverInvitationOffered = games.Count > 0 || settings.WelcomeCoverSetupCompleted || steamGrid.IsConfigured;

        Games = new ObservableCollection<GameCardViewModel>(
            games.Select(game => new GameCardViewModel(game, this, _covers)));
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;
        _hasVisibleGames = Games.Count > 0;

        AddGamesCommand = new AsyncRelayCommand(AddGamesFromDialogAsync);
        ScanInstalledGamesCommand = new AsyncRelayCommand(ScanInstalledGamesAsync);
        UndoImportCommand = new AsyncRelayCommand(UndoLastImportAsync, () => CanUndoImport);
        OpenPreferencesCommand = new AsyncRelayCommand(OpenPreferencesAsync);
        ConnectCoversCommand = new AsyncRelayCommand(ConnectCoversAsync);
        SkipWelcomeConnectionCommand = new AsyncRelayCommand(SkipWelcomeConnectionAsync);
        DismissCoverSetupCommand = new RelayCommand(() =>
        {
            _showCoverSetupHint = false;
            OnPropertyChanged(nameof(ShowCoverSetupHint));
        });
        ClearSearchCommand = new RelayCommand(ClearSearch);
    }

    public ObservableCollection<GameCardViewModel> Games { get; }
    public ICollectionView GamesView { get; }
    public AsyncRelayCommand AddGamesCommand { get; }
    public AsyncRelayCommand ScanInstalledGamesCommand { get; }
    public AsyncRelayCommand OpenPreferencesCommand { get; }
    public AsyncRelayCommand ConnectCoversCommand { get; }
    public AsyncRelayCommand SkipWelcomeConnectionCommand { get; }
    public RelayCommand DismissCoverSetupCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public bool IsSteamGridConfigured => _steamGrid.IsConfigured;
    public bool AnimationsEnabled => _settings.AnimationsEnabled;
    private int? _previewCoverSize;
    public double PreferredCoverWidth => AppSettings.CoverWidthFor(_previewCoverSize ?? _settings.CoverSizePreset);
    public double PreferredCoverHeight => PreferredCoverWidth * 1.5;
    public void PreviewCoverSize(int? preset)
    {
        _previewCoverSize = preset.HasValue ? Math.Clamp(preset.Value, 0, 3) : null;
        OnPropertyChanged(nameof(PreferredCoverWidth));
        OnPropertyChanged(nameof(PreferredCoverHeight));
    }
    bool IGameCardActions.CanFindCover => IsSteamGridConfigured;
    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.1.4";
    private Guid? _lastImportBatchId;
    public ObservableCollection<GameCardViewModel> SelectedGames { get; } = [];
    public AsyncRelayCommand UndoImportCommand { get; }
    System.Windows.Input.ICommand? IGameCardActions.UndoImportCommand => UndoImportCommand;
    public bool CanUndoImport => _lastImportBatchId is Guid batch && Games.Any(card => card.Model.ImportBatchId == batch);
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

            var normalized = SearchText.Normalize(_searchQuery);
            ClearSearchCommand.RaiseCanExecuteChanged();
            if (normalized == _normalizedSearchQuery) return;
            _normalizedSearchQuery = normalized;
            GamesView.Refresh();
            UpdateViewState();
            OnPropertyChanged(nameof(NoResultsText));
            ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public string NoResultsText => L.Get("NoResults");

    public bool ShowEmptyState => Games.Count == 0;
    public bool ShowWelcomeConnection => ShowEmptyState && !_coverInvitationOffered && !IsSteamGridConfigured;
    public bool ShowWelcomeImport => ShowEmptyState && !ShowWelcomeConnection;
    public bool ShowCoverSetupHint => _showCoverSetupHint && Games.Count > 0 && !IsSteamGridConfigured;
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
        ShowToast(L.Get("ChooseACoverForThisImage"), isError: false);

    public void ShowUnsupportedDropHint() =>
        ShowToast(L.Get("OnlyLnkShortcutsExeProgramsOrUrlFilesCanBeAdded"), isError: true);

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
                ShowToast(result.Error ?? L.Get("WindowsCouldNotLaunchTheGame"), isError: true);
            }
        }
        catch (Exception exception)
        {
            _launchRequested = false;
            card.IsLaunching = false;
            App.RecordUnexpectedError(exception);
            ShowToast(L.Get("FriendlyLaunchFailure"), isError: true);
        }
    }

    public Task ChangeCoverAsync(GameCardViewModel card) => RunOperationAsync(() => ChangeCoverCoreAsync(card));

    private async Task ChangeCoverCoreAsync(GameCardViewModel card)
    {
        if (!Games.Contains(card))
        {
            return;
        }

        var source = _dialogs.ChooseCoverSource(_steamGrid.IsConfigured, card.Model.CoverFileName is null);
        if (source is null) return;
        if (source is CoverSource.SteamGridDb or CoverSource.Automatic)
        {
            if (!_steamGrid.IsConfigured) await OpenPreferencesCoreAsync();
            if (!_steamGrid.IsConfigured) return;
            if (source == CoverSource.Automatic) await FindCoverCoreAsync(card);
            else await SearchCoverCoreAsync(card);
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
            ShowToast(L.Get("ConnectSteamgriddbInSettingsCtrlToFindCovers"), isError: false);
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
            ShowToast(L.Get("CouldNotGetACoverAutomatically"), isError: true);
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
            ShowToast(L.Get("ConnectSteamgriddbInSettingsCtrlToFindCovers"), isError: false);
            return;
        }

        await _galleryGate.WaitAsync(_lifetime.Token);
        var ownsBusyState = false;
        try
        {
            if (!Games.Contains(card) || card.IsBusy || (manualFallback && card.Model.CoverFileName is not null)) return;
            card.SetBusy(L.Get("FindingCovers"));
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
            ShowToast(L.Get("CouldNotGetTheCover"), isError: true);
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
            ReportSaveFailure(saved, L.Get("CouldNotSaveTheChanges"));
        }
        finally
        {
            _coverUpdateGate.Release();
        }
    }

    public Task RemoveAsync(GameCardViewModel card) => RunOperationAsync(() => RemoveCoreAsync(card));

    private Task RemoveCoreAsync(GameCardViewModel card) => RemoveCardsCoreAsync(
        SelectedGames.Contains(card) && SelectedGames.Count > 1 ? SelectedGames.ToArray() : [card], false);
    public Task RemoveSelectionAsync() => RunOperationAsync(() => RemoveCardsCoreAsync(SelectedGames.ToArray(), false));
    public Task UndoLastImportAsync() => RunOperationAsync(() => RemoveCardsCoreAsync(
        Games.Where(card => _lastImportBatchId is not null && card.Model.ImportBatchId == _lastImportBatchId).ToArray(), true));
    private async Task RemoveCardsCoreAsync(IReadOnlyList<GameCardViewModel> selection, bool undo)
    {
        var cards=selection.Where(card=>Games.Contains(card)).Distinct().ToArray();
        if(cards.Length==0)return;
        if(!(undo?_dialogs.ConfirmUndoImport(cards.Length):cards.Length==1?_dialogs.ConfirmRemove(cards[0].Name):_dialogs.ConfirmRemoveMany(cards.Length)))return;
        await _coverUpdateGate.WaitAsync(_lifetime.Token);
        try
        {
            var removing=cards.Where(Games.Contains).ToHashSet();
            if(await PersistAsync(Games.Where(card=>!removing.Contains(card)).Select(card=>card.Model.Clone()).ToList()))
            {
                foreach(var card in removing){Games.Remove(card);SelectedGames.Remove(card);}
                if(SelectedGame is not null && removing.Contains(SelectedGame))SelectedGame=null;
                UpdateViewState();await PruneCoversAsync();
            }
            else ShowToast(L.Get("CouldNotSaveTheChange"),isError:true);
        }
        finally{_coverUpdateGate.Release();}
    }
    public Task ScanInstalledGamesAsync() => RunOperationAsync(ScanInstalledGamesCoreAsync);
    private async Task ScanInstalledGamesCoreAsync()
    {
        if(_installedScanner is null || IsImporting || _closing)return;
        try{var detected=await Task.Run(()=>_installedScanner.ScanAll(),_lifetime.Token);if (!_closing && !_lifetime.IsCancellationRequested) await ImportDetectedGamesAsync(detected);}
        catch(OperationCanceledException){}
        catch(Exception){ShowToast(L.Get("DetectionFailed"),isError:true);}
    }

    public async Task AddGamesFromDialogAsync()
    {
        var files = _dialogs.PickGameFiles(directly: true);
        if (files is { Length: > 0 })
        {
            await ImportFilesAsync(files);
        }
    }

    public Task ImportDetectedGamesAsync(IReadOnlyList<DetectedGame> detected) =>
        RunOperationAsync(() => ImportDetectedGamesCoreAsync(detected));

    private async Task ImportDetectedGamesCoreAsync(IReadOnlyList<DetectedGame> detected)
    {
        if (_isImporting) return;
        IsImporting = true;
        var acquired = false;
        var addedCards = new List<GameCardViewModel>();
        try
        {
            await _coverUpdateGate.WaitAsync(_lifetime.Token);
            acquired = true;
            var existing = Games.Select(card => card.Model.Clone()).ToList();
            var keys = new HashSet<string>(existing.Select(GameIdentity.CreateDuplicateKey), StringComparer.Ordinal);
            var nextOrder = existing.Count == 0 ? 0 : existing.Max(g => g.SortOrder) + 1;

            var candidates=detected.Where(d=>LaunchPlanner.CreatePlan(new Game{Name=d.Name,LaunchPath=d.LaunchPath,LaunchKind=d.LaunchKind,Arguments=d.Arguments,WorkingDirectory=d.WorkingDirectory}).IsValid).ToArray();
            var approved=_dialogs.ReviewImport(candidates);
            if(approved is null)return;
            var selected=approved.Where(candidates.Contains).Distinct().ToArray();
            var batch=Guid.NewGuid();
            var added = new List<Game>();
            foreach (var d in selected)
            {
                // Preserve executable-based entries from earlier detector versions when switching to platform protocols.
                if (d.LaunchKind == LaunchKind.Uri && d.HighConfidence && existing.Any(previous =>
                    previous.LaunchKind == LaunchKind.Executable &&
                    (d.ExecutablePath is not null && GameIdentity.NormalizePath(previous.LaunchPath) == GameIdentity.NormalizePath(d.ExecutablePath) &&
                        string.Equals(previous.Arguments?.Trim() ?? "", d.Arguments?.Trim() ?? "", StringComparison.Ordinal) ||
                     d.Platform == "Steam" && d.WorkingDirectory is not null &&
                        GameIdentity.NormalizePath(previous.LaunchPath).StartsWith(GameIdentity.NormalizePath(d.WorkingDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal)))) continue;
                var game = new Game
                {
                    Id = Guid.NewGuid(),
                    Name = d.Name,
                    ImportBatchId = batch,
                    ImportSource = d.Platform,
                    SteamAppId = d.Platform == "Steam" && int.TryParse(d.PlatformId,out var steamId) ? steamId : null,
                    LaunchPath = d.LaunchPath,
                    LaunchKind = d.LaunchKind,
                    Arguments = d.Arguments,
                    WorkingDirectory = d.WorkingDirectory,
                    IconPath = d.IconPath,
                    AddedAt = DateTimeOffset.UtcNow,
                };
                if(game.SteamAppId is not null && existing.Any(previous=>previous.SteamAppId==game.SteamAppId))continue;
                var duplicateKey = GameIdentity.CreateDuplicateKey(game);
                if (!keys.Add(duplicateKey)) continue;
                game.SortOrder = nextOrder++;
                added.Add(game);
            }

            if (added.Count == 0)
            {
                ShowToast("No se encontraron juegos nuevos.", isError: false);
                return;
            }

            var saved = await PersistAsync(existing.Concat(added).ToList(), batch);
            if (!saved)
            {
                ShowToast(L.Get("CouldNotSaveTheGames"), isError: true);
                return;
            }

            _lastImportBatchId = batch;
            foreach (var game in added)
            {
                var card = new GameCardViewModel(game, this, _covers);
                Games.Add(card);
                addedCards.Add(card);
            }

            if (!_coverInvitationOffered && addedCards.Count > 0)
            {
                _coverInvitationOffered = true;
                _showCoverSetupHint = !_steamGrid.IsConfigured;
            }

            if (addedCards.Count > 0 && SearchQuery.Length > 0)
            {
                SearchQuery = string.Empty;
            }

            UpdateViewState();
            ShowToast($"Se importaron {added.Count} juego(s).", isError: false);
        }
        finally
        {
            if (acquired) _coverUpdateGate.Release();
            IsImporting = false;
            if (addedCards.Count > 0 && _settings.AutoFetchCovers && _steamGrid.IsConfigured)
                _ = RunOperationAsync(() => FetchAutomaticCoversAsync(addedCards));
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

            var batch=Guid.NewGuid();
            // Picking or dropping specific files is already an explicit import decision.
            foreach(var game in outcome.Added)game.ImportBatchId=batch;
            var saved = outcome.Added.Count == 0 || await PersistAsync(existing.Concat(outcome.Added).ToList(), batch);
            if (!saved)
            {
                ShowToast(L.Get("CouldNotSaveTheGames"), isError: true);
                return;
            }

            if(outcome.Added.Count>0)_lastImportBatchId=batch;
            foreach (var game in outcome.Added)
            {
                var card = new GameCardViewModel(game, this, _covers);
                Games.Add(card);
                addedCards.Add(card);
            }

            if (!_coverInvitationOffered && addedCards.Count > 0)
            {
                _coverInvitationOffered = true;
                _showCoverSetupHint = !_steamGrid.IsConfigured;
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
            App.RecordUnexpectedError(exception);
            ShowToast(L.Get("FriendlyImportFailure"), isError: true);
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
    public Task ConnectCoversAsync() => RunOperationAsync(() => EditPreferencesCoreAsync(connectionOnly: true));

    public Task SkipWelcomeConnectionAsync() => RunOperationAsync(async () =>
    {
        if (_editingSettings || _closing || !ShowWelcomeConnection) return;
        _editingSettings = true;
        try
        {
            var updated = _settings.Clone();
            updated.WelcomeCoverSetupCompleted = true;
            await _settingsStore.SaveAsync(updated);
            _settings.WelcomeCoverSetupCompleted = true;
            _coverInvitationOffered = true;
            UpdateViewState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowToast(L.Get("CouldNotSaveTheSettings"), isError: true);
        }
        finally { _editingSettings = false; }
    });

    private async Task OpenPreferencesCoreAsync()
    {
        try { await EditPreferencesCoreAsync(); }
        finally { PreviewCoverSize(null); }
    }

    private async Task EditPreferencesCoreAsync(bool connectionOnly = false)
    {
        if (_editingSettings || _closing) return;
        _editingSettings = true;
        try { await ApplyPreferencesAsync(connectionOnly); }
        finally { _editingSettings = false; }
    }

    private async Task ApplyPreferencesAsync(bool connectionOnly)
    {
        var updated = connectionOnly ? _dialogs.ConnectCovers(_settings.Clone())
            : _dialogs.EditPreferences(_settings.Clone(), preset => PreviewCoverSize(preset));
        if (updated is null)
        {
            return;
        }

        updated.SteamGridDbApiKey = string.IsNullOrWhiteSpace(updated.SteamGridDbApiKey)
            ? null
            : updated.SteamGridDbApiKey.Trim();
        updated.WelcomeCoverSetupCompleted |= _settings.WelcomeCoverSetupCompleted || IsSteamGridConfigured || updated.SteamGridDbApiKey is not null;
        // Centralized rule: no API key => auto-fetch disabled
        if (string.IsNullOrWhiteSpace(updated.SteamGridDbApiKey))
        {
            updated.AutoFetchCovers = false;
        }
        try
        {
            var wasConfigured = _steamGrid.IsConfigured;
            var keyChanged = _settings.SteamGridDbApiKey != updated.SteamGridDbApiKey;
            var autoEnabled = !_settings.AutoFetchCovers && updated.AutoFetchCovers;
            var connectionChanged = keyChanged || _settings.AutoFetchCovers != updated.AutoFetchCovers;
            if (wasConfigured && updated.SteamGridDbApiKey is null)
            {
                updated.AutoFetchCovers = false;
                await _settingsStore.DisconnectAsync(updated);
            }
            else await _settingsStore.SaveAsync(updated);
            if (connectionChanged || wasConfigured && updated.SteamGridDbApiKey is null)
            {
                _coverConnectionRevision++;
                var previous = _coverConnectionCancellation;
                _coverConnectionCancellation = new();
                previous.Cancel(); previous.Dispose();
            }
            _settings.SteamGridDbApiKey = updated.SteamGridDbApiKey;
            _settings.WelcomeCoverSetupCompleted = updated.WelcomeCoverSetupCompleted;
            if (updated.WelcomeCoverSetupCompleted) _coverInvitationOffered = true;
            if (!string.IsNullOrWhiteSpace(updated.SteamGridDbApiKey)) _showCoverSetupHint = false;
            _settings.AutoFetchCovers = updated.AutoFetchCovers;
            _settings.AnimationsEnabled = updated.AnimationsEnabled;
            _settings.CoverSizePreset = updated.CoverSizePreset;
            _settings.Language = updated.Language;
            L.Use(_settings.Language);
            OnPropertyChanged(nameof(NoResultsText));
            if (keyChanged || wasConfigured != !string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey))
                _steamGrid.UpdateApiKey(_settings.SteamGridDbApiKey);
            OnPropertyChanged(nameof(IsSteamGridConfigured));
            OnPropertyChanged(nameof(ShowWelcomeConnection));
            OnPropertyChanged(nameof(ShowWelcomeImport));
            OnPropertyChanged(nameof(ShowCoverSetupHint));
            OnPropertyChanged(nameof(AnimationsEnabled));
            foreach (var card in Games) card.RefreshCoverAvailability();
            DismissToast();
            if (_steamGrid.IsConfigured && (connectionOnly || keyChanged)) ShowToast(L.Get("SteamGridConnected"), isError: false);
            if (_steamGrid.IsConfigured && (connectionOnly || keyChanged || autoEnabled))
                _ = RunOperationAsync(() => FetchAutomaticCoversAsync(Games.Where(card => card.Model.CoverFileName is null).ToArray(),
                    allowManualFallback: false, connectionBackfill: connectionOnly || keyChanged));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowToast(L.Get("CouldNotSaveTheSettings"), isError: true);
        }
    }

    private async Task ApplyCoverFromFileAsync(GameCardViewModel card, string imagePath)
    {
        if (card.IsBusy || !Games.Contains(card))
        {
            return;
        }

        card.SetBusy(L.Get("UpdatingCover"));
        try
        {
            var saved = await CommitCoverAsync(card, () => _covers.SetFromFileAsync(
                card.Model.Id, imagePath, card.Model.CoverFileName, _lifetime.Token));
            ReportSaveFailure(saved, L.Get("CouldNotSaveTheCover"));
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
            ShowToast(L.Get("CouldNotUpdateTheCover"), isError: true);
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

    private async Task FetchAutomaticCoversAsync(IReadOnlyList<GameCardViewModel> cards, bool allowManualFallback = true, bool connectionBackfill = false)
    {
        var revision = _coverConnectionRevision;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, _coverConnectionCancellation.Token);
        var token = cancellation.Token;
        // Let the import return and release its persistence gate before showing any modal.
        await Task.Yield();
        try { await _automaticGate.WaitAsync(token); }
        catch (OperationCanceledException) { return; }
        try
        {
            foreach (var card in cards)
            {
                if (revision != _coverConnectionRevision || (!connectionBackfill && !_settings.AutoFetchCovers) || !_steamGrid.IsConfigured || token.IsCancellationRequested)
                {
                    return;
                }

                if (card.Model.CoverFileName is not null || !Games.Contains(card) || card.IsBusy)
                {
                    continue;
                }

                try
                {
                    if (await TryAutomaticCoverAsync(card, token, revision, connectionBackfill) && allowManualFallback && _settings.AutoFetchCovers && revision == _coverConnectionRevision)
                        await SearchCoverCoreAsync(card, manualFallback: true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SteamGridDbException exception)
                {
                    ShowToast(exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                        ? L.Get("ReviewCoverConnection") : exception.Message, isError: true);
                    return; // Do not retry the whole batch on auth, network, or rate-limit errors.
                }
                catch (CoverImageException exception) { ShowToast(exception.Message, isError: true); }
                catch (Exception) { ShowToast(L.Get("CouldNotGetACoverAutomatically"), isError: true); return; }
            }
        }
        finally { _automaticGate.Release(); }
    }

    // true means manual input is needed; HTTP failures propagate and never open a fallback modal.
    private async Task<bool> TryAutomaticCoverAsync(GameCardViewModel card, CancellationToken token = default, int? revision = null, bool connectionBackfill = false)
    {
        if (!_pendingCoverSearches.Add(card.Model.Id)) return false;
        card.IsFindingCover = true;
        try { return await TryAutomaticCoverCoreAsync(card, token == default ? _lifetime.Token : token, revision, connectionBackfill); }
        finally
        {
            card.IsFindingCover = false;
            _pendingCoverSearches.Remove(card.Model.Id);
        }
    }

    private async Task<bool> TryAutomaticCoverCoreAsync(GameCardViewModel card, CancellationToken token, int? revision, bool connectionBackfill)
    {
        bool CurrentConnection() => revision is null || revision == _coverConnectionRevision && _steamGrid.IsConfigured && (connectionBackfill || _settings.AutoFetchCovers) && !token.IsCancellationRequested;
        var before = card.Model.Clone();
        var resolved = await Task.Run(() => _importer.ResolveIdentityForCoverSearch(before), token);
        if (!CurrentConnection() || !Games.Contains(card) || card.IsBusy || !SameIdentity(card.Model, before) || card.Model.CoverFileName is not null) return false;
        var identity = await new GameIdentitySearch(_steamGrid).FindAsync(resolved, token);
        if (!CurrentConnection() || !Games.Contains(card) || card.IsBusy || !SameIdentity(card.Model, before) || card.Model.CoverFileName is not null) return false;
        if (identity.Confirmed is null) return true;
        var grids = await _steamGrid.GetGridsAsync(identity.Confirmed.Id, token);
        if (!CurrentConnection() || !SameIdentity(card.Model, before) || card.Model.CoverFileName is not null) return false;
        if (grids.Count == 0) return true;
        var bytes = await _steamGrid.DownloadImageAsync(grids[0].Url, token);
        var saved = await CommitCoverAsync(card, () => _covers.SetFromBytesAsync(card.Model.Id, bytes, null, token),
            onlyIfMissing: true, identity: identity, resolvedMetadata: resolved,
            canCommit: () => CurrentConnection() && !card.IsBusy && SameIdentity(card.Model, before));
        if (!saved && SameIdentity(card.Model, before) && card.Model.CoverFileName is null)
            ShowToast(L.Get("CouldNotSaveTheCover"), isError: true);
        return false;
    }

    private static bool SameIdentity(Game current, Game previous) => current.Name == previous.Name &&
        current.LaunchPath == previous.LaunchPath && current.SteamGridDbGameId == previous.SteamGridDbGameId &&
        current.CoverFileName == previous.CoverFileName;

    private async Task<bool> PersistAsync(IReadOnlyList<Game> snapshot, Guid? batch = null)
    {
        try
        {
            await _libraryStore.SaveAsync(snapshot, _lifetime.Token, batch);
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
        _coverConnectionCancellation.Dispose();
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
                ? L.Get("0Added", outcome.Added[0].Name)
                : L.Get("0GamesAdded", outcome.Added.Count);
            if (outcome.DuplicateCount > 0)
            {
                message += outcome.DuplicateCount == 1
                    ? L.Get("1WasAlreadyInTheLibrary")
                    : L.Get("0WereAlreadyInTheLibrary", outcome.DuplicateCount);
            }

            if (errors > 0)
            {
                message += errors == 1
                    ? L.Get("1FileCouldNotBeAdded")
                    : L.Get("0FilesCouldNotBeAdded", errors);
            }
        }
        else if (outcome.DuplicateCount > 0 && errors == 0)
        {
            message = outcome.DuplicateCount == 1
                ? L.Get("ThatGameIsAlreadyInTheLibrary")
                : L.Get("ThoseGamesAreAlreadyInTheLibrary");
        }
        else if (outcome.Problems.Count > 0)
        {
            var first = outcome.Problems[0];
            message = $"{Path.GetFileName(first.Path)}: {first.Message}";
            if (outcome.Problems.Count > 1)
            {
                message += L.Get("0More", outcome.Problems.Count - 1);
            }

            isError = true;
        }
        else
        {
            message = L.Get("NoGamesWereAdded");
            isError = true;
        }

        if (!saved)
        {
            message += L.Get("CouldNotSaveTheLibrary");
            isError = true;
        }


        return new ImportToast(message, isError);
    }

    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        // Also discard a pending delayed text edit when the model is still empty.
        OnPropertyChanged(nameof(SearchQuery));
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
        UndoImportCommand.RaiseCanExecuteChanged();
        _hasVisibleGames = Games.Count > 0 && !GamesView.IsEmpty;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowWelcomeConnection));
        OnPropertyChanged(nameof(ShowWelcomeImport));
        OnPropertyChanged(nameof(ShowCoverSetupHint));
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
