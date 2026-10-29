using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Viciotek.Infrastructure.Processes;

public sealed record LaunchResult(bool Success, string? Error)
{
    public static LaunchResult Ok() => new(true, null);
    public static LaunchResult Fail(string error) => new(false, error);
}

public interface IGameLauncher
{
    Task<LaunchResult> LaunchAsync(Domain.Game game);
}

public sealed class GameLauncher : IGameLauncher
{
    public Task<LaunchResult> LaunchAsync(Domain.Game game)
    {
        var validation = Domain.LaunchPlanner.CreatePlan(game);
        if (!validation.IsValid || validation.Plan is null)
        {
            return Task.FromResult(LaunchResult.Fail(validation.Error ?? "No se pudo preparar el juego."));
        }

        var plan = validation.Plan;
        return Task.Run(() => Launch(plan));
    }

    private static LaunchResult Launch(Domain.LaunchPlan plan)
    {
        try
        {
            if (plan.Mode == Domain.LaunchMode.Shell)
            {
                using var shellProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = plan.FileName,
                    UseShellExecute = true
                });
                return LaunchResult.Ok();
            }

            var arguments = WindowsCommandLine.Split(plan.Arguments);
            var startInfo = new ProcessStartInfo
            {
                FileName = plan.FileName,
                WorkingDirectory = plan.WorkingDirectory ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(startInfo);
                return LaunchResult.Ok();
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 740)
            {
                // Some games request elevation in their manifest. Retry through
                // ShellExecute so Windows can show the UAC prompt.
                var elevatedInfo = new ProcessStartInfo
                {
                    FileName = plan.FileName,
                    Arguments = WindowsCommandLine.Join(arguments),
                    WorkingDirectory = plan.WorkingDirectory ?? string.Empty,
                    UseShellExecute = true
                };
                using var elevatedProcess = Process.Start(elevatedInfo);
                return LaunchResult.Ok();
            }
        }
        catch (Win32Exception exception)
        {
            return LaunchResult.Fail(DescribeWin32Error(exception));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return LaunchResult.Fail("Windows no pudo iniciar el juego: " + exception.Message);
        }
    }

    private static string DescribeWin32Error(Win32Exception exception) =>
        exception.NativeErrorCode switch
        {
            2 or 3 => "Windows no encontró el ejecutable del juego.",
            5 => "Windows denegó el acceso al ejecutable.",
            740 => "El juego requiere permisos de administrador.",
            1155 => "Windows no tiene un controlador para este protocolo de lanzamiento.",
            1223 => "Se canceló la solicitud de permisos de Windows.",
            193 => "El destino no es un ejecutable válido.",
            _ => $"Windows no pudo iniciar el juego (error {exception.NativeErrorCode})."
        };
}

/// <summary>
/// Windows-compatible argument parsing/quoting. Used to preserve shortcut
/// arguments and only for the rare ShellExecute elevation fallback.
/// </summary>
public static class WindowsCommandLine
{
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        var wrapped = "\"__viciotek__.exe\" " + commandLine;
        var argv = CommandLineToArgvW(wrapped, out var count);
        if (argv == IntPtr.Zero)
        {
            return Array.Empty<string>();
        }

        try
        {
            var result = new List<string>(Math.Max(0, count - 1));
            for (var index = 1; index < count; index++)
            {
                var pointer = Marshal.ReadIntPtr(argv, index * IntPtr.Size);
                var value = Marshal.PtrToStringUni(pointer);
                if (value is not null)
                {
                    result.Add(value);
                }
            }

            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    public static string Join(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        if (!argument.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var result = new System.Text.StringBuilder(argument.Length + 8);
        result.Append('"');
        var backslashes = 0;

        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', (backslashes * 2) + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                result.Append('\\', backslashes);
                backslashes = 0;
            }

            result.Append(character);
        }

        if (backslashes > 0)
        {
            result.Append('\\', backslashes * 2);
        }

        result.Append('"');
        return result.ToString();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
        out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
