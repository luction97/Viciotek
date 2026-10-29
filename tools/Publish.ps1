param(
    [ValidateSet('Portable', 'FrameworkDependent')]
    [string]$Variant = 'Portable',
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\dist')
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -Force
$project = Join-Path $PSScriptRoot '..\src\Viciotek\Viciotek.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
$suffix = if ($Variant -eq 'Portable') { 'win-x64' } else { 'win-x64-framework-dependent' }
$name = "Viciotek-$version-$suffix"
$folder = Join-Path $OutputRoot $name
$archive = Join-Path $OutputRoot "$name.zip"

# Never mix an existing distribution with a new publish or delete user files.
if ((Test-Path -LiteralPath $folder) -or (Test-Path -LiteralPath $archive)) {
    throw "La salida ya existe: $folder. Elegi otro -OutputRoot para una nueva publicacion."
}

$selfContained = if ($Variant -eq 'Portable') { 'true' } else { 'false' }
& dotnet publish $project -c Release -r win-x64 --self-contained $selfContained `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
    --nologo -o $folder
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo ($LASTEXITCODE)." }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\README.md') -Destination (Join-Path $folder 'README.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\COPYRIGHT.md') -Destination (Join-Path $folder 'COPYRIGHT.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\THIRD_PARTY_NOTICES.md') -Destination (Join-Path $folder 'THIRD_PARTY_NOTICES.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\SECURITY.md') -Destination (Join-Path $folder 'SECURITY.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\CONTRIBUTING.md') -Destination (Join-Path $folder 'CONTRIBUTING.md')
[IO.Directory]::CreateDirectory((Join-Path $folder 'docs')) | Out-Null
# Only curated documentation: raw measurements can contain usernames and local paths.
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\docs') -Filter '*.md' -File | Copy-Item -Destination (Join-Path $folder 'docs')
& (Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1') -Directory $folder
Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
"$($hash.Hash)  $name.zip" | Set-Content -LiteralPath "$archive.sha256" -Encoding ASCII
Write-Output "Distribucion: $folder"
Write-Output "ZIP: $archive"
Write-Output "SHA256: $($hash.Hash)"
