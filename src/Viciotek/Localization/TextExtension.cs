using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Viciotek.Localization;

[MarkupExtensionReturnType(typeof(object))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding { Source = Language.Current, Path = new PropertyPath("[" + Key + "]"), Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}
