using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Viciotek.Presentation.Dialogs;

public partial class AddGamesDialog : ViciotekDialogWindow
{
    public AddGamesDialog()
    {
        InitializeComponent();
    }

    public string[]? Files { get; private set; }

    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir juegos",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Accesos y programas|*.lnk;*.exe;*.url|Accesos directos|*.lnk|Programas|*.exe|Accesos web|*.url|Todos los archivos|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            Files = dialog.FileNames;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetPaths(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (TryGetPaths(e.Data, out var paths))
        {
            Files = paths;
            DialogResult = true;
        }

        e.Handled = true;
    }

    private static bool TryGetPaths(IDataObject data, out string[] paths)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            paths = files;
            return true;
        }

        paths = [];
        return false;
    }
}
