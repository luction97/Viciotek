using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Viciotek;
using Viciotek.Application;
using Viciotek.Domain;
using Viciotek.Infrastructure.Covers;
using Viciotek.Infrastructure.Imaging;
using Viciotek.Infrastructure.Library;
using Viciotek.Infrastructure.Processes;
using Viciotek.Infrastructure.Shell;
using Viciotek.Infrastructure.Storage;
using Viciotek.Presentation;
using Viciotek.Presentation.Controls;
using Viciotek.Presentation.Dialogs;

// Opt-in real HTTP. Never put a personal key in this file, arguments, or diagnostics.
internal static class Program
{
    private static string? _failure;
    private static bool _expectGallery;
    private static bool _galleryConfirmed;

    [STAThread]
    private static int Main(string[] args)
    {
        var live = args.Contains("--live");
        var key = live ? Environment.GetEnvironmentVariable("VICIOTEK_TEST_API_KEY") : "fixture-key";
        Environment.SetEnvironmentVariable("VICIOTEK_TEST_API_KEY", null);
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("--live requiere VICIOTEK_TEST_API_KEY en el entorno del proceso.");
            return 2;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode", "viciotek-drop-" + Guid.NewGuid().ToString("N"));
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Viciotek;component/Presentation/Theme/Theme.xaml", UriKind.Relative) });
        app.Resources["BoolToVisibility"] = new BooleanToVisibilityConverter();
        EventManager.RegisterClassHandler(typeof(CoverGalleryDialog), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnGalleryLoaded));
        var exitCode = 1;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await RunAsync(app, root, key, live);
                Console.WriteLine("PASS: " + (live ? "SteamGridDB real" : "HTTP simulado") +
                    "; FileDrop WPF .exe/.lnk/.url, portada local, galería, preferencias y reapertura offline.");
                Console.WriteLine("Datos aislados: " + root);
                exitCode = 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception.GetType().Name + ": " + exception.Message.Replace(key, "[REDACTED]"));
            }
            finally { app.Shutdown(); }
        }));
        app.Run();
        return exitCode;
    }

    private static async Task RunAsync(System.Windows.Application app, string root, string key, bool live)
    {
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        var library = new LibraryStore(paths);
        var settingsStore = new SettingsStore(paths);
        // The real key stays in HttpClient memory, not even in the sandbox's settings.
        var settings = new AppSettings { AutoFetchCovers = true };
        using var tracking = new TrackingHandler(live ? new HttpClientHandler() : new FixtureHandler());
        var client = new SteamGridDbClient(key, tracking);
        Require((await client.VerifyAsync(default)).Success, "La verificación autenticada falló.");
        var covers = new CoverStore(paths);
        var viewModel = new MainViewModel([], settings, library, settingsStore,
            new GameImporter(new ShellShortcutResolver(), new GameIdentityResolver(new WindowsGameMetadataReader())),
            new NoLaunch(), covers, client, new DialogService(paths, client), null);
        using var icons = new IconService();
        var window = new MainWindow(viewModel, new CoverImageLoader(covers), icons, settings) { ShowActivated = false };
        app.MainWindow = window;
        window.Show();
        try
        {
            await VerifyPreferencesAsync(paths);
            var installation = Path.Combine(root, "Hades", "Binaries", "Win64");
            Directory.CreateDirectory(installation);
            var exe = Path.Combine(installation, "game.exe");
            await File.WriteAllTextAsync(exe, "Test fixture. Never executed.");
            await DropAndWaitAsync(window, viewModel, exe, 1);
            Require(viewModel.Games[0].Name == "Hades", "No se identificó Hades desde la carpeta real de la fixture.");

            var url = Path.Combine(root, "Hades.url");
            await File.WriteAllTextAsync(url, "[InternetShortcut]\nURL=steam://run/1145360");
            await DropAndWaitAsync(window, viewModel, url, 2);
            Require(viewModel.Games[1].Model.SteamAppId == 1145360, "No se conservó el Steam AppID.");

            var shortcut = Path.Combine(root, "Hades.lnk");
            CreateShortcut(shortcut, exe, installation);
            await DropAndWaitAsync(window, viewModel, shortcut, 3);
            Require(viewModel.Games[2].Model.LaunchPath == exe && viewModel.Games[2].Model.Arguments == "--smoke-test",
                "El acceso directo perdió destino o argumentos.");

            var count = tracking.RequestCount;
            var firstCover = viewModel.Games[0].Model.CoverFileName;
            RaiseDrop(window, exe);
            await WaitUntilAsync(() => !viewModel.IsImporting);
            Require(viewModel.Games.Count == 3 && tracking.RequestCount == count, "Un duplicado agregó otro juego o volvió a descargar la portada.");

            _expectGallery = true;
            await viewModel.SearchCoverAsync(viewModel.Games[0]).WaitAsync(TimeSpan.FromSeconds(60));
            _expectGallery = false;
            Require(_galleryConfirmed, "La galería real no confirmó una alternativa.");
            Require(firstCover != viewModel.Games[0].Model.CoverFileName, "La selección no actualizó la tarjeta.");

            var saved = new LibraryStore(paths).Load().Games;
            Require(saved.Count == 3 && saved.All(game => game.SteamGridDbGameId is > 0), "No persistieron las asociaciones.");
            Require(saved.All(game => File.Exists(covers.CoverPathFor(game)) && File.Exists(covers.ThumbnailPathFor(game))),
                "Faltan originales o miniaturas locales.");
            Require(!tracking.SawInvalidDimensions, "Se enviaron dimensiones que el proveedor no admite.");
            await CloseAsync(window, viewModel);

            using var offline = new TrackingHandler(new OfflineHandler());
            var offlineVm = new MainViewModel(saved, settings, library, settingsStore,
                new GameImporter(new ShellShortcutResolver()), new NoLaunch(), covers,
                new SteamGridDbClient(key, offline), new DialogService(paths), null);
            var reopened = new MainWindow(offlineVm, new CoverImageLoader(covers), icons, settings) { ShowActivated = false };
            app.MainWindow = reopened;
            reopened.Show();
            try
            {
                await WaitUntilAsync(() => Images(reopened).OfType<CoverImage>().Count(image => image.Source is BitmapSource) == saved.Count);
                Require(offline.RequestCount == 0, "La reapertura hizo solicitudes de red.");
            }
            finally { await CloseAsync(reopened, offlineVm); }
            Require(!File.ReadAllText(paths.SettingsFile).Contains(key, StringComparison.Ordinal), "La clave apareció en settings.json.");
            var report = new { Passed = true, LiveHttp = live, Version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3),
                RoutedFileDrops = new[] { ".exe", ".url", ".lnk" }, Games = saved.Count, ApiCalls = tracking.ApiCount,
                ImageCalls = tracking.ImageCount, OfflineCalls = offline.RequestCount, GalleryConfirmed = _galleryConfirmed,
                InvalidDimensions = tracking.SawInvalidDimensions, RealExplorerDrag = false };
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { if (window.IsVisible) await CloseAsync(window, viewModel); }
    }

    private static async Task VerifyPreferencesAsync(AppPaths paths)
    {
        using var handler = new PreferenceHandler();
        using var client = new SteamGridDbClient("saved-fixture-key", handler);
        var dialog = new PreferencesDialog(new AppSettings { SteamGridDbApiKey = "saved-fixture-key" }, paths, client) { ShowActivated = false };
        dialog.Show();
        try
        {
            var box = dialog.FindName("ApiKeyBox") as PasswordBox;
            Require(box is not null, "La clave no está oculta mediante PasswordBox.");
            box!.Password = "entered-fixture-key";
            ((Button)dialog.FindName("VerifyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => ((TextBlock)dialog.FindName("StatusText")).Text == "Conexión correcta.");
            Require(handler.UsedEnteredKey, "Verificar conexión usó la clave anterior en lugar de la escrita.");
        }
        finally { dialog.Close(); }
    }

    private static async Task DropAndWaitAsync(MainWindow window, MainViewModel vm, string path, int count)
    {
        RaiseDrop(window, path);
        await WaitUntilAsync(() => vm.Games.Count == count && !vm.IsImporting);
        var card = vm.Games[count - 1];
        await WaitUntilAsync(() => card.Model.CoverFileName is not null || vm.ToastIsError);
        Require(card.Model.CoverFileName is not null, "No se aplicó la portada: " + vm.ToastMessage);
        await WaitUntilAsync(() => !card.IsFindingCover);
    }

    private static void RaiseDrop(MainWindow window, string path)
    {
        Require(window.AllowDrop, "La ventana no admite FileDrop.");
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        var over = DragArgs(window, data, DragDrop.PreviewDragOverEvent);
        window.RaiseEvent(over);
        Require(over.Handled && over.Effects == DragDropEffects.Copy, "El DragOver real de la ventana no acepta el archivo.");
        var drop = DragArgs(window, data, DragDrop.PreviewDropEvent);
        window.RaiseEvent(drop);
        Require(drop.Handled && drop.Effects == DragDropEffects.Copy, "El PreviewDrop no procesó el archivo.");
    }

    private static DragEventArgs DragArgs(Window window, IDataObject data, RoutedEvent routedEvent)
    {
        // WPF's constructor is internal; reflection is confined to this test tool.
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [data, DragDropKeyStates.None, DragDropEffects.Copy, window, new Point(150, 180)], null)!;
        args.RoutedEvent = routedEvent;
        return args;
    }

    private static async void OnGalleryLoaded(object sender, RoutedEventArgs args)
    {
        var dialog = (CoverGalleryDialog)sender;
        try
        {
            Require(_expectGallery, "Se abrió un respaldo manual inesperado durante la búsqueda automática.");
            var vm = (CoverGalleryViewModel)dialog.DataContext;
            await WaitUntilAsync(() => vm.Candidates.Count > 1 && !vm.IsLoading);
            await WaitUntilAsync(() => Images(dialog).Any(image => image.Source is BitmapSource));
            vm.Selected = vm.Candidates[0];
            await vm.ConfirmAsync();
            Require(!dialog.IsVisible, "El modal no cerró tras confirmar y persistir la selección.");
            _galleryConfirmed = true;
        }
        catch (Exception exception) { _failure = exception.Message; dialog.Close(); }
    }

    private static IEnumerable<Image> Images(DependencyObject root)
    {
        if (root is Image image) yield return image;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        foreach (var child in Images(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static async Task CloseAsync(Window window, MainViewModel vm)
    {
        await vm.PrepareCloseAsync();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(45);
        while (!condition() && DateTime.UtcNow < limit && _failure is null) await Task.Delay(25);
        Require(_failure is null, _failure ?? "Fallo del modal.");
        Require(condition(), "La interfaz o la solicitud no completó la prueba dentro de 45 segundos.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void CreateShortcut(string path, string target, string workingDirectory)
    {
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!);
            dynamic dynamicShell = shell!;
            shortcut = dynamicShell.CreateShortcut(path);
            dynamic link = shortcut;
            link.TargetPath = target;
            link.Arguments = "--smoke-test";
            link.WorkingDirectory = workingDirectory;
            link.Save();
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    private sealed class TrackingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int RequestCount { get; private set; }
        public int ApiCount { get; private set; }
        public int ImageCount { get; private set; }
        public bool SawInvalidDimensions { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestCount++;
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/v2/", StringComparison.Ordinal)) ApiCount++;
            else { ImageCount++; Require(request.Headers.Authorization is null, "La descarga de imagen recibió credenciales."); }
            SawInvalidDimensions |= Uri.UnescapeDataString(request.RequestUri.Query).Contains("300x450", StringComparison.Ordinal);
            return base.SendAsync(request, token);
        }
    }

    private sealed class PreferenceHandler : HttpMessageHandler
    {
        public bool UsedEnteredKey { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            UsedEnteredKey = request.Headers.Authorization?.Parameter == "entered-fixture-key";
            return Task.FromResult(Json("""{"success":true,"data":[]}"""));
        }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new HttpRequestException("La prueba offline no permite red.");
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("games/steam/")) return Task.FromResult(Json("""{"success":true,"data":{"id":123,"name":"Hades"}}"""));
            if (path.Contains("search/")) return Task.FromResult(Json("""{"success":true,"data":[{"id":123,"name":"Hades"}]}"""));
            if (path.Contains("grids/game/")) return Task.FromResult(Json("""
                {"success":true,"data":[
                {"id":1,"url":"https://cdn.example.com/a.png","thumb":"https://cdn.example.com/ta.png","width":600,"height":900},
                {"id":2,"url":"https://cdn.example.com/b.png","thumb":"https://cdn.example.com/tb.png","width":600,"height":900}]}
                """));
            var pixels = Enumerable.Repeat((byte)180, 160 * 240 * 4).ToArray();
            var source = BitmapSource.Create(160, 240, 96, 96, PixelFormats.Bgra32, null, pixels, 640);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(stream.ToArray()) });
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class NoLaunch : IGameLauncher
    { public Task<LaunchResult> LaunchAsync(Game game) => throw new InvalidOperationException("Esta prueba nunca ejecuta juegos."); }
}
