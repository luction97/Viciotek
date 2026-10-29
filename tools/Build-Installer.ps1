param(
    [string]$CompilerPath,
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\dist'),
    [string]$PublishDirectory,
    [string]$SmokeRoot
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -Force
$project = Join-Path $PSScriptRoot '..\src\Viciotek\Viciotek.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version invalida para el instalador.' }
if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $CompilerPath = $command.Source }
    else { throw 'Indica -CompilerPath. tools/Get-BuildTools.ps1 prepara Inno Setup portatil con SHA256 y Authenticode.' }
}
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$suffix = if ($SmokeRoot) { '-smoke' } else { '' }
$installer = Join-Path $OutputRoot "Viciotek-$version-setup-win-x64$suffix.exe"
if (Test-Path -LiteralPath $installer) { throw "La salida ya existe: $installer. Elegi otro -OutputRoot." }

if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $OutputRoot "Viciotek-$version-win-x64"
    if (-not (Test-Path -LiteralPath $PublishDirectory)) {
        & (Join-Path $PSScriptRoot 'Publish.ps1') -OutputRoot $OutputRoot
    }
}
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'coreclr.dll'))) { throw 'El instalador requiere publicacion x64 autocontenida.' }
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishDirectory 'Viciotek.exe')).FileVersion
if ($fileVersion -ne "$version.0") { throw 'El payload no coincide con la version del proyecto.' }
& (Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1') -Directory $PublishDirectory

# Define values are compiler data, not executable PowerShell. Reject directive/quote injection.
foreach ($value in @($OutputRoot, $PublishDirectory, $SmokeRoot)) {
    if ($value -and $value.IndexOfAny([char[]]@('"',"`r","`n")) -ge 0) { throw 'Ruta con caracteres no admitidos.' }
}
[IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
$arguments = @('/Qp', "/DAppVersion=$version", "/DPublishDir=$PublishDirectory", "/DOutputRoot=$OutputRoot")
if ($SmokeRoot) { $arguments += "/DSmokeRoot=$([IO.Path]::GetFullPath($SmokeRoot))" }
& $CompilerPath @arguments (Join-Path $PSScriptRoot '..\installer\Viciotek.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC fallo ($LASTEXITCODE)." }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding ASCII
Write-Output "Instalador: $installer"
Write-Output "SHA256: $hash"
