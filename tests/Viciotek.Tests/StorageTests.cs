using Viciotek.Domain;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Storage;

namespace Viciotek.Tests;

public sealed class AtomicJsonFileTests : IDisposable
{
    private readonly string _directory = TestHelpers.CreateTempDirectory();
    private readonly string _path;

    public AtomicJsonFileTests()
    {
        _path = Path.Combine(_directory, "data.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class Payload
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    [Fact]
    public void WriteNow_ThenRead_RoundTrips()
    {
        var file = new AtomicJsonFile<Payload>(_path);
        file.WriteNow(new Payload { Name = "Viciotek", Value = 7 });

        var result = file.Read();

        Assert.Null(result.Warning);
        Assert.NotNull(result.Value);
        Assert.Equal("Viciotek", result.Value.Name);
        Assert.Equal(7, result.Value.Value);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void WriteNow_Twice_CreatesBackup()
    {
        var file = new AtomicJsonFile<Payload>(_path);
        file.WriteNow(new Payload { Name = "first", Value = 1 });
        file.WriteNow(new Payload { Name = "second", Value = 2 });

        Assert.True(File.Exists(_path + ".bak"));
        Assert.Equal("second", file.Read().Value!.Name);
    }

    [Fact]
    public void Read_CorruptMain_RecoversBackupAndPreservesCorruptFile()
    {
        var file = new AtomicJsonFile<Payload>(_path);
        file.WriteNow(new Payload { Name = "first", Value = 1 });
        file.WriteNow(new Payload { Name = "second", Value = 2 });
        File.WriteAllText(_path, "{ this is not json");

        var result = file.Read();

        Assert.NotNull(result.Value);
        Assert.Equal("first", result.Value.Name);
        Assert.NotNull(result.Warning);
        Assert.NotEmpty(Directory.GetFiles(_directory, "data.corrupt-*.json"));
    }

    [Fact]
    public void Read_CorruptWithoutBackup_ReturnsWarning()
    {
        File.WriteAllText(_path, "{ broken");
        var file = new AtomicJsonFile<Payload>(_path);

        var result = file.Read();

        Assert.Null(result.Value);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Read_EmptyFile_ReturnsWarning()
    {
        File.WriteAllText(_path, string.Empty);
        var file = new AtomicJsonFile<Payload>(_path);

        var result = file.Read();

        Assert.Null(result.Value);
        Assert.NotNull(result.Warning);
    }
}

public sealed class LibraryStoreTests : IDisposable
{
    private readonly string _directory = TestHelpers.CreateTempDirectory();
    private readonly AppPaths _paths;

    public LibraryStoreTests()
    {
        _paths = new AppPaths(_directory);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void SaveNow_ThenLoad_SanitizesAndSorts()
    {
        var duplicateId = Guid.NewGuid();
        var games = new List<Game>
        {
            new()
            {
                Id = duplicateId,
                Name = "  Second  ",
                LaunchPath = @"C:\Games\Second.exe",
                CoverFileName = @"..\..\covers\cover.jpg",
                SortOrder = 9
            },
            new()
            {
                Id = duplicateId,
                Name = string.Empty,
                LaunchPath = @"C:\Games\First.exe",
                SortOrder = 2
            }
        };

        var store = new LibraryStore(_paths);
        store.SaveNow(games);
        var (loaded, warning) = store.Load();

        Assert.Null(warning);
        Assert.Equal(2, loaded.Count);
        Assert.NotEqual(loaded[0].Id, loaded[1].Id);
        Assert.Equal("First", loaded[0].Name);
        Assert.Equal("Second", loaded[1].Name);
        Assert.Equal("cover.jpg", loaded[1].CoverFileName);
        Assert.Equal([0L, 1L], loaded.Select(game => game.SortOrder).ToArray());
    }

    [Fact]
    public void Load_RecoversFromCorruptLibrary()
    {
        var store = new LibraryStore(_paths);
        store.SaveNow([new Game { Name = "Original", LaunchPath = @"C:\game.exe" }]);
        store.SaveNow([new Game { Name = "Changed", LaunchPath = @"C:\game2.exe" }]);
        File.WriteAllText(_paths.LibraryFile, "not json");

        var (loaded, warning) = store.Load();

        Assert.NotNull(warning);
        Assert.Single(loaded);
        Assert.Equal("Original", loaded[0].Name);
    }
}

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = TestHelpers.CreateTempDirectory();
    private readonly AppPaths _paths;

    public SettingsStoreTests()
    {
        _paths = new AppPaths(_directory);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_ClampsWindowSizeAndTrimsApiKey()
    {
        var store = new SettingsStore(_paths);
        store.SaveNow(new AppSettings
        {
            WindowWidth = 10,
            WindowHeight = 90_000,
            SteamGridDbApiKey = "  secret  "
        });

        var (settings, warning) = store.Load();

        Assert.Null(warning);
        Assert.Equal(720, settings.WindowWidth);
        Assert.Equal(10_000, settings.WindowHeight);
        Assert.Equal("secret", settings.SteamGridDbApiKey);
    }

    [Fact]
    public void Load_BlankApiKeyBecomesNull()
    {
        var store = new SettingsStore(_paths);
        store.SaveNow(new AppSettings { SteamGridDbApiKey = "   " });

        Assert.Null(store.Load().Settings.SteamGridDbApiKey);
    }

    [Fact]
    public void AnimationsSetting_DefaultsTrueAndRoundTrips()
    {
        var store = new SettingsStore(_paths);

        Assert.True(store.Load().Settings.AnimationsEnabled);

        store.SaveNow(new AppSettings { AnimationsEnabled = false });

        Assert.False(store.Load().Settings.AnimationsEnabled);
    }
}
