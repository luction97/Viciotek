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
        Assert.DoesNotContain("arrastr", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("soltá", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(document.Descendants(ui + "MenuItem"), item => (string?)item.Attribute("Header") == "Cambiar portada…");
        Assert.Contains(document.Descendants(ui + "Button"), item =>
            (string?)item.Attribute("AutomationProperties.Name") == "Preferencias" &&
            (string?)item.Attribute("Command") == "{Binding OpenPreferencesCommand}");
        Assert.Contains(document.Descendants(ui + "Button"), item =>
            (string?)item.Attribute("AutomationProperties.Name") == "Cambiar portada" &&
            (string?)item.Attribute("Command") == "{Binding ChangeCoverCommand}" &&
            item.Descendants(ui + "Setter").Any(setter => (string?)setter.Attribute("Property") == "Visibility" && (string?)setter.Attribute("Value") == "Hidden"));
    }
}
