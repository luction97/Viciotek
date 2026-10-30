using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Storage;

namespace Viciotek.Tests;

public sealed class DisconnectStorageTests
{
    [Fact]
    public async Task Disconnect_WhenRecoveryIsLocked_DoesNotClaimSuccessOrChangeMain()
    {
        var root = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root); paths.EnsureCreated();
            var store = new SettingsStore(paths);
            var settings = new AppSettings { SteamGridDbApiKey = "fixture-secret-key" };
            await store.SaveAsync(settings); await store.SaveAsync(settings);
            var before = await File.ReadAllTextAsync(paths.SettingsFile);
            using (var locked = File.Open(paths.SettingsFile + ".bak", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await Assert.ThrowsAsync<IOException>(() => store.DisconnectAsync(settings));
                Assert.Equal(before, await File.ReadAllTextAsync(paths.SettingsFile));
            }
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Disconnect_ReplacesCorruptRecoveryWithoutRetainingItsContents()
    {
        var root = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root); paths.EnsureCreated();
            var store = new SettingsStore(paths);
            var settings = new AppSettings { SteamGridDbApiKey = "fixture-secret-key" };
            await store.SaveAsync(settings);
            await File.WriteAllTextAsync(paths.SettingsFile + ".bak", "broken json fixture-secret-key");
            await store.DisconnectAsync(settings);
            Assert.DoesNotContain("fixture-secret-key", await File.ReadAllTextAsync(paths.SettingsFile + ".bak"));
            Assert.Null(store.Load().Settings.SteamGridDbApiKey);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Disconnect_RemovesCredentialFromMainAndRecovery_PreservesOtherPreferences()
    {
        var root = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root); paths.EnsureCreated();
            var store = new SettingsStore(paths);
            var settings = new AppSettings { SteamGridDbApiKey = "fixture-secret-key", Language = "en", CoverSizePreset = 0, AnimationsEnabled = false };
            await store.SaveAsync(settings); await store.SaveAsync(settings);
            Assert.DoesNotContain("fixture-secret-key", await File.ReadAllTextAsync(paths.SettingsFile));
            await store.DisconnectAsync(settings);
            var saved = store.Load().Settings;
            Assert.Null(saved.SteamGridDbApiKey); Assert.Null(saved.ProtectedApiKey);
            Assert.False(saved.AutoFetchCovers); Assert.False(saved.AnimationsEnabled);
            Assert.Equal("en", saved.Language); Assert.Equal(0, saved.CoverSizePreset);
            File.Delete(paths.SettingsFile);
            var recovered = store.Load().Settings;
            Assert.Null(recovered.SteamGridDbApiKey); Assert.Null(recovered.ProtectedApiKey);
            Assert.False(recovered.AutoFetchCovers);
            Assert.False(File.Exists(paths.SettingsFile + ".bak.bak"));
        }
        finally { Directory.Delete(root, true); }
    }
}
