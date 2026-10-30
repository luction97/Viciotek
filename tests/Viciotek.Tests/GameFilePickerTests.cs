using Viciotek.Presentation.Dialogs;

namespace Viciotek.Tests;

public sealed class GameFilePickerTests
{
    [Fact]
    public Task Picker_PreservesShortcutPathsForIdentityAndLaunchArguments() => WpfTest.RunAsync(() =>
    {
        var picker = AddGamesDialog.CreateFilePicker();
        Assert.False(picker.DereferenceLinks);
        Assert.True(picker.CheckFileExists);
        Assert.True(picker.Multiselect);
        return Task.CompletedTask;
    });
}
