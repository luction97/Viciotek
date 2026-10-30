using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Viciotek.Application;

namespace Viciotek.Presentation.Dialogs;

public partial class CoverGalleryDialog : ViciotekDialogWindow
{
    private readonly CoverGalleryViewModel _viewModel;

    public CoverGalleryDialog(CoverGalleryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Completed += () => { if (IsVisible) DialogResult = true; };
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            await viewModel.InitializeAsync();
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private async void Suggestion_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Suggestions, e.OriginalSource as DependencyObject) is ListBoxItem { Content: SteamGridGame game })
        { e.Handled = true; SearchBox.Focus(); await _viewModel.SelectGameAsync(game); }
    }
    private async void Suggestions_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Suggestions.SelectedItem is SteamGridGame game)
        { e.Handled = true; SearchBox.Focus(); await _viewModel.SelectGameAsync(game); }
    }
    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _viewModel.ShowSuggestions && Suggestions.HasItems)
        {
            e.Handled = true;
            Suggestions.SelectedIndex = 0;
            Suggestions.UpdateLayout();
            (Suggestions.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _viewModel.SearchAsync();
    }
}
