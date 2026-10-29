param(
    [int]$Count = 500,
    [string]$SourceDir = (Join-Path $env:TEMP 'viciotek-ui'),
    [string]$TargetDir = (Join-Path $env:TEMP "viciotek-perf\n$Count")
)

# Generates a large, realistic library by cloning the visual seed library.
# Covers reuse illustration bytes but have distinct per-game revisions, so
# cache/virtualization measurements do not benefit from identical cache keys.
# This is a developer tool for startup/memory measurements, not app code.

$ErrorActionPreference = 'Stop'

$sourceLibraryPath = Join-Path $SourceDir 'library.json'
if (-not (Test-Path $sourceLibraryPath)) {
    throw "No existe $sourceLibraryPath. Ejecutá tools/SeedUiData.ps1 primero."
}

$template = Get-Content -LiteralPath $sourceLibraryPath -Raw | ConvertFrom-Json
$templates = @($template.games)
if ($templates.Count -eq 0) {
    throw "La biblioteca de origen está vacía."
}

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
if (Test-Path -LiteralPath (Join-Path $TargetDir 'library.json')) {
    throw 'La biblioteca de destino ya existe. Usa una carpeta de prueba nueva.'
}
$targetCovers = Join-Path $TargetDir 'covers'
$targetThumbs = Join-Path $targetCovers 'thumbs'
New-Item -ItemType Directory -Force -Path $targetCovers, $targetThumbs | Out-Null

$sourceCovers = Join-Path $SourceDir 'covers'
$sourceThumbs = Join-Path $sourceCovers 'thumbs'

$library = New-Object System.Collections.Generic.List[object]
$now = [DateTimeOffset]::UtcNow

for ($index = 0; $index -lt $Count; $index++) {
    $origin = $templates[$index % $templates.Count]
    $cycle = [int][Math]::Floor($index / $templates.Count) + 1
    $id = [Guid]::NewGuid()
    $name = if ($cycle -eq 1) { $origin.name } else { "$($origin.name) $cycle" }
    $coverFileName = $origin.coverFileName

    if ($coverFileName) {
        $sourceCover = Join-Path $sourceCovers $coverFileName
        $coverFileName = "$($id.ToString('N'))-$([Guid]::NewGuid().ToString('N'))$([System.IO.Path]::GetExtension($coverFileName))"
        Copy-Item -LiteralPath $sourceCover -Destination (Join-Path $targetCovers $coverFileName)
        $sourceThumb = Join-Path $sourceThumbs "$(([Guid]$origin.id).ToString('N')).jpg"
        if (Test-Path $sourceThumb) {
            $thumbnailName = [System.IO.Path]::GetFileNameWithoutExtension($coverFileName) + '.jpg'
            Copy-Item -LiteralPath $sourceThumb -Destination (Join-Path $targetThumbs $thumbnailName)
        }
        else {
            $coverFileName = $null
        }
    }

    $library.Add([ordered]@{
        id               = $id
        name             = $name
        launchPath       = $origin.launchPath
        launchKind       = 'executable'
        workingDirectory = $origin.workingDirectory
        iconPath         = $origin.iconPath
        coverFileName    = $coverFileName
        sortOrder        = $index
        addedAt          = $now.AddMinutes(-$index).ToString('o')
    })
}

$document = [ordered]@{
    schemaVersion = 1
    games         = $library
}
$document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $TargetDir 'library.json') -Encoding UTF8

$settings = [ordered]@{
    autoFetchCovers = $false
    windowWidth     = 1280
    windowHeight    = 800
    windowMaximized = $false
}
$settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $TargetDir 'settings.json') -Encoding UTF8

$withCover = @($library | Where-Object { $_.coverFileName }).Count
Write-Output "Seeded $Count games in $TargetDir ($withCover covers, $($Count - $withCover) icon fallbacks)."
