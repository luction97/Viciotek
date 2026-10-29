using Viciotek.Infrastructure.Shell;

namespace Viciotek.Tests;

public sealed class InstallerPolicyTests
{
    private static string Script => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RepoFixtures", "Viciotek.iss"));

    [Fact]
    public void Installer_IsPerUser_WithoutElevationOrForcedProcessTermination()
    {
        Assert.Contains("PrivilegesRequired=lowest", Script);
        Assert.Contains(@"DefaultDirName={localappdata}\Programs\VICIOTEK", Script);
        Assert.Contains("CloseApplications=no", Script);
        Assert.Contains("RestartApplications=no", Script);
        Assert.DoesNotContain("PrivilegesRequiredOverridesAllowed", Script);
    }

    [Fact]
    public void DesktopShortcut_IsUnconditionalAndTargetsInstalledExecutable()
    {
        Assert.Contains("#define DesktopRoot \"{autodesktop}\"", Script);
        var icon = Script.Split('\n').Single(line => line.StartsWith("Name: \"{#DesktopRoot}", StringComparison.Ordinal));
        Assert.Contains(@"\VICIOTEK""; Filename: ""{app}\Viciotek.exe""", icon);
        Assert.DoesNotContain("Tasks:", icon);
        Assert.Contains("AllowNoIcons=no", Script);
    }

    [Fact]
    public void Uninstaller_HasNoRecursiveOrPersonalDataDeletionRules()
    {
        Assert.DoesNotContain("[UninstallDelete]", Script);
        Assert.DoesNotContain("[InstallDelete]", Script);
        Assert.DoesNotContain(@"{localappdata}\Viciotek", Script);
        Assert.DoesNotContain("filesandordirs", Script);
        Assert.Contains("CreateUninstallRegKey=no", Script); // Sandbox compilation cannot pollute HKCU.
    }

    [Fact]
    public void InstallerAndApplication_AgreeOnPresenceMutex() =>
        Assert.Contains("AppMutex=" + InstallationGuard.MutexName, Script);

    [Fact]
    public void PresenceMutex_AllowsMultipleIsolatedLibraries_AndDisappearsAfterLastHandle()
    {
        var name = @"Local\Viciotek.Installer.Tests." + Guid.NewGuid().ToString("N");
        using (var first = new InstallationGuard(name))
        {
            using (var second = new InstallationGuard(name))
            {
                Assert.True(Mutex.TryOpenExisting(name, out var handle));
                handle!.Dispose();
            }
            Assert.True(Mutex.TryOpenExisting(name, out var remaining));
            remaining!.Dispose();
        }
        Assert.False(Mutex.TryOpenExisting(name, out var closed));
        closed?.Dispose();
    }

    [Fact]
    public void CI_DoesNotUsePrivilegedPullRequestTrigger_AndPinsActionCommits()
    {
        var ci = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RepoFixtures", "ci.yml"));
        Assert.Contains("contents: read", ci);
        Assert.Contains("persist-credentials: false", ci);
        Assert.DoesNotContain("pull_request_target", ci);
        var actions = ci.Split('\n').Where(line => line.Contains("uses: actions/", StringComparison.Ordinal));
        Assert.All(actions, action => Assert.Matches(@"uses: actions/[a-z-]+@[a-f0-9]{40}", action));
        Assert.Contains("--locked-mode", ci);
        Assert.Contains("--redact", ci);
    }
}
