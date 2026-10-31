using System.Windows;

namespace Viciotek.Presentation.Dialogs;

public partial class ConfirmDialog : ViciotekDialogWindow
{
    public ConfirmDialog(string title, string message, string confirmLabel, string? details = null, bool showRemovalHint = true)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        if (!showRemovalHint) RemovalHint.Visibility = Visibility.Collapsed;
        if (details is not null)
        {
            SizeToContent = SizeToContent.Height;
            MinHeight = 260;
            RemovalHint.Visibility = CancelButton.Visibility = Visibility.Collapsed;
            DetailsSection.Visibility = Visibility.Visible;
            DetailsText.Text = details;
            ConfirmButton.Style = (Style)FindResource("PrimaryButtonStyle");
            CancelButton.IsDefault = false;
            ConfirmButton.IsDefault = true;
        }
        Loaded += (_, _) => { if (details is null) CancelButton.Focus(); else ConfirmButton.Focus(); };
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
