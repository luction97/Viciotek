using System.Xml.Linq;

namespace Viciotek.Tests;

public sealed class MinimalInterfaceTests
{
    [Fact]
    public void MainWindow_HasNoObviousInstructionsAndKeepsCoverCustomizationDiscoverable()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "UiFixtures", "MainWindow.xaml"));
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var text = string.Join(" ", document.Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName is "Text" or "Content")
            .Select(attribute => attribute.Value));
        Assert.DoesNotContain("clic", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{loc:Text DragGamesToAdd}", text);
        Assert.Contains("{loc:Text ShortcutsOrExecutables}", text);
        Assert.Contains("{loc:Text YourLibraryStartsHere}", text);
        Assert.Contains(document.Descendants(ui + "Button"), item =>
            (string?)item.Attribute("Content") == "{loc:Text ChooseGame}" &&
            (string?)item.Attribute("Command") == "{Binding AddGamesCommand}");
        Assert.DoesNotContain("soltá", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(document.Descendants(ui + "MenuItem"), item => (string?)item.Attribute("Header") == "{loc:Text ChangeCover}");
        Assert.Contains(document.Descendants(ui + "Button"), item =>
            (string?)item.Attribute("AutomationProperties.Name") == "{loc:Text Settings}" &&
            (string?)item.Attribute("Command") == "{Binding OpenPreferencesCommand}");
        Assert.DoesNotContain(document.Descendants(ui + "Button"), item =>
            (string?)item.Attribute("Command") == "{Binding ChangeCoverCommand}");
        Assert.Contains(document.Descendants(ui + "MenuItem"), item =>
            (string?)item.Attribute("Header") == "{loc:Text ChangeCover}" &&
            (string?)item.Attribute("Command") == "{Binding ChangeCoverCommand}");
    }
}
