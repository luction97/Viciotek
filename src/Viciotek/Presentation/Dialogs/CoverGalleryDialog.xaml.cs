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

    private async void Game_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SteamGridGame game) await _viewModel.SelectGameAsync(game);
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _viewModel.SearchAsync();
    }
}
