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
    private string? _selectedLanguage;

    private readonly bool _connectionOnly;

    public PreferencesDialog(AppSettings settings, AppPaths paths, SteamGridDbClient? steamGrid = null, bool connectionOnly = false)
    {
        InitializeComponent();
        _connectionOnly = connectionOnly;
        if (connectionOnly)
        {
            Title = HeadingText.Text = L.Get("ConnectCoverService");
            MinHeight = 0;
            ProviderHeader.Visibility = LanguageButton.Visibility = LibraryOptions.Visibility = AboutButton.Visibility = Visibility.Collapsed;
            SaveButton.Content = L.Get("Cancel");
            SaveButton.Style = (Style)FindResource("GhostButtonStyle");
            SaveButton.IsDefault = false;
            SaveButton.IsCancel = true;
            VerifyButton.IsDefault = true;
        }
        _settings = settings;
        _selectedLanguage = settings.Language is "es" or "en" ? settings.Language : null;
        UpdateLanguageChoice();
        _paths = paths;
        _steamGrid = steamGrid;
        ApiKeyBox.Password = settings.SteamGridDbApiKey ?? string.Empty;
        DataObject.AddPastingHandler(ApiKeyBox, ApiKeyBox_Pasting);
        AutoFetchCheckBox.IsChecked = settings.AutoFetchCovers;
        AnimationsCheckBox.IsChecked = settings.AnimationsEnabled;
        CoverSizeSlider.Value = settings.CoverSizePreset;
        UpdateCoverSizeLabel();
        DataPathBox.Text = paths.Root;
        UpdateConnectionAction();
        if (!string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
            ShowConnectionStatus(L.Get("ConnectionSavedOnThisComputer"));
        if (!connectionOnly)
        {
            ConnectionForm.Visibility = Visibility.Collapsed;
            ConnectionSummary.Visibility = Visibility.Visible;
            var connected = !string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey);
            SavedConnectionText.Text = L.Get(connected ? "ConnectionSavedOnThisComputer" : "CoverConnectionBenefit");
            SavedConnectionText.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            ManageConnectionButton.Content = L.Get(connected ? "ReviewCoverConnectionAction" : "ConnectSteamGridOptional");
            DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        }
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    public AppSettings? Result { get; private set; }
    public event Action<int>? CoverSizePreview;

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        LanguageButton.ContextMenu.PlacementTarget = LanguageButton;
        LanguageButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        LanguageButton.ContextMenu.IsOpen = true;
    }
    private void LanguageChoice_Click(object sender, RoutedEventArgs e)
    {
        _selectedLanguage = ((System.Windows.Controls.MenuItem)sender).Tag as string;
        if (_selectedLanguage == "auto") _selectedLanguage = null;
        UpdateLanguageChoice();
    }
    private void UpdateLanguageChoice()
    {
        SelectedLanguageText.Text = _selectedLanguage switch { "es" => "Español", "en" => "English", _ => L.Get("SystemDefault") };
        SystemLanguageItem.IsChecked = _selectedLanguage is null;
        SpanishLanguageItem.IsChecked = _selectedLanguage == "es";
        EnglishLanguageItem.IsChecked = _selectedLanguage == "en";
        SystemLanguageCheck.Visibility = _selectedLanguage is null ? Visibility.Visible : Visibility.Hidden;
        SpanishLanguageCheck.Visibility = _selectedLanguage == "es" ? Visibility.Visible : Visibility.Hidden;
        EnglishLanguageCheck.Visibility = _selectedLanguage == "en" ? Visibility.Visible : Visibility.Hidden;
    }
    private void CoverSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CoverSizeLabel is null) return;
        UpdateCoverSizeLabel();
        CoverSizePreview?.Invoke((int)CoverSizeSlider.Value);
    }

    private void UpdateCoverSizeLabel() => CoverSizeLabel.Text = (int)CoverSizeSlider.Value switch
    { 0 => L.Get("Small"), 1 => L.Get("Medium"), 2 => L.Get("Large"), _ => L.Get("ExtraLarge") };

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionOnly) { Close(); return; }
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
        Result = CurrentSettings();
        DialogResult = true;
    }

    private AppSettings CurrentSettings()
    {
        var updated = _settings.Clone();
        updated.SteamGridDbApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password)
            ? null
            : ApiKeyBox.Password.Trim();
        updated.AutoFetchCovers = AutoFetchCheckBox.IsChecked == true ||
            (_connectionOnly && string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey));
        updated.AnimationsEnabled = AnimationsCheckBox.IsChecked == true;
        updated.CoverSizePreset = (int)CoverSizeSlider.Value;
        updated.Language = _selectedLanguage;
        return updated;
    }

    private void ManageConnection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PreferencesDialog(CurrentSettings(), _paths, _steamGrid, connectionOnly: true) { Owner = this };
        if (dialog.ShowDialog() == true) { Result = dialog.Result; DialogResult = true; }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Password = string.Empty;
        AutoFetchCheckBox.IsChecked = false;
        SaveAndClose();
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
            ShowStatus(L.Get("CouldNotOpenTheBrowserTheAddressIsSteamgriddbComProfilePreferencesApi"));
        }
    }

    private async void VerifyConnection_Click(object sender, RoutedEventArgs e)
    {
        var saveAfterConnecting = ApiKeyBox.Password.Trim() != (_settings.SteamGridDbApiKey ?? string.Empty).Trim();
        if (await CheckConnectionAsync() && (saveAfterConnecting || _connectionOnly)) SaveAndClose();
    }

    private async Task<bool> CheckConnectionAsync()
    {
        if (_steamGrid is null || _verifying) return false;
        var code = ApiKeyBox.Password.Trim();
        if (code.Length == 0)
        {
            ShowConnectionStatus(L.Get("CopyYourSteamgriddbCodeAndPasteItHereToConnect"), error: true);
            return false;
        }
        _verifying = true;
        ApiKeyBox.IsEnabled = PasteButton.IsEnabled = SaveButton.IsEnabled = VerifyButton.IsEnabled = false;
        VerifyButton.Content = L.Get("Connecting");
        ShowConnectionStatus(L.Get("CheckingYourCodeWithSteamgriddb"));
        try
        {
            var result = await _steamGrid.VerifyAsync(code, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return false;
            ShowConnectionStatus(result.Success ? L.Get("ConnectionVerifiedYouCanNowFindCovers") : result.Message,
                error: !result.Success);
            return result.Success;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception)
        {
            ShowConnectionStatus(L.Get("CouldNotConnectCheckYourInternetConnectionAndTryAgain"), error: true);
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
            ? L.Get("Verify") : L.Get("Connect2");
    }

    private async void PasteCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var code = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
            if (code.Length == 0)
            {
                ShowConnectionStatus(L.Get("FirstCopyYourCodeFromSteamgriddbThenComeBackAndChoosePaste"), error: true);
                return;
            }
            await PasteConnectionCodeAsync(code);
        }
        catch (ExternalException)
        {
            ShowConnectionStatus(L.Get("CouldNotPasteTheCodeTryCtrlVInTheConnectionField"), error: true);
        }
    }

    private async void ApiKeyBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.CommandCancelled || (e.FormatToApply != DataFormats.UnicodeText && e.FormatToApply != DataFormats.Text)) return;
        // The routed event runs before WPF updates Password; inspect the completed edit.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        if (IsVisible && !_lifetime.IsCancellationRequested)
            await PasteConnectionCodeAsync(ApiKeyBox.Password);
    }

    internal async Task PasteConnectionCodeAsync(string code)
    {
        if (!IsVisible || _verifying || string.IsNullOrWhiteSpace(code)) return;
        ApiKeyBox.Password = code.Trim();
        if (await CheckConnectionAsync() && !_lifetime.IsCancellationRequested) SaveAndClose();
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
            ShowStatus(L.Get("CouldNotOpenTheDataFolder"));
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
