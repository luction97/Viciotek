using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
        UpdateConnectionAction();
        if (!string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
            ShowConnectionStatus("Conexión guardada en este equipo.");
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    public AppSettings? Result { get; private set; }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_verifying) return;
        var code = ApiKeyBox.Password.Trim();
        if (code.Length > 0 && code != (_settings.SteamGridDbApiKey ?? string.Empty).Trim())
        {
            if (!await CheckConnectionAsync()) return;
        }
        SaveAndClose();
    }

    private void SaveAndClose()
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
        var saveAfterConnecting = ApiKeyBox.Password.Trim() != (_settings.SteamGridDbApiKey ?? string.Empty).Trim();
        if (await CheckConnectionAsync() && saveAfterConnecting) SaveAndClose();
    }

    private async Task<bool> CheckConnectionAsync()
    {
        if (_steamGrid is null || _verifying) return false;
        var code = ApiKeyBox.Password.Trim();
        if (code.Length == 0)
        {
            ShowConnectionStatus("Copiá el código de SteamGridDB y pegalo acá para conectar.", error: true);
            return false;
        }
        _verifying = true;
        ApiKeyBox.IsEnabled = PasteButton.IsEnabled = SaveButton.IsEnabled = VerifyButton.IsEnabled = false;
        VerifyButton.Content = "Conectando…";
        ShowConnectionStatus("Comprobando tu código con SteamGridDB…");
        try
        {
            var result = await _steamGrid.VerifyAsync(code, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return false;
            ShowConnectionStatus(result.Success ? "Conexión comprobada. Ya podés buscar portadas." : result.Message,
                error: !result.Success);
            return result.Success;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception)
        {
            ShowConnectionStatus("No pudimos conectar. Revisá tu conexión a internet e intentá de nuevo.", error: true);
            return false;
        }
        finally
        {
            _verifying = false;
            if (IsVisible)
            {
                ApiKeyBox.IsEnabled = PasteButton.IsEnabled = SaveButton.IsEnabled = true;
                UpdateConnectionAction();
            }
        }
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // InitializeComponent can raise this event before the remaining named controls exist.
        if (VerifyButton is null || ConnectionStatusText is null || _verifying) return;
        ConnectionStatusText.Visibility = Visibility.Collapsed;
        UpdateConnectionAction();
    }

    private void UpdateConnectionAction()
    {
        var code = ApiKeyBox.Password.Trim();
        VerifyButton.IsEnabled = !_verifying && _steamGrid is not null && code.Length > 0;
        VerifyButton.Content = code.Length > 0 && code == (_settings?.SteamGridDbApiKey ?? string.Empty).Trim()
            ? "Verificar" : "Conectar";
    }

    private void PasteCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var code = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
            if (code.Length == 0)
            {
                ShowConnectionStatus("Primero copiá tu código en SteamGridDB. Después volvé y elegí Pegar.", error: true);
                return;
            }
            ApiKeyBox.Password = code;
            ApiKeyBox.Focus();
        }
        catch (ExternalException)
        {
            ShowConnectionStatus("No pudimos pegar el código. Probá con Ctrl+V en el campo de conexión.", error: true);
        }
    }

    private void ShowConnectionStatus(string message, bool error = false)
    {
        ConnectionStatusText.Text = message;
        ConnectionStatusText.Foreground = (SolidColorBrush)FindResource(error ? "DangerBrush" : "TextSecondaryBrush");
        ConnectionStatusText.Visibility = Visibility.Visible;
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
