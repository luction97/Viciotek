using Viciotek.Infrastructure.Processes;
using Viciotek.Infrastructure.Shell;

namespace Viciotek.Tests;

public sealed class WindowsCommandLineTests
{
    [Fact]
    public void Split_ParsesQuotedArguments()
    {
        var arguments = WindowsCommandLine.Split("\"C:\\Games\\My Game\\save.dat\" --level \"A B\" --flag");

        Assert.Equal(
            ["C:\\Games\\My Game\\save.dat", "--level", "A B", "--flag"],
            arguments);
    }

    [Fact]
    public void Split_EmptyOrNullReturnsEmpty()
    {
        Assert.Empty(WindowsCommandLine.Split(null));
        Assert.Empty(WindowsCommandLine.Split("   "));
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("with space")]
    [InlineData("with \"quotes\"")]
    [InlineData("trailing slash \\")]
    [InlineData("quoted \\\" middle")]
    public void QuoteThenSplit_RoundTrips(string value)
    {
        var joined = WindowsCommandLine.Join([value, "--next"]);

        var split = WindowsCommandLine.Split(joined);

        Assert.Equal([value, "--next"], split);
    }

    [Fact]
    public void Quote_EmptyStringUsesEmptyQuotes() =>
        Assert.Equal("\"\"", WindowsCommandLine.Quote(string.Empty));
}

public sealed class IconServiceTests
{
    [Fact]
    public async Task Service_StartsWorkerOnlyForAnActualIconRequest()
    {
        using var service = new Viciotek.Infrastructure.Shell.IconService();
        Assert.False(service.IsWorkerStarted);
        Assert.Null(await service.LoadAsync(null));
        Assert.False(service.IsWorkerStarted);
        await service.LoadAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"));
        Assert.True(service.IsWorkerStarted);
    }

    [Fact]
    public async Task LoadAsync_ExtractsIconFromSystemExecutable()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Assert.True(File.Exists(notepad));

        using var service = new IconService();
        var icon = await service.LoadAsync(notepad).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.NotNull(icon);
        Assert.True(icon.PixelWidth >= 32);
        Assert.True(icon.PixelHeight >= 32);
        Assert.True(icon.IsFrozen);
    }

    [Fact]
    public async Task LoadAsync_InvalidPathsReturnNull()
    {
        using var service = new IconService();

        Assert.Null(await service.LoadAsync(null));
        Assert.Null(await service.LoadAsync(string.Empty));
        Assert.Null(await service.LoadAsync(Path.Combine(Environment.SystemDirectory, "does-not-exist.exe")));
        Assert.Null(await service.LoadAsync("<>|invalid"));
    }

    [Fact]
    public async Task LoadAsync_SamePathReturnsCachedInstance()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        using var service = new IconService();

        var first = await service.LoadAsync(notepad).WaitAsync(TimeSpan.FromSeconds(20));
        var second = await service.LoadAsync(notepad).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Same(first, second);
    }
}
