using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Storage;

namespace Viciotek.Presentation.Dialogs;

public partial class PreferencesDialog : ViciotekDialogWindow
{
    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly SteamGridDbClient? _steamGrid;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _verifying;

    public PreferencesDialog(AppSettings settings, AppPaths paths, SteamGridDbClient? steamGrid = null)
    {
        InitializeComponent();
        _settings = settings;
        _paths = paths;
        _steamGrid = steamGrid;
        ApiKeyBox.Password = settings.SteamGridDbApiKey ?? string.Empty;
        AutoFetchCheckBox.IsChecked = settings.AutoFetchCovers;
        AnimationsCheckBox.IsChecked = settings.AnimationsEnabled;
        DataPathBox.Text = paths.Root;
        VerifyButton.IsEnabled = steamGrid is not null;
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    public AppSettings? Result { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var updated = _settings.Clone();
        updated.SteamGridDbApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password)
            ? null
            : ApiKeyBox.Password.Trim();
        updated.AutoFetchCovers = AutoFetchCheckBox.IsChecked == true;
        updated.AnimationsEnabled = AnimationsCheckBox.IsChecked == true;
        Result = updated;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenSteamGridDb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.steamgriddb.com/profile/preferences/api",
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowStatus("No se pudo abrir el navegador. La dirección es steamgriddb.com/profile/preferences/api");
        }
    }

    private async void VerifyConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_steamGrid is null || _verifying) return;
        _verifying = true;
        VerifyButton.IsEnabled = false;
        ShowStatus("Verificando…");
        try
        {
            var result = await _steamGrid.VerifyAsync(ApiKeyBox.Password, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            ShowStatus(result.Message);
            StatusText.Foreground = result.Success
                ? new SolidColorBrush(Color.FromRgb(0x7F, 0xC7, 0x9A))
                : (SolidColorBrush)FindResource("DangerBrush");
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            ShowStatus("Sin conexión a internet.");
        }
        finally
        {
            _verifying = false;
            if (IsVisible) VerifyButton.IsEnabled = true;
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_paths.Root);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_paths.Root}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowStatus("No se pudo abrir la carpeta de datos.");
        }
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog { Owner = Owner };
        dialog.ShowDialog();
    }
}
