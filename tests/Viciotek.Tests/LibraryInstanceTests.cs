using System.Diagnostics;
using System.Text;
using Viciotek.Infrastructure.Shell;

namespace Viciotek.Tests;

public sealed class LibraryInstanceTests
{
    [Fact]
    public void SameLibrary_RejectsSecondWriterAndCanReopenAfterDispose()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            using (var first = LibraryInstance.TryAcquire(directory))
            {
                Assert.NotNull(first);
                using var second = LibraryInstance.TryAcquire(Path.Combine(directory, "."));
                Assert.Null(second);
            }
            using var reopened = LibraryInstance.TryAcquire(directory);
            Assert.NotNull(reopened);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DifferentLibraries_CanBeOpenConcurrently()
    {
        var directory = TestHelpers.CreateTempDirectory();
        try
        {
            using var first = LibraryInstance.TryAcquire(Path.Combine(directory, "first"));
            using var second = LibraryInstance.TryAcquire(Path.Combine(directory, "second"));
            Assert.NotNull(first);
            Assert.NotNull(second);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TerminatedProcess_ReleasesLockWithoutDeletingTheMetadataFile()
    {
        var directory = TestHelpers.CreateTempDirectory();
        using var process = new Process();
        var started = false;
        var completed = false;
        try
        {
            var lockPath = Path.Combine(directory, ".instance.lock").Replace("'", "''");
            var script = $"$ErrorActionPreference='Stop'; $lock=[System.IO.File]::Open('{lockPath}', 'OpenOrCreate', 'ReadWrite', 'Read'); Write-Output 'ready'; Start-Sleep -Seconds 30";
            process.StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            started = process.Start();
            Assert.True(started);
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            using (var denied = LibraryInstance.TryAcquire(directory))
            {
                Assert.Null(denied);
            }
            process.Kill();
            await process.WaitForExitAsync();
            LibraryInstance? recovered = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (recovered is null && DateTime.UtcNow < deadline)
            {
                recovered = LibraryInstance.TryAcquire(directory);
                if (recovered is null)
                {
                    await Task.Delay(20);
                }
            }
            using (recovered)
            {
                Assert.NotNull(recovered);
            }
            completed = true;
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
            if (completed)
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
