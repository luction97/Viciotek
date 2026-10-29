using System.Text.Json;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Storage;

namespace Viciotek.Tests;

public sealed class CoverSizeSettingsTests
{
    [Theory]
    [InlineData(0, 200)]
    [InlineData(1, 240)]
    [InlineData(2, 280)]
    [InlineData(3, 320)]
    public async Task Preset_RoundTripsWithoutChangingLibrarySchema(int preset, double width)
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var paths = new AppPaths(directory);
            paths.EnsureCreated();
            var store = new SettingsStore(paths);
            await store.SaveAsync(new AppSettings { CoverSizePreset = preset });
            var loaded = store.Load().Settings;
            Assert.Equal(preset, loaded.CoverSizePreset);
            Assert.Equal(width, AppSettings.CoverWidthFor(loaded.CoverSizePreset));
            Assert.False(File.Exists(paths.LibraryFile));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void LegacyAndInvalidSettings_HaveSafePresetDefaults()
    {
        Assert.Equal(2, JsonSerializer.Deserialize<AppSettings>("{}")!.CoverSizePreset);
        Assert.Equal(0, new AppSettings { CoverSizePreset = -1 }.CoverSizePreset);
        Assert.Equal(3, new AppSettings { CoverSizePreset = int.MaxValue }.CoverSizePreset);
    }
}
