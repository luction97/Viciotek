using System.IO;
using System.Windows;
using System.Windows.Threading;
using Viciotek.Application;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Processes;
using Viciotek.Infrastructure.Shell;
using Viciotek.Infrastructure.Storage;
using Viciotek.Presentation;
using Viciotek.Presentation.Dialogs;
using Viciotek.Presentation.Diagnostics;

namespace Viciotek;

public partial class App : System.Windows.Application
{
    private string? _logPath;
    private IconService? _iconService;
    private LibraryInstance? _instance;
    private InstallationGuard? _installationGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _installationGuard = new InstallationGuard();
            var paths = AppPaths.CreateDefault();
            paths.EnsureCreated();
            _logPath = Path.Combine(paths.Root, "viciotek.log");
            _instance = LibraryInstance.TryAcquire(paths.Root);
            if (_instance is null)
            {
                LibraryInstance.ActivateExisting(paths.Root);
                Shutdown();
                return;
            }

            var settingsStore = new SettingsStore(paths);
            var libraryStore = new LibraryStore(paths);
            var (settings, settingsWarning) = settingsStore.Load();
            var (games, libraryWarning) = libraryStore.Load();
            var startupWarning = JoinWarnings(settingsWarning, libraryWarning);

            var shortcutResolver = new ShellShortcutResolver();
            var importer = new GameImporter(shortcutResolver, new GameIdentityResolver(new WindowsGameMetadataReader()));
            var launcher = new GameLauncher();
            var covers = new CoverStore(paths);
            var steamGrid = new SteamGridDbClient(settings.SteamGridDbApiKey);
            var dialogs = new DialogService(paths, steamGrid);
            var viewModel = new MainViewModel(
                games,
                settings,
                libraryStore,
                settingsStore,
                importer,
                launcher,
                covers,
                steamGrid,
                dialogs,
                startupWarning);

            _iconService = new IconService();
            var window = new MainWindow(
                viewModel,
                new CoverImageLoader(covers),
                _iconService,
                settings);

            MainWindow = window;
            window.SourceInitialized += (_, _) =>
                _instance.SetWindowHandle(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            StartupProbe.AttachIfRequested(window);
            window.Show();
        }
        catch (Exception exception)
        {
            Log(exception, "Inicio");
            MessageBox.Show(
                "VICIOTEK no pudo iniciar.\n\n" + exception.Message +
                (_logPath is null ? string.Empty : $"\n\nDetalle técnico: {_logPath}"),
                "VICIOTEK",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception, "Interfaz");
        MessageBox.Show(
            "VICIOTEK encontró un problema inesperado, pero la ventana sigue abierta.\n\n" + e.Exception.Message +
            (_logPath is null ? string.Empty : $"\n\nDetalle técnico: {_logPath}"),
            "VICIOTEK",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log(e.ExceptionObject as Exception, "Proceso");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log(e.Exception, "Tarea en segundo plano");
        e.SetObserved();
    }

    private void Log(Exception? exception, string source)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var path = _logPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Viciotek",
                "viciotek.log");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {source}{Environment.NewLine}" +
                exception + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception)
        {
            // Logging must never mask the original error.
        }
    }

    private static string? JoinWarnings(params string?[] warnings)
    {
        var present = warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).ToArray();
        return present.Length == 0 ? null : string.Join(" ", present);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _iconService?.Dispose();
        _instance?.Dispose();
        _installationGuard?.Dispose();
        base.OnExit(e);
    }
}
