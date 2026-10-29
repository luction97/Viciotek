param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$Icon = (Join-Path $PSScriptRoot '..\src\Viciotek\Assets\viciotek.ico')
)

$ErrorActionPreference = 'Stop'
if (-not ('ViciotekIconResources' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class ViciotekIconResources {
    private delegate bool Callback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, Callback callback, IntPtr parameter);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    public static byte[][] Read(string path) {
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x22); // Data only: never execute the binary.
        if (module == IntPtr.Zero) throw new Win32Exception();
        try {
            var frames = new List<byte[]>();
            Callback callback = delegate(IntPtr m, IntPtr type, IntPtr name, IntPtr p) {
                var resource = FindResource(m, name, type);
                var data = new byte[checked((int)SizeofResource(m, resource))];
                Marshal.Copy(LockResource(LoadResource(m, resource)), data, 0, data.Length);
                frames.Add(data);
                return true;
            };
            if (!EnumResourceNames(module, new IntPtr(3), callback, IntPtr.Zero)) throw new Win32Exception();
            return frames.ToArray();
        } finally { FreeLibrary(module); }
    }
}
'@
}
$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Icon).Path)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $embedded = @([ViciotekIconResources]::Read((Resolve-Path -LiteralPath $Executable).Path) | ForEach-Object {
        [Convert]::ToBase64String($sha.ComputeHash([byte[]]$_))
    })
    $count = [BitConverter]::ToUInt16($bytes, 4)
    for ($i = 0; $i -lt $count; $i++) {
        $length = [BitConverter]::ToInt32($bytes, 6 + 16 * $i + 8)
        $offset = [BitConverter]::ToInt32($bytes, 6 + 16 * $i + 12)
        $hash = [Convert]::ToBase64String($sha.ComputeHash($bytes, $offset, $length))
        if ($hash -notin $embedded) { throw "Falta el marco oficial $i en $Executable." }
    }
    Write-Output "Icono incrustado verificado: $Executable ($count marcos oficiales, SHA256 identicos)."
} finally { $sha.Dispose() }
