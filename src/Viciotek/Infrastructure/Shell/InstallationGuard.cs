namespace Viciotek.Infrastructure.Shell;

/// <summary>Presence marker for Setup/Uninstall; it does not enforce library uniqueness or close processes.</summary>
public sealed class InstallationGuard : IDisposable
{
    public const string MutexName = @"Local\Viciotek.Application.Running";
    private readonly Mutex _presence;

    public InstallationGuard() : this(MutexName) { }
    internal InstallationGuard(string name) => _presence = new Mutex(initiallyOwned: false, name);

    public void Dispose() => _presence.Dispose();
}
