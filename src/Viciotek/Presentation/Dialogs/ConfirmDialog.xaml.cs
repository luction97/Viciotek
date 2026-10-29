using System.Windows;

namespace Viciotek.Presentation.Dialogs;

public partial class ConfirmDialog : ViciotekDialogWindow
{
    public ConfirmDialog(string title, string message, string confirmLabel)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
