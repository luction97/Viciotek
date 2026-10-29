using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Viciotek.Infrastructure.Shell;

/// <summary>
/// A single writer per data directory. Windows releases the handle even after
/// a crash; the remaining file is metadata, not a stale lock to be deleted.
/// </summary>
public sealed class LibraryInstance : IDisposable
{
    private const string FileName = ".instance.lock";
    private readonly FileStream _lock;

    private LibraryInstance(FileStream stream)
    {
        _lock = stream;
    }

    public static LibraryInstance? TryAcquire(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var stream = new FileStream(
                Path.Combine(dataDirectory, FileName), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.Read);
            try
            {
                stream.SetLength(0);
                return new LibraryInstance(stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
        {
            return null;
        }
    }

    public void SetWindowHandle(IntPtr handle)
    {
        Span<byte> data = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(data, Environment.ProcessId);
        BinaryPrimitives.WriteInt64LittleEndian(data[4..], handle.ToInt64());
        _lock.Position = 0;
        _lock.Write(data);
        _lock.Flush();
    }

    public static void ActivateExisting(string dataDirectory)
    {
        try
        {
            using var reader = new FileStream(Path.Combine(dataDirectory, FileName),
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> data = stackalloc byte[12];
            if (reader.Length != data.Length)
            {
                return; // The first instance is still creating its window.
            }
            reader.ReadExactly(data);
            var processId = BinaryPrimitives.ReadInt32LittleEndian(data);
            var handle = new IntPtr(BinaryPrimitives.ReadInt64LittleEndian(data[4..]));
            GetWindowThreadProcessId(handle, out var actualProcessId);
            if (processId <= 0 || actualProcessId != (uint)processId)
            {
                return;
            }
            if (IsIconic(handle))
            {
                ShowWindow(handle, 9); // SW_RESTORE
            }
            SetForegroundWindow(handle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A concurrent close/start must never display an extra dialog.
        }
    }

    public void Dispose() => _lock.Dispose();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);
}
