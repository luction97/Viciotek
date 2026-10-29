using System.Runtime.InteropServices;
using System.Text;
using Viciotek.Application;

namespace Viciotek.Infrastructure.Shell;

public sealed class ShellShortcutResolver : IShortcutResolver
{
    private const int MaxPath = 32_768;
    private const uint SlrNoUi = 0x0001;
    private const uint SlrNoSearch = 0x0010;

    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");

    public ShortcutInfo Resolve(string shortcutPath)
    {
        var type = Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: true)
                   ?? throw new InvalidOperationException("No se pudo crear el objeto ShellLink.");
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(type)
                       ?? throw new InvalidOperationException("No se pudo crear el objeto ShellLink.");

            var link = (IShellLinkW)instance;
            var persistFile = (IPersistFile)instance;
            persistFile.Load(shortcutPath, 0);

            try
            {
                link.Resolve(IntPtr.Zero, SlrNoUi | SlrNoSearch);
            }
            catch (COMException)
            {
                // GetPath can still return the stored target when resolution fails.
            }

            var target = new StringBuilder(MaxPath);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);

            var workingDirectory = new StringBuilder(MaxPath);
            link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);

            var arguments = new StringBuilder(MaxPath);
            link.GetArguments(arguments, arguments.Capacity);

            return new ShortcutInfo(
                target.ToString(),
                arguments.ToString(),
                workingDirectory.ToString());
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cch,
            IntPtr pfd,
            uint fFlags);

        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
