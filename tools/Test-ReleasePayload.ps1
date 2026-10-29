param([Parameter(Mandatory = $true)][string]$Directory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Directory).Path
$forbidden = @()
foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
    $relative = $file.FullName.Substring($root.Length + 1)
    if ($file.Name -match '^(settings|library)(\..*)?\.json$|^\.env($|\.)|^secrets\.json$|^\.instance\.lock$' -or
        $file.Extension -in '.pfx','.p12','.key','.pem','.log','.bak','.tmp','.dmp','.pdb','.suo','.user','.cs','.csproj' -or
        $relative -match '(^|[\\/])(measurements|TestResults|covers|\.git|\.vs)([\\/]|$)') {
        $forbidden += $relative
    }
}
if ($forbidden.Count -gt 0) {
    throw ('El paquete contiene archivos personales, sensibles o de desarrollo: ' + ($forbidden -join ', '))
}
foreach ($name in @('Viciotek.exe','Viciotek.dll','Viciotek.runtimeconfig.json','COPYRIGHT.md','THIRD_PARTY_NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $name))) { throw "Falta $name en el paquete." }
}
Write-Output 'Payload validado: sin bibliotecas personales, claves privadas, logs ni fuentes.'
