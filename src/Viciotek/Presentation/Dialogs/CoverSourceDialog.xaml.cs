using System.Windows;

namespace Viciotek.Presentation.Dialogs;

public partial class CoverSourceDialog : ViciotekDialogWindow
{
    public CoverSourceDialog(bool connected, bool canChooseAutomatically)
    {
        InitializeComponent();
        if (!connected) WebButton.Content = L.Get("ConnectSteamgriddbAndSearch");
        AutomaticOptions.Visibility = connected && canChooseAutomatically ? Visibility.Visible : Visibility.Collapsed;
    }
    public CoverSource? Result { get; private set; }
    private void Select(CoverSource source) { Result = source; DialogResult = true; }
    private void Local_Click(object sender, RoutedEventArgs e) => Select(CoverSource.Local);
    private void Web_Click(object sender, RoutedEventArgs e) => Select(CoverSource.SteamGridDb);
    private void Automatic_Click(object sender, RoutedEventArgs e) => Select(CoverSource.Automatic);
}
