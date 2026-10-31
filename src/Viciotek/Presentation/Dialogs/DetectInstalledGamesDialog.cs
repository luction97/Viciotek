using System.Windows;
using System.Windows.Controls;
using Viciotek.Application;

namespace Viciotek.Presentation.Dialogs;

public partial class DetectInstalledGamesDialog : Window
{
    public IReadOnlyList<DetectedGame> Selected { get; private set; } = [];

    public DetectInstalledGamesDialog(IReadOnlyList<DetectedGame> games)
    {
        Title = "Detectar juegos instalados";
        Width = 480;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var list = new ListBox { SelectionMode = SelectionMode.Multiple, Margin = new Thickness(12) };
        foreach (var g in games)
        {
            var item = new ListBoxItem { Content = $"{g.Name}  —  {g.Platform}", Tag = g, IsSelected = true };
            list.Items.Add(item);
        }
        Grid.SetRow(list, 0);
        grid.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        var importBtn = new Button { Content = "Importar seleccionados", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4) };
        var cancelBtn = new Button { Content = "Cancelar", Padding = new Thickness(10, 4, 10, 4) };
        importBtn.Click += (_, _) =>
        {
            Selected = list.SelectedItems.Cast<ListBoxItem>().Select(i => (DetectedGame)i.Tag!).ToList();
            DialogResult = true;
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        buttons.Children.Add(importBtn);
        buttons.Children.Add(cancelBtn);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
    }
}
