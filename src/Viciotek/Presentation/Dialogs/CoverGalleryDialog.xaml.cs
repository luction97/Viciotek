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
            SearchBox.Select(SearchBox.Text.Length, 0);
            await viewModel.InitializeAsync();
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Preview_Loaded(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: CoverCandidate candidate }) _viewModel.SetPreviewVisible(candidate, true); }
    private void Preview_Unloaded(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { DataContext: CoverCandidate candidate }) _viewModel.SetPreviewVisible(candidate, false); }
    private async void CoverResults_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange > 0 && e.ExtentHeight - e.VerticalOffset - e.ViewportHeight < 300 && _viewModel.LoadMoreCommand.CanExecute(null))
            await _viewModel.LoadMoreAsync();
    }

    private async void Suggestion_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Suggestions, e.OriginalSource as DependencyObject) is ListBoxItem { Content: SteamGridGame game })
        { e.Handled = true; await SelectSuggestionAsync(game); }
    }
    private async void Suggestions_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Suggestions.SelectedItem is SteamGridGame game)
        { e.Handled = true; await SelectSuggestionAsync(game); }
    }
    private async Task SelectSuggestionAsync(SteamGridGame game)
    {
        var selection = _viewModel.SelectGameAsync(game);
        SearchBox.Focus();
        SearchBox.Select(SearchBox.Text.Length, 0);
        await selection;
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
