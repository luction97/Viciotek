using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Viciotek.Localization;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Storage;
namespace Viciotek.Tests;
[CollectionDefinition("Language", DisableParallelization = true)]
public sealed class LanguageCollection;
[Collection("Language")]
public sealed class LocalizationTests
{
    [Theory]
    [InlineData("es-AR", "es")][InlineData("es-ES", "es")][InlineData("es-MX", "es")]
    [InlineData("es-419", "es")][InlineData("en-US", "en")][InlineData("en-GB", "en")]
    [InlineData("en-AU", "en")][InlineData("fr-FR", "en")]
    public void SystemLanguage_UsesFamily(string culture, string expected) =>
        Assert.Equal(expected, Language.Resolve(null, CultureInfo.GetCultureInfo(culture)));
    [Fact]
    public void ExplicitChoice_OverridesSystemAndUnknownChoiceUsesSystem()
    {
        Assert.Equal("es", Language.Resolve("es", CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal("en", Language.Resolve("en", CultureInfo.GetCultureInfo("es-MX")));
        Assert.Equal("es", Language.Resolve("invalid", CultureInfo.GetCultureInfo("es-ES")));
    }
    [Fact]
    public Task ChangingLanguage_RefreshesExistingBindingWithoutChangingRegionalFormats() => WpfTest.RunAsync(async () =>
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            Language.Use("es");
            var text = new TextBlock();
            text.SetBinding(TextBlock.TextProperty, new Binding { Source = Language.Current, Path = new PropertyPath("[Settings]") });
            Assert.Equal("Configuración", text.Text);
            Language.Use("en");
            await WpfTest.WaitUntilAsync(() => text.Text == "Settings");
            Assert.Same(originalCulture, CultureInfo.CurrentCulture);
            Assert.Contains("Example", Language.Get("0WillBeRemovedFromYourLibrary", "Example"));
        }
        finally { Language.Use("es"); }
    });
    [Theory]
    [InlineData(null)][InlineData("es")][InlineData("en")]
    public async Task Preference_RoundTripsAndCloneKeepsChoice(string? choice)
    {
        var root = TestHelpers.CreateTempDirectory();
        try
        {
            var store = new SettingsStore(new AppPaths(root));
            var settings = new AppSettings { Language = choice };
            Assert.Equal(choice, settings.Clone().Language);
            await store.SaveAsync(settings);
            Assert.Equal(choice, store.Load().Settings.Language);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false, false, "en")]
    [InlineData(true, false, "es")]
    [InlineData(false, true, "es")]
    public Task SaveCancelAndFailure_ApplyLanguageOnlyAfterPersistence(bool cancel, bool fail, string expected) => WpfTest.RunAsync(async () =>
    {
        var root = TestHelpers.CreateTempDirectory();
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        var store = new SettingsStore(paths);
        var settings = new AppSettings { Language = "es" };
        await store.SaveAsync(settings);
        if (fail) { File.Delete(paths.SettingsFile); Directory.CreateDirectory(paths.SettingsFile); }
        using var client = new Viciotek.Infrastructure.Covers.SteamGridDbClient(null);
        var vm = new Viciotek.Presentation.MainViewModel([], settings, new LibraryStore(paths), store,
            new Viciotek.Application.GameImporter(new Viciotek.Infrastructure.Shell.ShellShortcutResolver()),
            new Viciotek.Infrastructure.Processes.GameLauncher(), new Viciotek.Infrastructure.Imaging.CoverStore(paths),
            client, new LanguageDialogs(cancel), null);
        try
        {
            Language.Use("es");
            await vm.OpenPreferencesAsync();
            Assert.Equal(expected, Language.Code);
            Assert.Equal(expected, settings.Language);
            if (!fail) Assert.Equal(expected, store.Load().Settings.Language);
        }
        finally { await vm.PrepareCloseAsync(); Language.Use("es"); Directory.Delete(root, true); }
    });
    private sealed class LanguageDialogs(bool cancel) : Viciotek.Presentation.Dialogs.IDialogService
    {
        public string[]? PickGameFiles() => null;
        public string? PickCoverFile() => null;
        public Viciotek.Presentation.Dialogs.GameEditResult? EditGame(Viciotek.Domain.Game game) => null;
        public bool ConfirmRemove(string name) => false;
        public AppSettings? EditPreferences(AppSettings settings) { settings.Language = "en"; return cancel ? null : settings; }
        public Task PickCoverFromGalleryAsync(Viciotek.Domain.Game game, Viciotek.Infrastructure.Covers.SteamGridDbClient client,
            Func<Viciotek.Presentation.CoverSelection, CancellationToken, Task<bool>> apply, bool fallback, CancellationToken token) => Task.CompletedTask;
    }

    [Fact]
    public void BothCatalogs_HaveMatchingKeysAndPlaceholders()
    {
        var manager = new ResourceManager("Viciotek.Localization.Texts", typeof(Language).Assembly);
        var es = manager.GetResourceSet(CultureInfo.GetCultureInfo("es"), true, true)!;
        var en = manager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)!;
        var count = 0;
        foreach (System.Collections.DictionaryEntry entry in es)
        {
            var translated = en.GetString((string)entry.Key);
            Assert.False(string.IsNullOrWhiteSpace(translated), (string)entry.Key);
            var sourceTokens = System.Text.RegularExpressions.Regex.Matches((string)entry.Value!, @"\{\d+\}").Select(m => m.Value).Order();
            var translatedTokens = System.Text.RegularExpressions.Regex.Matches(translated!, @"\{\d+\}").Select(m => m.Value).Order();
            Assert.Equal(sourceTokens, translatedTokens);
            count++;
        }
        Assert.True(count >= 200);
    }
}
