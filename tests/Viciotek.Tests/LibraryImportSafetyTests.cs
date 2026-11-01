using System.Text.Json;
using System.Windows.Controls;
using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Processes;
using Viciotek.Infrastructure.Storage;
using Viciotek.Presentation;
using Viciotek.Presentation.Controls;
using Viciotek.Presentation.Dialogs;
namespace Viciotek.Tests;
public sealed class LibraryImportSafetyTests
{
    [Theory]
    [InlineData(-1, GamepadMoveDirection.Left, 0)]
    [InlineData(-1, GamepadMoveDirection.Down, 0)]
    [InlineData(0, GamepadMoveDirection.Down, 4)]
    [InlineData(4, GamepadMoveDirection.Up, 0)]
    [InlineData(1, GamepadMoveDirection.Up, 1)]
    [InlineData(3, GamepadMoveDirection.Right, 3)]
    [InlineData(4, GamepadMoveDirection.Left, 4)]
    [InlineData(7, GamepadMoveDirection.Down, 7)]
    public void DirectionalNavigation_UsesGridAndFirstIntent(int index, GamepadMoveDirection direction, int expected)
    {
        Assert.Equal(expected, Viciotek.MainWindow.NavigationIndex(8, index, 4, direction));
        Assert.Equal(-1, Viciotek.MainWindow.NavigationIndex(0, -1, 4, direction));
        Assert.Equal(0, Viciotek.MainWindow.NavigationIndex(1, -1, 1, direction));
    }
    [Fact]
    public void XInput_UsesWindowsNativeLayout()
    {
        Assert.Equal(16, System.Runtime.InteropServices.Marshal.SizeOf<Viciotek.Infrastructure.Input.XInputGamepad.NativeState>());
        Assert.Equal(4, System.Runtime.InteropServices.Marshal.OffsetOf<Viciotek.Infrastructure.Input.XInputGamepad.NativeState>("Buttons").ToInt32());
        Assert.Equal(8, System.Runtime.InteropServices.Marshal.OffsetOf<Viciotek.Infrastructure.Input.XInputGamepad.NativeState>("AxisLeftX").ToInt32());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public Task ManualImport_ImportsExplicitFilesWithoutReview(bool drop)=>WpfTest.RunAsync(async()=>
    {
        await using var f=new Fixture();var exe=f.Exe("Game.exe");f.Dialogs.Files=[exe];
        if(drop)await f.Vm.HandleDroppedFilesAsync([exe]);else await f.Vm.AddGamesFromDialogAsync();
        Assert.Equal(0,f.Dialogs.Reviews);Assert.Single(f.Vm.Games);Assert.True(f.Vm.CanUndoImport);Assert.True(File.Exists(exe));
        Assert.Single(f.Store.Load().Games);
        if(drop)await f.Vm.HandleDroppedFilesAsync([exe]);else await f.Vm.AddGamesFromDialogAsync();
        Assert.Single(f.Vm.Games);Assert.Equal(0,f.Dialogs.Reviews);
    });
    [Fact]
    public Task ManualImport_CancelPickerDoesNotImportAnything()=>WpfTest.RunAsync(async()=>
    {
        await using var f=new Fixture();
        await f.Vm.AddGamesFromDialogAsync();
        Assert.Empty(f.Vm.Games);Assert.False(f.Vm.CanUndoImport);Assert.Equal(0,f.Dialogs.Reviews);
    });
    [Fact]
    public Task Review_PreselectsOnlyHighConfidenceAndNeverApprovesOnItsOwn()
    {
        var high=new DetectedGame("Game","Steam","steam://rungameid/1",LaunchKind.Uri,HighConfidence:true);
        var uncertain=new DetectedGame("Unknown","Manual","unknown.exe");
        Assert.True(new DetectInstalledGamesDialog.Choice(high).IsSelected);
        Assert.False(new DetectInstalledGamesDialog.Choice(uncertain).IsSelected);
        return Task.CompletedTask;
    }
    [Fact]
    public Task DetectedImport_RequiresApproval_OnlyImportsSelectedAndPreservesLaunchDetails()=>WpfTest.RunAsync(async()=>
    {
        await using var f=new Fixture(1);var exe=f.Exe("Game.exe");
        var candidates=new[]{new DetectedGame("Game","GOG",exe,Arguments:"--windowed",WorkingDirectory:f.Paths.Root,HighConfidence:true),new DetectedGame("Other","GOG",f.Exe("Other.exe"),HighConfidence:true)};
        await f.Vm.ImportDetectedGamesAsync(candidates);Assert.Single(f.Vm.Games);
        f.Dialogs.Approval=games=>[games[0]];await f.Vm.ImportDetectedGamesAsync(candidates);
        Assert.Equal(2,f.Vm.Games.Count);var added=f.Vm.Games[1].Model;
        Assert.Equal(exe,added.LaunchPath);Assert.Equal("--windowed",added.Arguments);Assert.Equal(f.Paths.Root,added.WorkingDirectory);Assert.NotNull(added.ImportBatchId);
        await f.Vm.ImportDetectedGamesAsync(candidates[..1]);Assert.Equal(2,f.Vm.Games.Count);
    });
    [Fact]
    public Task BulkRemove_IsConfirmedAndNeverDeletesGamesOrOtherCovers()=>WpfTest.RunAsync(async()=>
    {
        await using var f=new Fixture(3);var originals=f.Vm.Games.Select(c=>c.Model.LaunchPath).ToArray();
        var cover=Path.Combine(f.Paths.CoversDirectory,"manual.png");File.WriteAllText(cover,"fixture");f.Vm.Games[2].Model.CoverFileName="manual.png";
        await f.Store.SaveAsync(f.Vm.Games.Select(c=>c.Model).ToArray());
        f.Vm.SelectedGames.Add(f.Vm.Games[0]);f.Vm.SelectedGames.Add(f.Vm.Games[1]);
        await f.Vm.RemoveSelectionAsync();Assert.Equal(3,f.Vm.Games.Count);Assert.Equal(2,f.Dialogs.LastCount);
        f.Dialogs.Confirm=true;await f.Vm.RemoveAsync(f.Vm.Games[0]);
        Assert.Single(f.Vm.Games);Assert.All(originals,path=>Assert.True(File.Exists(path)));Assert.True(File.Exists(cover));
        Assert.Single(f.Store.Load().Games);
    });
    [Fact]
    public Task UndoLastBatch_SurvivesRestart_AndDoesNotFallBackToOlderBatches()=>WpfTest.RunAsync(async()=>
    {
        await using var f=new Fixture(1);f.Dialogs.Approval=games=>games;f.Dialogs.Confirm=true;
        var first=f.Exe("First.exe");var second=f.Exe("Second.exe");var last=f.Exe("Last.exe");
        await f.Vm.ImportFilesAsync([first,second]);var earlier=f.Vm.Games.Last().Model.ImportBatchId;
        await f.Vm.ImportFilesAsync([last]);var latest=f.Vm.Games.Last().Model.ImportBatchId;Assert.NotEqual(earlier,latest);
        await f.Reload();Assert.True(f.Vm.CanUndoImport);f.Dialogs.Confirm=false;
        await f.Vm.UndoLastImportAsync();Assert.Equal(4,f.Vm.Games.Count);
        f.Dialogs.Confirm=true;await f.Vm.UndoLastImportAsync();Assert.Equal(3,f.Vm.Games.Count);Assert.False(f.Vm.CanUndoImport);
        Assert.DoesNotContain(f.Vm.Games,c=>c.Model.LaunchPath==last);Assert.True(File.Exists(last));
        await f.Reload();Assert.False(f.Vm.CanUndoImport);await f.Vm.UndoLastImportAsync();Assert.Equal(3,f.Vm.Games.Count);
        using var json=JsonDocument.Parse(File.ReadAllText(f.Paths.LibraryFile));Assert.Equal(1,json.RootElement.GetProperty("schemaVersion").GetInt32());
    });
    [Fact]
    public Task FailedPersistence_DoesNotAdoptImportBatchOrBulkRemoval()=>WpfTest.RunAsync(async()=>
    {
        await using var f=new Fixture(2);var before=File.ReadAllText(f.Paths.LibraryFile);f.Dialogs.Approval=games=>games;f.Dialogs.Confirm=true;f.FailWrites=true;
        await f.Vm.ImportFilesAsync([f.Exe("New.exe")]);Assert.Equal(2,f.Vm.Games.Count);Assert.False(f.Vm.CanUndoImport);
        foreach(var card in f.Vm.Games)f.Vm.SelectedGames.Add(card);
        await f.Vm.RemoveSelectionAsync();Assert.Equal(2,f.Vm.Games.Count);Assert.Equal(before,File.ReadAllText(f.Paths.LibraryFile));
    });
    [Fact]
    public Task SelectAll_UsesOnlyVisibleItems_AndDetectedSteamLaunchUsesProtocol()=>WpfTest.RunAsync(async()=>
    {
        await using var f=new Fixture(3);var gallery=new GameGallery{SelectionMode=SelectionMode.Extended,ItemsSource=f.Vm.GamesView};
        f.Vm.SearchQuery="Existing 1";gallery.SelectAll();Assert.Single(gallery.SelectedItems);
        f.Vm.SearchQuery="";gallery.SelectAll();Assert.Equal(3,gallery.SelectedItems.Count);
        f.Dialogs.Approval=games=>games;
        await f.Vm.ImportDetectedGamesAsync([new DetectedGame("Steam Game","Steam","steam://rungameid/123",LaunchKind.Uri,PlatformId:"123",HighConfidence:true)]);
        var card=f.Vm.Games.Last();await f.Vm.PlayAsync(card);Assert.Equal(123,card.Model.SteamAppId);Assert.Equal("steam://rungameid/123",f.Launcher.Last!.LaunchPath);
        Assert.Equal(LaunchMode.Shell,LaunchPlanner.CreatePlan(f.Launcher.Last).Plan!.Mode);
    });
    [Theory][InlineData("Steam")][InlineData("Epic Games")]
    public Task ProtocolDetection_DoesNotDuplicateLegacyExecutableEntries(string platform)=>WpfTest.RunAsync(async()=>
    {
        await using var f=new Fixture(1);f.Dialogs.Approval=games=>games;
        var previous=f.Vm.Games[0].Model.Clone();
        await f.Vm.ImportDetectedGamesAsync([new DetectedGame(previous.Name,platform,
            platform=="Steam"?"steam://rungameid/100":"com.epicgames.launcher://apps/ns:item:game?action=launch",
            LaunchKind.Uri,WorkingDirectory:f.Paths.Root,PlatformId:"100",HighConfidence:true,ExecutablePath:platform=="Steam"?null:previous.LaunchPath)]);
        Assert.Single(f.Vm.Games);Assert.Equal(previous.Id,f.Vm.Games[0].Model.Id);Assert.Equal(previous.LaunchPath,f.Vm.Games[0].Model.LaunchPath);
        Assert.False(f.Vm.CanUndoImport);
    });
    private sealed class Fixture:IAsyncDisposable
    {
        public AppPaths Paths{get;}=new(TestHelpers.CreateTempDirectory());public LibraryStore Store{get;private set;}public MainViewModel Vm{get;private set;}public Dialogs Dialogs{get;}=new();public Launcher Launcher{get;}=new();public bool FailWrites;
        public Fixture(int count=0){Paths.EnsureCreated();Store=CreateStore();var games=Enumerable.Range(0,count).Select(i=>new Game{Name="Existing "+i,LaunchPath=Exe("Existing"+i+".exe")}).ToArray();Store.SaveNow(games);Vm=CreateVm(games);}
        private LibraryStore CreateStore()=>new(Paths,async(document,token)=>{if(FailWrites)throw new IOException("simulated");await new AtomicJsonFile<LibraryDocument>(Paths.LibraryFile).WriteAsync(document,token);});
        private MainViewModel CreateVm(IReadOnlyList<Game> games)=>new(games,new AppSettings{AutoFetchCovers=false},Store,new SettingsStore(Paths),new GameImporter(new NoShortcuts()),Launcher,new CoverStore(Paths),new SteamGridDbClient(null),Dialogs,null);
        public string Exe(string name){var path=Path.Combine(Paths.Root,name);File.WriteAllText(path,"fixture, never executed");return path;}
        public async Task Reload(){await Vm.PrepareCloseAsync();Store=CreateStore();var loaded=Store.Load();Vm=CreateVm(loaded.Games);}
        public async ValueTask DisposeAsync(){await Vm.PrepareCloseAsync();Directory.Delete(Paths.Root,true);}
    }
    private sealed class NoShortcuts:IShortcutResolver{public ShortcutInfo Resolve(string path)=>throw new NotSupportedException();}
    private sealed class Launcher:IGameLauncher{public Game? Last;public Task<LaunchResult> LaunchAsync(Game game){Last=game.Clone();return Task.FromResult(LaunchResult.Ok());}}
    private sealed class Dialogs:IDialogService
    {
        public Func<IReadOnlyList<DetectedGame>,IReadOnlyList<DetectedGame>?>? Approval;public int Reviews;public bool Confirm;public int LastCount;public string[]? Files;
        public IReadOnlyList<DetectedGame>? ReviewImport(IReadOnlyList<DetectedGame> games){Reviews++;return Approval?.Invoke(games);}
        public bool ConfirmRemoveMany(int count){LastCount=count;return Confirm;}public bool ConfirmUndoImport(int count)=>ConfirmRemoveMany(count);public bool ConfirmRemove(string name)=>ConfirmRemoveMany(1);
        public string[]? PickGameFiles()=>Files;public string? PickCoverFile()=>null;public GameEditResult? EditGame(Game game)=>null;public AppSettings? EditPreferences(AppSettings settings)=>null;
        public Task PickCoverFromGalleryAsync(Game game,SteamGridDbClient client,Func<CoverSelection,CancellationToken,Task<bool>> apply,bool manualFallback,CancellationToken token)=>Task.CompletedTask;
    }
}
