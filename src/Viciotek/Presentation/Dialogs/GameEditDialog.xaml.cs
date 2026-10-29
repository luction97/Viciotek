using System.IO;
using System.Windows;
using System.Windows.Controls;
using Viciotek.Application;
using Viciotek.Domain;
using Microsoft.Win32;

namespace Viciotek.Presentation.Dialogs;

public partial class GameEditDialog : ViciotekDialogWindow
{
    public GameEditDialog(Game game)
    {
        InitializeComponent();
        NameBox.Text = game.Name;
        TargetBox.Text = game.LaunchPath;
        ArgumentsBox.Text = game.Arguments ?? string.Empty;
        WorkingDirectoryBox.Text = game.WorkingDirectory ?? string.Empty;

        RefreshTargetState();

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public GameEditResult? Result { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!GameEditValidator.TryCreate(
                NameBox.Text,
                TargetBox.Text,
                ArgumentsBox.Text,
                WorkingDirectoryBox.Text,
                out var values,
                out var error) ||
            values is null)
        {
            ShowError(error ?? "Revisá los datos del juego.");
            return;
        }

        Result = new GameEditResult(
            values.Name,
            values.Target,
            values.LaunchKind,
            values.Arguments,
            values.WorkingDirectory);
        DialogResult = true;
    }

    private void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir destino del juego",
            CheckFileExists = true,
            Multiselect = false,
            Filter = "Programas y accesos|*.exe;*.url|Todos los archivos|*.*"
        };

        var current = TargetBox.Text.Trim().Trim('"');
        if (File.Exists(current))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
            dialog.FileName = Path.GetFileName(current);
        }

        if (dialog.ShowDialog(this) == true)
        {
            TargetBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text))
            {
                WorkingDirectoryBox.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Elegir directorio de trabajo",
            Multiselect = false
        };

        var current = WorkingDirectoryBox.Text.Trim().Trim('"');
        if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) == true)
        {
            WorkingDirectoryBox.Text = dialog.FolderName;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Field_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ErrorText is null)
        {
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
        if (ReferenceEquals(sender, TargetBox) && ArgumentsBox is not null && WorkingDirectoryBox is not null && BrowseFolderButton is not null && TargetHint is not null)
        {
            RefreshTargetState();
        }
    }

    private void RefreshTargetState()
    {
        var uri = ProtocolPolicy.TryValidate(TargetBox.Text, out _, out _);
        ArgumentsBox.IsEnabled = !uri;
        WorkingDirectoryBox.IsEnabled = !uri;
        BrowseFolderButton.IsEnabled = !uri;
        TargetHint.Text = uri ? "Enlace del launcher" : ".exe, .url o enlace de launcher";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
