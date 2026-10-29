using Viciotek.Domain;

namespace Viciotek.Tests;

public sealed class GameNameTests
{
    [Theory]
    [InlineData(@"C:\Games\Hades.exe", "Hades")]
    [InlineData(@"C:\Games\Hades - acceso directo.lnk", "Hades")]
    [InlineData(@"C:\Games\Hades - shortcut.lnk", "Hades")]
    [InlineData(@"C:\Games\Hades (shortcut).lnk", "Hades")]
    [InlineData(@"C:\Games\Hades - link.url", "Hades")]
    [InlineData(@"C:\Games\Hades - shortcuT.lnk", "Hades")]
    public void FromPath_RemovesExtensionAndShortcutSuffixes(string path, string expected) =>
        Assert.Equal(expected, GameName.FromPath(path));

    [Fact]
    public void FromPath_EmptyOrInvalidNameFallsBack()
    {
        Assert.Equal("Juego sin título", GameName.FromPath(@"C:\Games\.exe"));
        Assert.Equal("Juego sin título", GameName.FromPath(string.Empty));
    }
}

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Pokémon", "pokemon")]
    [InlineData("POKÉMON", "pokemon")]
    [InlineData("Red Dead Redemption 2", "dead redemption")]
    [InlineData("NieR: Automata", "automata")]
    public void Matches_IsCaseAndAccentInsensitive(string name, string query) =>
        Assert.True(SearchText.Matches(name, query));

    [Fact]
    public void Matches_EmptyQueryMatchesEverything() =>
        Assert.True(SearchText.Matches("Hades", "  "));

    [Fact]
    public void Matches_DoesNotMatchUnrelatedText() =>
        Assert.False(SearchText.Matches("Hades", "celeste"));
}

public sealed class ProtocolPolicyTests
{
    [Theory]
    [InlineData("steam://run/1245620")]
    [InlineData("STEAM://run/1245620")]
    [InlineData("com.epicgames.launcher://apps/Fortnite?action=launch")]
    [InlineData("uplay://launch/123/0")]
    [InlineData("goggalaxy://openGameView/123")]
    public void TryValidate_AcceptsKnownLauncherSchemes(string value)
    {
        Assert.True(ProtocolPolicy.TryValidate(value, out var uri, out var error));
        Assert.NotNull(uri);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("file:///C:/Windows/System32/notepad.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("steam://user:password@run/1")]
    public void TryValidate_RejectsUnsafeTargets(string value) =>
        Assert.False(ProtocolPolicy.TryValidate(value, out _, out var error), error);

    [Fact]
    public void TryValidate_RejectsControlCharacters() =>
        Assert.False(ProtocolPolicy.TryValidate("steam://run/1\ncalc", out _, out var error));
}

public sealed class GameIdentityTests
{
    [Fact]
    public void DuplicateKey_IsCaseInsensitiveForPaths()
    {
        var first = new Game { LaunchPath = @"C:\Games\Hades.exe", LaunchKind = LaunchKind.Executable };
        var second = new Game { LaunchPath = @"""c:\games\HADES.EXE""", LaunchKind = LaunchKind.Executable };

        Assert.Equal(GameIdentity.CreateDuplicateKey(first), GameIdentity.CreateDuplicateKey(second));
    }

    [Fact]
    public void DuplicateKey_DistinguishesArguments()
    {
        var first = new Game { LaunchPath = @"C:\Games\Hades.exe", Arguments = "-windowed" };
        var second = new Game { LaunchPath = @"C:\Games\Hades.exe" };

        Assert.NotEqual(GameIdentity.CreateDuplicateKey(first), GameIdentity.CreateDuplicateKey(second));
    }

    [Fact]
    public void DuplicateKey_NormalizesUri()
    {
        var first = new Game { LaunchKind = LaunchKind.Uri, LaunchPath = "steam://run/123" };
        var second = new Game { LaunchKind = LaunchKind.Uri, LaunchPath = "STEAM://run/123" };

        Assert.Equal(GameIdentity.CreateDuplicateKey(first), GameIdentity.CreateDuplicateKey(second));
    }

    [Fact]
    public void DuplicateKey_UsesShortcutPathForShortcutKind()
    {
        var first = new Game { LaunchKind = LaunchKind.Shortcut, LaunchPath = @"C:\Games\Hades.lnk" };
        var second = new Game { LaunchKind = LaunchKind.Shortcut, LaunchPath = @"C:\Games\hades.LNK" };

        Assert.Equal(GameIdentity.CreateDuplicateKey(first), GameIdentity.CreateDuplicateKey(second));
    }
}

public sealed class LaunchPlannerTests
{
    [Fact]
    public void CreatePlan_ValidExecutable_UsesDirectMode()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var exe = Path.Combine(directory, "game.exe");
            File.WriteAllText(exe, "stub");
            var game = new Game
            {
                Name = "Game",
                LaunchPath = exe,
                LaunchKind = LaunchKind.Executable,
                Arguments = "--level 2",
                WorkingDirectory = directory
            };

            var result = LaunchPlanner.CreatePlan(game);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Plan);
            Assert.Equal(exe, result.Plan.FileName);
            Assert.Equal("--level 2", result.Plan.Arguments);
            Assert.Equal(directory, result.Plan.WorkingDirectory);
            Assert.Equal(LaunchMode.Direct, result.Plan.Mode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreatePlan_MissingWorkingDirectory_FallsBackToExecutableDirectory()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var exe = Path.Combine(directory, "game.exe");
            File.WriteAllText(exe, "stub");
            var game = new Game
            {
                Name = "Game",
                LaunchPath = exe,
                WorkingDirectory = Path.Combine(directory, "gone")
            };

            var result = LaunchPlanner.CreatePlan(game);

            Assert.True(result.IsValid);
            Assert.Equal(directory, result.Plan!.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreatePlan_RejectsMissingExecutable()
    {
        var game = new Game { Name = "Game", LaunchPath = @"C:\missing\game.exe" };

        var result = LaunchPlanner.CreatePlan(game);

        Assert.False(result.IsValid);
        Assert.Contains("ya no existe", result.Error);
    }

    [Fact]
    public void CreatePlan_RejectsUnsafeExtension()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var script = Path.Combine(directory, "game.cmd");
            File.WriteAllText(script, "echo");
            var game = new Game { Name = "Game", LaunchPath = script };

            var result = LaunchPlanner.CreatePlan(game);

            Assert.False(result.IsValid);
            Assert.Contains(".exe", result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreatePlan_Uri_UsesShellMode()
    {
        var game = new Game { Name = "Steam", LaunchPath = "steam://run/123", LaunchKind = LaunchKind.Uri };

        var result = LaunchPlanner.CreatePlan(game);

        Assert.True(result.IsValid);
        Assert.Equal(LaunchMode.Shell, result.Plan!.Mode);
        Assert.Equal("steam://run/123", result.Plan.FileName);
    }

    [Fact]
    public void CreatePlan_RejectsUnsafeUri()
    {
        var game = new Game { Name = "Bad", LaunchPath = "http://example.com", LaunchKind = LaunchKind.Uri };

        Assert.False(LaunchPlanner.CreatePlan(game).IsValid);
    }

    [Fact]
    public void CreatePlan_ShortcutExistingFile_UsesShellMode()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var shortcut = Path.Combine(directory, "game.lnk");
            File.WriteAllText(shortcut, "stub");
            var game = new Game
            {
                Name = "Game",
                LaunchPath = shortcut,
                LaunchKind = LaunchKind.Shortcut
            };

            var result = LaunchPlanner.CreatePlan(game);

            Assert.True(result.IsValid);
            Assert.Equal(LaunchMode.Shell, result.Plan!.Mode);
            Assert.Equal(shortcut, result.Plan.FileName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed class UrlShortcutReaderTests
{
    [Fact]
    public void TryRead_ParsesAllowedUrlAndRelativeIcon()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var icon = Path.Combine(directory, "icon.ico");
            File.WriteAllBytes(icon, [0]);
            var url = Path.Combine(directory, "Hades.url");
            File.WriteAllLines(url,
            [
                "[InternetShortcut]",
                "URL=steam://run/1145360",
                "IconFile=icon.ico",
                "IconIndex=2"
            ]);

            var success = UrlShortcutReader.TryRead(url, out var info, out var error);

            Assert.True(success, error);
            Assert.NotNull(info);
            Assert.Equal("steam://run/1145360", info.Url);
            Assert.Equal(icon, info.IconFile);
            Assert.Equal(2, info.IconIndex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRead_RejectsHttpUrl()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var url = Path.Combine(directory, "web.url");
            File.WriteAllText(url, "[InternetShortcut]\nURL=https://example.com");

            Assert.False(UrlShortcutReader.TryRead(url, out _, out var error));
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRead_RejectsFileWithoutUrl()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var url = Path.Combine(directory, "empty.url");
            File.WriteAllText(url, "[InternetShortcut]\nIconFile=icon.ico");

            Assert.False(UrlShortcutReader.TryRead(url, out _, out var error));
            Assert.Contains("destino", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
