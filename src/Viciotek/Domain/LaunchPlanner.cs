namespace Viciotek.Domain;

public sealed record LaunchPlan(
    string FileName,
    string? Arguments,
    string? WorkingDirectory,
    LaunchMode Mode);

public enum LaunchMode
{
    Direct,
    Shell
}

public sealed record LaunchValidation(LaunchPlan? Plan, string? Error)
{
    public bool IsValid => Plan is not null;

    public static LaunchValidation Valid(LaunchPlan plan) => new(plan, null);
    public static LaunchValidation Invalid(string error) => new(null, error);
}

/// <summary>
/// Validates a library entry and turns it into a concrete launch request.
/// No process is started here, which keeps this logic testable.
/// </summary>
public static class LaunchPlanner
{
    public static LaunchValidation CreatePlan(Game game)
    {
        if (game is null)
        {
            return LaunchValidation.Invalid(L.Get("TheGameIsNotValid"));
        }

        if (string.IsNullOrWhiteSpace(game.Name))
        {
            return LaunchValidation.Invalid(L.Get("TheGameHasNoName"));
        }

        var launchPath = (game.LaunchPath ?? string.Empty).Trim();
        if (launchPath.Length == 0)
        {
            return LaunchValidation.Invalid(L.Get("TheGameHasNoDestinationSet"));
        }

        return game.LaunchKind switch
        {
            LaunchKind.Uri => CreateUriPlan(launchPath),
            LaunchKind.Shortcut => CreateShortcutPlan(launchPath),
            _ => CreateExecutablePlan(game, launchPath)
        };
    }

    private static LaunchValidation CreateUriPlan(string launchPath)
    {
        if (!ProtocolPolicy.TryValidate(launchPath, out var uri, out var error))
        {
            return LaunchValidation.Invalid(error ?? L.Get("TheProtocolIsNotValid"));
        }

        return LaunchValidation.Valid(new LaunchPlan(uri.AbsoluteUri, null, null, LaunchMode.Shell));
    }

    private static LaunchValidation CreateShortcutPlan(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
        {
            return LaunchValidation.Invalid(
                L.Get("TheShortcutNoLongerExistsEditTheGameToChooseAValidDestination"));
        }

        var extension = Path.GetExtension(shortcutPath);
        if (!string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchValidation.Invalid(L.Get("TheDestinationIsNotASupportedShortcut"));
        }

        return LaunchValidation.Valid(new LaunchPlan(shortcutPath, null, null, LaunchMode.Shell));
    }

    private static LaunchValidation CreateExecutablePlan(Game game, string launchPath)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(launchPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return LaunchValidation.Invalid(L.Get("TheExecutablePathIsNotValid"));
        }

        if (!LaunchTargetRules.IsAllowedProgram(fullPath))
        {
            return LaunchValidation.Invalid(L.Get("OnlyExeAndComFilesCanBeLaunched"));
        }

        if (!File.Exists(fullPath))
        {
            return LaunchValidation.Invalid(
                L.Get("TheExecutableNoLongerExistsAtThatLocationEditTheGameToChooseAnotherDestination"));
        }

        if ((game.Arguments?.Length ?? 0) > 16_000)
        {
            return LaunchValidation.Invalid(L.Get("TheLaunchArgumentsAreTooLong"));
        }

        var workingDirectory = game.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(fullPath);
        }

        return LaunchValidation.Valid(new LaunchPlan(
            fullPath,
            string.IsNullOrWhiteSpace(game.Arguments) ? null : game.Arguments,
            workingDirectory,
            LaunchMode.Direct));
    }
}
