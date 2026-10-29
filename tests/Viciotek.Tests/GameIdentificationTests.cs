using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Shell;
using Viciotek.Infrastructure.Storage;

namespace Viciotek.Tests;

public sealed class GameIdentificationTests
{
    [Theory]
    [InlineData("RE9.exe", "Resident Evil Requiem")]
    [InlineData("MK.exe", "Mortal Kombat 1")]
    public void AbbreviatedExecutable_UsesSimulatedInstallationFolder_NotAnAbbreviationDictionary(string exe, string title)
    {
        var game = new Game { Name = Path.GetFileNameWithoutExtension(exe), LaunchPath = @"C:\Games\" + title + "\\" + exe };
        new GameIdentityResolver(new Metadata(new(InstallationFolder: title))).IdentifyNewGame(game, exe);
        Assert.Equal(title, game.Name);
        Assert.Equal(exe, game.ExecutableName);
        Assert.Equal("exe", game.ImportSource);
        Assert.Equal(title, game.CoverSearchName);
        Assert.EndsWith(exe, game.LaunchPath);
    }

    [Fact]
    public void ProductName_UsesSimulatedFileVersionInfoBeforeFolder()
    {
        var game = new Game { Name = "MK", LaunchPath = @"C:\Games\InternalCode\MK.exe" };
        new GameIdentityResolver(new Metadata(new(ProductName: "Mortal Kombat 1", InstallationFolder: "InternalCode")))
            .IdentifyNewGame(game, game.LaunchPath);
        Assert.Equal("Mortal Kombat 1", game.Name);
        Assert.Equal(["Mortal Kombat 1", "InternalCode", "MK"], game.IdentityCandidates);
    }

    [Theory]
    [InlineData("Unreal Engine", "UE4Game")]
    [InlineData("Unity Player", "Application")]
    [InlineData("UE5", "Bootstrap")]
    public void GenericVersionMetadata_IsIgnored(string product, string description)
    {
        var game = new Game { Name = "game", LaunchPath = @"C:\Games\Resident Evil Requiem\game.exe" };
        new GameIdentityResolver(new Metadata(new(ProductName: product, FileDescription: description,
            InstallationFolder: "Resident Evil Requiem"))).IdentifyNewGame(game, game.LaunchPath);
        Assert.Equal("Resident Evil Requiem", game.Name);
        Assert.DoesNotContain(product, game.IdentityCandidates);
    }

    [Fact]
    public void DescriptiveShortcut_HasPriorityOverVersion_WithoutChangingLaunchData()
    {
        var game = new Game { Name = "MK", ShortcutPath = @"C:\Desktop\Mortal Kombat 1.lnk", LaunchPath = @"C:\Games\MK.exe",
            Arguments = "--windowed", WorkingDirectory = @"C:\Games" };
        new GameIdentityResolver(new Metadata(new(ProductName: "Other Product"))).IdentifyNewGame(game, game.ShortcutPath);
        Assert.Equal("Mortal Kombat 1", game.Name);
        Assert.Equal("lnk", game.ImportSource);
        Assert.Equal("--windowed", game.Arguments);
        Assert.Equal(@"C:\Games\MK.exe", game.LaunchPath);
        Assert.Equal(@"C:\Games", game.WorkingDirectory);
    }

    [Fact]
    public void AbbreviatedShortcut_DoesNotMaskDescriptiveMetadata()
    {
        var game = new Game { Name = "RE9", ShortcutPath = @"C:\Desktop\RE9.lnk", LaunchPath = @"C:\Games\RE9.exe" };
        new GameIdentityResolver(new Metadata(new(ProductName: "Resident Evil Requiem"))).IdentifyNewGame(game, game.ShortcutPath);
        Assert.Equal("Resident Evil Requiem", game.Name);
    }

    [Fact]
    public void AbbreviatedProductMetadata_DoesNotOverrideCommercialInstallationName()
    {
        var game = new Game { Name = "RE9", LaunchPath = @"C:\Games\RE9.exe" };
        new GameIdentityResolver(new Metadata(new(ProductName: "RE9", FileDescription: "RE9",
            InstallationFolder: "Resident Evil Requiem"))).IdentifyNewGame(game, game.LaunchPath);
        Assert.Equal("Resident Evil Requiem", game.Name);
    }

    [Fact]
    public void NoMetadata_DoesNotInventExpansion()
    {
        var game = new Game { Name = "RE9", LaunchPath = @"C:\RE9.exe" };
        new GameIdentityResolver(new Metadata(new())).IdentifyNewGame(game, game.LaunchPath);
        Assert.Equal("RE9", game.Name);
        Assert.Equal(["RE9"], game.IdentityCandidates);
    }

    [Fact]
    public void RealLocalManifest_RequiresExactInstallationPath_AndSkipsTechnicalDirectories()
    {
        var root = TestHelpers.CreateTempDirectory();
        try
        {
            var steamapps = Path.Combine(root, "steamapps");
            var directory = Path.Combine(steamapps, "common", "InternalCode", "Binaries", "Win64");
            Directory.CreateDirectory(directory);
            var exe = Path.Combine(directory, "RE9.exe");
            File.WriteAllText(exe, "not executed");
            File.WriteAllText(Path.Combine(steamapps, "appmanifest_1145360.acf"), """
                "AppState" { "appid" "1145360" "name" "Hades" "installdir" "InternalCode" }
                """
            );
            var reader = new WindowsGameMetadataReader();
            var game = new Game { Name = "RE9", LaunchPath = exe };
            new GameIdentityResolver(reader).IdentifyNewGame(game, exe);
            Assert.Equal("Hades", game.Name); // Fixture metadata, not a claim that RE9 is Hades.
            Assert.Equal(1145360, game.SteamAppId);
            Assert.Equal("InternalCode", reader.Read(game).InstallationFolder);

            var sibling = Path.Combine(steamapps, "common", "InternalCodeOther", "MK.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(sibling)!);
            File.WriteAllText(sibling, "not executed");
            Assert.Null(reader.Read(new Game { LaunchPath = sibling }).SteamAppId);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("steam://run/1145360", 1145360)]
    [InlineData("steam://rungameid/1145360", 1145360)]
    [InlineData("steam://run/nope", null)]
    [InlineData("steam://run/0", null)]
    [InlineData("https://run/123", null)]
    public void SteamUri_ExtractsOnlyValidAppId(string uri, int? expected) =>
        Assert.Equal(expected, WindowsGameMetadataReader.SteamIdFromUri(uri));

    [Fact]
    public void SteamLauncherArguments_ProvideAppIdWithoutRunningTheLauncher()
    {
        var game = new Game { LaunchPath = @"C:\Steam\steam.exe", Arguments = "-applaunch 1145360 --windowed" };
        Assert.Equal(1145360, new WindowsGameMetadataReader().Read(game).SteamAppId);
        Assert.Equal("-applaunch 1145360 --windowed", game.Arguments);
    }

    [Theory]
    [InlineData(@"C:\Games\Game\Binaries\game.exe", @"C:\Games\Game", true)]
    [InlineData(@"C:\Games\GameOther\game.exe", @"C:\Games\Game", false)]
    public void RegistryMatching_RequiresDirectoryBoundary(string exe, string location, bool expected) =>
        Assert.Equal(expected, WindowsGameMetadataReader.IsInside(exe, location));

    [Fact]
    public async Task KnownSteamAppId_UsesDirectLookup_NoTextQueries()
    {
        var provider = new Provider { SteamGame = new(123, "Hades") };
        var match = await new GameIdentitySearch(provider).FindAsync(new Game { Name = "HadesInternal", SteamAppId = 1145360 }, default);
        Assert.Equal(123, match.Confirmed?.Id);
        Assert.Equal([1145360], provider.SteamIds);
        Assert.Empty(provider.Queries);
    }

    [Fact]
    public async Task FirstQueryEmpty_SecondMetadataCandidateSucceeds()
    {
        var provider = new Provider { Search = query => query == "Mortal Kombat 1" ? [new(42, query)] : [] };
        var match = await new GameIdentitySearch(provider).FindAsync(new Game { Name = "Unknown Product",
            IdentityCandidates = ["Unknown Product", "Mortal Kombat 1", "MK"] }, default);
        Assert.Equal(42, match.Confirmed?.Id);
        Assert.Equal(["Unknown Product", "Mortal Kombat 1"], provider.Queries);
    }

    [Fact]
    public async Task AtMostThreeDistinctQueries_RegardlessOfCandidateCount()
    {
        var provider = new Provider();
        var match = await new GameIdentitySearch(provider).FindAsync(new Game { Name = "First Title",
            IdentityCandidates = ["First Title", "FIRST TITLE", "Second Title", "Third Title", "Fourth Title"] }, default);
        Assert.Null(match.Confirmed);
        Assert.Equal(["First Title", "Second Title", "Third Title"], provider.Queries);
    }

    [Fact]
    public async Task AmbiguousSameName_DoesNotChooseFirst()
    {
        var provider = new Provider { Search = _ => [new(1, "Prey"), new(2, "Prey")] };
        var match = await new GameIdentitySearch(provider).FindAsync(new Game { Name = "Prey" }, default);
        Assert.Null(match.Confirmed);
        Assert.Equal(2, match.Choices.Count);
    }

    [Theory]
    [InlineData("Mortal Kombat 1", "Mortal Kombat 11", false)]
    [InlineData("Hades II", "Hades 2", true)]
    [InlineData("Hades", "Hades II", false)]
    [InlineData("Control Ultimate Edition", "Control", false)]
    [InlineData("RE9", "Resident Evil Requiem", false)]
    [InlineData("MK", "Mortal Kombat 1", false)]
    public async Task Matching_PreservesSequelsAndEditions_DoesNotExpandAbbreviations(string query, string result, bool expected)
    {
        var provider = new Provider { Search = _ => [new(1, result)] };
        var match = await new GameIdentitySearch(provider).FindAsync(new Game { Name = query }, default);
        Assert.Equal(expected, match.Confirmed is not null);
    }

    [Fact]
    public async Task ConfirmedAssociation_IsReusedAfterRealJsonReload_AndLegacyDataSurvives()
    {
        var paths = new AppPaths(TestHelpers.CreateTempDirectory());
        try
        {
            paths.EnsureCreated();
            var game = new Game { Name = "Mi título personalizado", ExecutableName = "MK.exe", LaunchPath = @"C:\Games\MK.exe",
                ImportSource = "lnk", SteamAppId = 123, SteamGridDbGameId = 42, SteamGridDbName = "Mortal Kombat 1",
                CoverSearchName = "Mortal Kombat 1", IsNameCustomized = true, CoverFileName = "my-cover.png",
                IdentityCandidates = ["Mortal Kombat 1"] };
            var store = new LibraryStore(paths);
            await store.SaveAsync([game]);
            var reloaded = Assert.Single(new LibraryStore(paths).Load().Games);
            var provider = new Provider();
            var match = await new GameIdentitySearch(provider).FindAsync(reloaded, default);
            Assert.Equal(42, match.Confirmed?.Id);
            Assert.Empty(provider.Queries);
            Assert.Empty(provider.SteamIds);
            Assert.Equal(game.Name, reloaded.Name);
            Assert.Equal(game.CoverFileName, reloaded.CoverFileName);
            Assert.Equal(game.LaunchPath, reloaded.LaunchPath);
            Assert.Equal(game.ExecutableName, reloaded.ExecutableName);
            Assert.Equal(game.ImportSource, reloaded.ImportSource);
            Assert.Equal(game.CoverSearchName, reloaded.CoverSearchName);
            Assert.Equal(1, new AtomicJsonFile<LibraryDocument>(paths.LibraryFile).Read().Value!.SchemaVersion);

            File.WriteAllText(paths.LibraryFile, """{"schemaVersion":1,"games":[{"name":"Nombre anterior","launchPath":"C:\\MK.exe","coverFileName":"custom.png"}]}""");
            var legacy = Assert.Single(store.Load().Games);
            Assert.Equal("Nombre anterior", legacy.Name);
            Assert.Equal("custom.png", legacy.CoverFileName);
            Assert.Empty(legacy.IdentityCandidates);
            Assert.Null(legacy.SteamGridDbGameId);
        }
        finally { Directory.Delete(paths.Root, true); }
    }

    [Fact]
    public async Task CustomizedName_SearchesOnlyEditedTitle()
    {
        var provider = new Provider();
        await new GameIdentitySearch(provider).FindAsync(new Game { Name = "Mi juego", IsNameCustomized = true,
            SteamAppId = 123, CoverSearchName = "Old title", IdentityCandidates = ["Other title"] }, default);
        Assert.Equal(["Mi juego"], provider.Queries);
        Assert.Empty(provider.SteamIds);
    }

    private sealed class Metadata(LocalGameMetadata result) : IGameMetadataReader
    {
        public LocalGameMetadata Read(Game game) => result;
    }

    private sealed class Provider : ISteamGridDbGames
    {
        public List<string> Queries { get; } = [];
        public List<int> SteamIds { get; } = [];
        public SteamGridGame? SteamGame { get; init; }
        public Func<string, IReadOnlyList<SteamGridGame>> Search { get; init; } = _ => [];
        public Task<SteamGridGame?> GetSteamGameAsync(int appId, CancellationToken cancellationToken)
        { SteamIds.Add(appId); return Task.FromResult(SteamGame); }
        public Task<IReadOnlyList<SteamGridGame>> SearchGamesAsync(string term, CancellationToken cancellationToken)
        { Queries.Add(term); return Task.FromResult(Search(term)); }
    }
}
