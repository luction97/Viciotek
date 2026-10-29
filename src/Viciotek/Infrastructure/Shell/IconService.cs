using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Viciotek.Infrastructure.Caching;
using WpfImaging = System.Windows.Interop.Imaging;

namespace Viciotek.Infrastructure.Shell;

/// <summary>
/// Extracts high-resolution shell icons without third-party libraries. Shell
/// COM requires an STA thread, so a single dedicated worker owns all COM calls;
/// results are frozen and safe to use from the UI thread.
/// </summary>
public sealed class IconService : IDisposable
{
    private const int IconSize = 128;
    private const uint SiigbfBiggerSizeOk = 0x00000001;
    private const uint SiigbfIconOnly = 0x00000004;
    private const uint CoInitApartmentThreaded = 0x00000002;

    private readonly LruCache<string, BitmapSource?> _cache = new(32);
    private readonly BlockingCollection<IconRequest> _requests = new();
    private readonly Thread _worker;
    private readonly object _workerGate = new();
    private bool _started;
    private bool _disposed;

    public IconService()
    {
        _worker = new Thread(ProcessRequests)
        {
            IsBackground = true,
            Name = "Viciotek.IconWorker"
        };
        _worker.SetApartmentState(ApartmentState.STA);
    }

    internal bool IsWorkerStarted => _started;

    public async Task<BitmapSource?> LoadAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (_cache.TryGet(fullPath, out var cached))
        {
            return cached;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        var icon = await RequestIconAsync(fullPath).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        _cache.Set(fullPath, icon);
        return icon;
    }

    private Task<BitmapSource?> RequestIconAsync(string path)
    {
        var completion = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_workerGate)
        {
            if (_disposed)
            {
                completion.TrySetResult(null);
                return completion.Task;
            }

            _requests.Add(new IconRequest(path, completion));
            if (!_started)
            {
                _worker.Start();
                _started = true;
            }
        }

        return completion.Task;
    }

    private void ProcessRequests()
    {
        var initialized = CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded) >= 0;
        try
        {
            foreach (var request in _requests.GetConsumingEnumerable())
            {
                BitmapSource? icon = null;
                try
                {
                    icon = ExtractIcon(request.Path);
                }
                catch (Exception)
                {
                    // A missing icon is a cosmetic problem, never an error.
                }

                request.Completion.TrySetResult(icon);
            }
        }
        finally
        {
            if (initialized)
            {
                CoUninitialize();
            }
        }
    }

    private static BitmapSource? ExtractIcon(string path)
    {
        var shellIcon = TryShellIcon(path);
        if (shellIcon is not null && shellIcon.PixelWidth >= 64)
        {
            return shellIcon;
        }

        var extracted = TryPrivateExtract(path);
        if (extracted is not null &&
            (shellIcon is null || extracted.PixelWidth > shellIcon.PixelWidth))
        {
            return extracted;
        }

        return shellIcon ?? TryLegacyIcon(path);
    }

    private static BitmapSource? TryShellIcon(string path)
    {
        IntPtr hBitmap = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            factory.GetImage(
                new NativeSize(IconSize, IconSize),
                SiigbfIconOnly | SiigbfBiggerSizeOk,
                out hBitmap);

            if (hBitmap == IntPtr.Zero)
            {
                return null;
            }

            var image = WpfImaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (factory is not null && Marshal.IsComObject(factory))
            {
                Marshal.FinalReleaseComObject(factory);
            }

            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
        }
    }

    private static BitmapSource? TryPrivateExtract(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".com", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var icons = new IntPtr[1];
        var count = PrivateExtractIcons(
            path,
            0,
            IconSize,
            IconSize,
            icons,
            IntPtr.Zero,
            1,
            0);
        if (count == 0 || icons[0] == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var image = WpfImaging.CreateBitmapSourceFromHIcon(
                icons[0],
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            DestroyIcon(icons[0]);
        }
    }

    private static BitmapSource? TryLegacyIcon(string path)
    {
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);
        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var image = WpfImaging.CreateBitmapSourceFromHIcon(
                info.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }

    public void Dispose()
    {
        lock (_workerGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _requests.CompleteAdding();
        }

        if (_started)
        {
            _worker.Join(TimeSpan.FromSeconds(2));
        }
    }

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    private sealed record IconRequest(
        string Path,
        TaskCompletionSource<BitmapSource?> Completion);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize
    {
        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShellFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        [In, Out] IntPtr[] phicon,
        IntPtr piconid,
        uint nIcons,
        uint flags);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
