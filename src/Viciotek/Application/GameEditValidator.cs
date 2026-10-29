using Viciotek.Domain;

namespace Viciotek.Application;

public sealed record GameEditValues(
    string Name,
    string Target,
    LaunchKind LaunchKind,
    string? Arguments,
    string? WorkingDirectory);

public static class GameEditValidator
{
    public static bool TryCreate(
        string? name,
        string? target,
        string? arguments,
        string? workingDirectory,
        out GameEditValues? values,
        out string? error)
    {
        values = null;
        error = null;

        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length == 0)
        {
            error = "El nombre no puede estar vacío.";
            return false;
        }

        var cleanTarget = (target ?? string.Empty).Trim().Trim('"');
        if (cleanTarget.Length == 0)
        {
            error = "El destino no puede estar vacío.";
            return false;
        }

        if (ProtocolPolicy.TryValidate(cleanTarget, out var uri, out _))
        {
            values = new GameEditValues(cleanName, uri.AbsoluteUri, LaunchKind.Uri, null, null);
            return true;
        }

        if (Path.GetExtension(cleanTarget).Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(cleanTarget))
            {
                error = "El archivo .url ya no existe.";
                return false;
            }

            if (!UrlShortcutReader.TryRead(cleanTarget, out var shortcut, out var readError) || shortcut is null)
            {
                error = readError ?? "No se pudo leer el archivo .url.";
                return false;
            }

            if (!ProtocolPolicy.TryValidate(shortcut.Url, out var shortcutUri, out var protocolError))
            {
                error = protocolError ?? "El protocolo del acceso directo no está permitido.";
                return false;
            }

            values = new GameEditValues(cleanName, shortcutUri.AbsoluteUri, LaunchKind.Uri, null, null);
            return true;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(cleanTarget);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "La ruta del ejecutable no es válida.";
            return false;
        }

        if (!LaunchTargetRules.IsAllowedProgram(fullPath))
        {
            error = "El destino debe ser un ejecutable .exe, un .url o una URL de launcher compatible.";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            error = "El ejecutable no existe en esa ubicación.";
            return false;
        }

        var cleanArguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
        if (cleanArguments is { Length: > 16_000 })
        {
            error = "Los argumentos son demasiado largos.";
            return false;
        }

        string? cleanWorkingDirectory = null;
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            try
            {
                cleanWorkingDirectory = Path.GetFullPath(workingDirectory.Trim().Trim('"'));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                error = "El directorio de trabajo no es válido.";
                return false;
            }

            if (!Directory.Exists(cleanWorkingDirectory))
            {
                error = "El directorio de trabajo no existe.";
                return false;
            }
        }

        values = new GameEditValues(
            cleanName,
            fullPath,
            LaunchKind.Executable,
            cleanArguments,
            cleanWorkingDirectory);
        return true;
    }
}
