using System.Text.Json;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Storage;

namespace Viciotek.Tests;

public sealed class CredentialMigrationTests : IDisposable
{
    private readonly AppPaths _paths = new(TestHelpers.CreateTempDirectory());

    public CredentialMigrationTests() => _paths.EnsureCreated();

    [Fact]
    public void Load_MigratesPlaintextImmediately_AndProtectsTheBackupItCreates()
    {
        File.WriteAllText(_paths.SettingsFile, """{"steamGridDbApiKey":"fixture-legacy-key","windowWidth":1234,"autoFetchCovers":false}""");
        var (settings, warning) = new SettingsStore(_paths).Load();
        Assert.Null(warning);
        Assert.Equal("fixture-legacy-key", settings.SteamGridDbApiKey);
        Assert.Equal(1234, settings.WindowWidth);
        Assert.False(settings.AutoFetchCovers);
        foreach (var file in new[] { _paths.SettingsFile, _paths.SettingsFile + ".bak" })
        {
            var raw = File.ReadAllText(file);
            Assert.DoesNotContain("fixture-legacy-key", raw);
            var document = JsonSerializer.Deserialize<AppSettings>(raw, AppJson.Options)!;
            Assert.Null(document.SteamGridDbApiKey);
            Assert.Equal("fixture-legacy-key", ProtectedSecret.Unprotect(document.ProtectedApiKey));
        }
    }

    [Fact]
    public void Load_ProtectsOlderBackupWithoutReplacingItsPreviousSettings()
    {
        var store = new SettingsStore(_paths);
        store.SaveNow(new AppSettings { SteamGridDbApiKey = "fixture-current-key", WindowWidth = 1400 });
        File.WriteAllText(_paths.SettingsFile + ".bak", """{"steamGridDbApiKey":"fixture-old-key","windowWidth":900,"autoFetchCovers":false}""");
        var (current, warning) = store.Load();
        Assert.Null(warning);
        Assert.Equal("fixture-current-key", current.SteamGridDbApiKey);
        var raw = File.ReadAllText(_paths.SettingsFile + ".bak");
        Assert.DoesNotContain("fixture-old-key", raw);
        var backup = JsonSerializer.Deserialize<AppSettings>(raw, AppJson.Options)!;
        Assert.Equal(900, backup.WindowWidth);
        Assert.False(backup.AutoFetchCovers);
        Assert.Equal("fixture-old-key", ProtectedSecret.Unprotect(backup.ProtectedApiKey));
        Assert.Equal(2, Directory.GetFiles(_paths.Root).Length);
    }

    [Fact]
    public void Load_LockedLegacyFile_ReportsWarningWithoutLosingConfiguration()
    {
        File.WriteAllText(_paths.SettingsFile, """{"steamGridDbApiKey":"fixture-legacy-key","windowWidth":1234}""");
        using var fileLock = File.Open(_paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        var (settings, warning) = new SettingsStore(_paths).Load();
        Assert.NotNull(warning);
        Assert.DoesNotContain("fixture-legacy-key", warning);
        Assert.Equal("fixture-legacy-key", settings.SteamGridDbApiKey);
        Assert.Equal(1234, settings.WindowWidth);
    }

    public void Dispose() => Directory.Delete(_paths.Root, recursive: true);
}
