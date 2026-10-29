using Viciotek.Application;
using Viciotek.Domain;

namespace Viciotek.Tests;

public sealed class GameImporterTests
{
    private sealed class FakeShortcutResolver : IShortcutResolver
    {
        public Dictionary<string, ShortcutInfo> Shortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ShortcutInfo? Fallback { get; set; }

        public ShortcutInfo Resolve(string shortcutPath) =>
            Shortcuts.TryGetValue(shortcutPath, out var info)
                ? info
                : Fallback ?? new ShortcutInfo(null, null, null);
    }

    [Fact]
    public void Import_AddsExecutableAndSkipsDuplicates()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var exe = Path.Combine(directory, "Hades.exe");
            File.WriteAllText(exe, "stub");
            var importer = new GameImporter(new FakeShortcutResolver());

            var first = importer.Import([exe], []);
            var second = importer.Import([exe], first.Added);

            Assert.Single(first.Added);
            Assert.Empty(first.Problems);
            Assert.Empty(second.Added);
            Assert.Equal(1, second.DuplicateCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_ShortcutWithArgumentsIsNotDuplicateOfBareExecutable()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var exe = Path.Combine(directory, "Hades.exe");
            var shortcut = Path.Combine(directory, "Hades - acceso directo.lnk");
            File.WriteAllText(exe, "stub");
            File.WriteAllText(shortcut, "stub");
            var resolver = new FakeShortcutResolver();
            resolver.Shortcuts[shortcut] = new ShortcutInfo(exe, "--windowed", directory);
            var importer = new GameImporter(resolver);

            var existing = new List<Game>
            {
                new()
                {
                    Name = "Hades",
                    LaunchPath = exe,
                    LaunchKind = LaunchKind.Executable
                }
            };

            var outcome = importer.Import([shortcut], existing);

            Assert.Single(outcome.Added);
            Assert.Equal(0, outcome.DuplicateCount);
            Assert.Equal("--windowed", outcome.Added[0].Arguments);
            Assert.Equal(exe, outcome.Added[0].LaunchPath);
            Assert.Equal(LaunchKind.Executable, outcome.Added[0].LaunchKind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_RejectsShortcutToUnsafeTarget()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var shortcut = Path.Combine(directory, "Game.lnk");
            File.WriteAllText(shortcut, "stub");
            var resolver = new FakeShortcutResolver();
            resolver.Shortcuts[shortcut] = new ShortcutInfo(Path.Combine(directory, "run.bat"), null, null);
            var importer = new GameImporter(resolver);

            var outcome = importer.Import([shortcut], []);

            Assert.Empty(outcome.Added);
            Assert.Single(outcome.Problems);
            Assert.Contains(".exe", outcome.Problems[0].Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_ReportsUnsupportedAndMissingEntries()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var text = Path.Combine(directory, "readme.txt");
            File.WriteAllText(text, "hello");
            var importer = new GameImporter(new FakeShortcutResolver());

            var outcome = importer.Import(
                [text, Path.Combine(directory, "missing.exe"), directory],
                []);

            Assert.Empty(outcome.Added);
            Assert.Equal(3, outcome.Problems.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_UrlShortcutIsStoredAsUri()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var url = Path.Combine(directory, "Hades.url");
            File.WriteAllText(url, "[InternetShortcut]\nURL=steam://run/1145360");
            var importer = new GameImporter(new FakeShortcutResolver());

            var outcome = importer.Import([url], []);

            Assert.Single(outcome.Added);
            Assert.Equal(LaunchKind.Uri, outcome.Added[0].LaunchKind);
            Assert.Equal("steam://run/1145360", outcome.Added[0].LaunchPath);
            Assert.Equal("Hades", outcome.Added[0].Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_AssignsSequentialSortOrder()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var first = Path.Combine(directory, "First.exe");
            var second = Path.Combine(directory, "Second.exe");
            File.WriteAllText(first, "stub");
            File.WriteAllText(second, "stub");
            var importer = new GameImporter(new FakeShortcutResolver());
            var existing = new List<Game>
            {
                new() { LaunchPath = Path.Combine(directory, "Existing.exe"), SortOrder = 7 }
            };

            var outcome = importer.Import([first, second], existing);

            Assert.Equal([8L, 9L], outcome.Added.Select(game => game.SortOrder).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed class GameEditValidatorTests
{
    [Fact]
    public void TryCreate_ValidExecutableKeepsArgumentsAndWorkingDirectory()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var exe = Path.Combine(directory, "game.exe");
            File.WriteAllText(exe, "stub");

            var success = GameEditValidator.TryCreate(
                "  My Game  ",
                exe,
                "  --level 1  ",
                directory,
                out var values,
                out var error);

            Assert.True(success, error);
            Assert.NotNull(values);
            Assert.Equal("My Game", values.Name);
            Assert.Equal(exe, values.Target);
            Assert.Equal("--level 1", values.Arguments);
            Assert.Equal(directory, values.WorkingDirectory);
            Assert.Equal(LaunchKind.Executable, values.LaunchKind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryCreate_AllowedUriNormalizesToUriKind()
    {
        var success = GameEditValidator.TryCreate(
            "Steam",
            "steam://run/123",
            "ignored",
            null,
            out var values,
            out var error);

        Assert.True(success, error);
        Assert.Equal(LaunchKind.Uri, values!.LaunchKind);
        Assert.Equal("steam://run/123", values.Target);
        Assert.Null(values.Arguments);
    }

    [Fact]
    public void TryCreate_UrlFileReadsProtocol()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var url = Path.Combine(directory, "Hades.url");
            File.WriteAllText(url, "[InternetShortcut]\nURL=steam://run/1");

            var success = GameEditValidator.TryCreate("Hades", url, null, null, out var values, out var error);

            Assert.True(success, error);
            Assert.Equal(LaunchKind.Uri, values!.LaunchKind);
            Assert.Equal("steam://run/1", values.Target);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("", @"C:\game.exe", "nombre")]
    [InlineData("Game", "", "destino")]
    [InlineData("Game", @"C:\missing\game.exe", "no existe")]
    [InlineData("Game", @"C:\missing\script.cmd", "debe ser")]
    public void TryCreate_InvalidValuesFail(string name, string target, string expectedMessage)
    {
        var success = GameEditValidator.TryCreate(name, target, null, null, out var values, out var error);

        Assert.False(success);
        Assert.Null(values);
        Assert.Contains(expectedMessage, error);
    }

    [Fact]
    public void TryCreate_RejectsMissingWorkingDirectory()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var exe = Path.Combine(directory, "game.exe");
            File.WriteAllText(exe, "stub");

            var success = GameEditValidator.TryCreate(
                "Game",
                exe,
                null,
                Path.Combine(directory, "gone"),
                out _,
                out var error);

            Assert.False(success);
            Assert.Contains("directorio", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryCreate_RejectsExcessivelyLongArguments()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            var exe = Path.Combine(directory, "game.exe");
            File.WriteAllText(exe, "stub");

            var success = GameEditValidator.TryCreate(
                "Game",
                exe,
                new string('a', 16_001),
                null,
                out _,
                out var error);

            Assert.False(success);
            Assert.Contains("largos", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
