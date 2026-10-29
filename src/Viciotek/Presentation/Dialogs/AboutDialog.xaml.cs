using System.Windows;

namespace Viciotek.Presentation.Dialogs;

public partial class AboutDialog : ViciotekDialogWindow
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = "Versión " + typeof(AboutDialog).Assembly.GetName().Version?.ToString(3);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
