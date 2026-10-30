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
            error = L.Get("TheNameCannotBeEmpty");
            return false;
        }

        var cleanTarget = (target ?? string.Empty).Trim().Trim('"');
        if (cleanTarget.Length == 0)
        {
            error = L.Get("TheDestinationCannotBeEmpty");
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
                error = L.Get("TheUrlFileNoLongerExists");
                return false;
            }

            if (!UrlShortcutReader.TryRead(cleanTarget, out var shortcut, out var readError) || shortcut is null)
            {
                error = readError ?? L.Get("CouldNotReadTheUrlFile");
                return false;
            }

            if (!ProtocolPolicy.TryValidate(shortcut.Url, out var shortcutUri, out var protocolError))
            {
                error = protocolError ?? L.Get("TheShortcutProtocolIsNotAllowed");
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
            error = L.Get("TheExecutablePathIsNotValid");
            return false;
        }

        if (!LaunchTargetRules.IsAllowedProgram(fullPath))
        {
            error = L.Get("ChooseAnExeFileAUrlFileOrASupportedLauncherLink");
            return false;
        }

        if (!File.Exists(fullPath))
        {
            error = L.Get("TheExecutableDoesNotExistAtThatLocation");
            return false;
        }

        var cleanArguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
        if (cleanArguments is { Length: > 16_000 })
        {
            error = L.Get("TheLaunchArgumentsAreTooLong");
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
                error = L.Get("TheWorkingFolderIsNotValid");
                return false;
            }

            if (!Directory.Exists(cleanWorkingDirectory))
            {
                error = L.Get("TheWorkingFolderDoesNotExist");
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
