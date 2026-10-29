using System.Windows.Media;
using Viciotek.Domain;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Presentation.Dialogs;

namespace Viciotek.Presentation;

public interface IGameCardActions
{
    bool CanFindCover { get; }
    Task PlayAsync(GameCardViewModel card);
    Task ChangeCoverAsync(GameCardViewModel card);
    Task FindCoverAsync(GameCardViewModel card);
    Task SearchCoverAsync(GameCardViewModel card);
    Task EditAsync(GameCardViewModel card);
    Task RemoveAsync(GameCardViewModel card);
}

public sealed class GameCardViewModel : ObservableObject
{
    private static readonly (Color Start, Color End)[] FallbackPalettes =
    [
        (Color.FromRgb(16, 42, 43), Color.FromRgb(8, 16, 18)),
        (Color.FromRgb(43, 20, 24), Color.FromRgb(16, 10, 12)),
        (Color.FromRgb(23, 27, 48), Color.FromRgb(9, 11, 18)),
        (Color.FromRgb(27, 36, 24), Color.FromRgb(10, 15, 10)),
        (Color.FromRgb(45, 31, 22), Color.FromRgb(18, 12, 9)),
        (Color.FromRgb(26, 31, 38), Color.FromRgb(9, 12, 16)),
        (Color.FromRgb(37, 22, 41), Color.FromRgb(14, 9, 16)),
        (Color.FromRgb(20, 31, 43), Color.FromRgb(8, 12, 17)),
        (Color.FromRgb(40, 35, 24), Color.FromRgb(16, 14, 9)),
        (Color.FromRgb(31, 26, 36), Color.FromRgb(12, 10, 14))
    ];

    private readonly IGameCardActions _actions;
    private readonly CoverStore _covers;
    private bool _isBusy;
    private bool _isFindingCover;
    private bool _isLaunching;
    private bool _isCoverDropTarget;
    private string _busyLabel = "Actualizando…";
    private Brush? _fallbackBrush;
    private string? _monogram;
    private string? _searchKey;

    public GameCardViewModel(Game model, IGameCardActions actions, CoverStore covers)
    {
        Model = model;
        _actions = actions;
        _covers = covers;

        PlayCommand = new AsyncRelayCommand(() => _actions.PlayAsync(this), CanInteract);
        ChangeCoverCommand = new AsyncRelayCommand(() => _actions.ChangeCoverAsync(this), CanInteract);
        FindCoverCommand = new AsyncRelayCommand(() => _actions.FindCoverAsync(this), CanInteract);
        SearchCoverCommand = new AsyncRelayCommand(() => _actions.SearchCoverAsync(this), CanInteract);
        EditCommand = new AsyncRelayCommand(() => _actions.EditAsync(this), CanInteract);
        RemoveCommand = new AsyncRelayCommand(() => _actions.RemoveAsync(this), CanInteract);
    }

    public Game Model { get; }
    public string Name => Model.Name;
    public string? CoverVersion => Model.CoverFileName;
    public string? CoverPath => string.IsNullOrWhiteSpace(Model.CoverFileName)
        ? null
        : _covers.ThumbnailPathFor(Model);
    public string? IconPath => string.IsNullOrWhiteSpace(Model.CoverFileName) ? Model.IconPath : null;
    public Brush FallbackBrush => _fallbackBrush ??= CreateFallbackBrush(Model.Id);
    public string Monogram => _monogram ??= CreateMonogram(Name);
    public string SearchKey => _searchKey ??= SearchText.Normalize(Name);
    public bool CanFindCover => _actions.CanFindCover;
    public bool CanAutomaticallyFindCover => CanFindCover && Model.CoverFileName is null;

    public AsyncRelayCommand PlayCommand { get; }
    public AsyncRelayCommand ChangeCoverCommand { get; }
    public AsyncRelayCommand FindCoverCommand { get; }
    public AsyncRelayCommand SearchCoverCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(IsWorking));
                OnPropertyChanged(nameof(ActivityLabel));
            }
        }
    }

    public bool IsFindingCover
    {
        get => _isFindingCover;
        set
        {
            if (!SetProperty(ref _isFindingCover, value)) return;
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(ActivityLabel));
        }
    }

    public bool IsWorking => IsBusy || IsFindingCover;
    public string ActivityLabel => IsBusy ? BusyLabel : "Buscando portada…";

    public string BusyLabel
    {
        get => _busyLabel;
        set { if (SetProperty(ref _busyLabel, value)) OnPropertyChanged(nameof(ActivityLabel)); }
    }

    public bool IsLaunching
    {
        get => _isLaunching;
        set
        {
            if (SetProperty(ref _isLaunching, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsCoverDropTarget
    {
        get => _isCoverDropTarget;
        set => SetProperty(ref _isCoverDropTarget, value);
    }

    public void RefreshCover()
    {
        OnPropertyChanged(nameof(CanAutomaticallyFindCover));
        OnPropertyChanged(nameof(CoverVersion));
        OnPropertyChanged(nameof(CoverPath));
        OnPropertyChanged(nameof(IconPath));
    }

    public void RefreshDetails()
    {
        _monogram = null;
        _searchKey = null;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Monogram));
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(CoverVersion));
        OnPropertyChanged(nameof(CoverPath));
    }

    public void RefreshCoverAvailability()
    {
        OnPropertyChanged(nameof(CanFindCover));
        OnPropertyChanged(nameof(CanAutomaticallyFindCover));
    }

    public void SetBusy(string label)
    {
        BusyLabel = label;
        IsBusy = true;
    }

    private bool CanInteract() => !IsBusy && !IsLaunching;

    private void RaiseCommandStates()
    {
        PlayCommand.RaiseCanExecuteChanged();
        ChangeCoverCommand.RaiseCanExecuteChanged();
        FindCoverCommand.RaiseCanExecuteChanged();
        SearchCoverCommand.RaiseCanExecuteChanged();
        EditCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
    }

    private static Brush CreateFallbackBrush(Guid id)
    {
        var bytes = id.ToByteArray();
        var palette = FallbackPalettes[(bytes[0] ^ bytes[3] ^ bytes[7]) % FallbackPalettes.Length];
        var brush = new LinearGradientBrush(palette.Start, palette.End, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static string CreateMonogram(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
        {
            return (FirstLetter(words[0]) + FirstLetter(words[1])).ToUpperInvariant();
        }

        var trimmed = name.Trim();
        return trimmed.Length switch
        {
            0 => "?",
            1 => trimmed.ToUpperInvariant(),
            _ => trimmed[..2].ToUpperInvariant()
        };
    }

    private static string FirstLetter(string value) =>
        value.Length == 0 ? string.Empty : value[..1];
}
