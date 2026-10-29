param(
    [string]$DataDir = (Join-Path $env:TEMP 'viciotek-seed')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$games = @(
    @{ Name = 'Elden Ring';                 AppId = 1245620 },
    @{ Name = 'Hades';                      AppId = 1145360 },
    @{ Name = 'Celeste';                    AppId = 504230 },
    @{ Name = 'Hollow Knight';              AppId = 367520 },
    @{ Name = 'Cyberpunk 2077';             AppId = 1091500 },
    @{ Name = 'Stardew Valley';             AppId = 413150 },
    @{ Name = 'The Witcher 3';              AppId = 292030 },
    @{ Name = 'Red Dead Redemption 2';      AppId = 1174180 },
    @{ Name = 'Baldur''s Gate 3';           AppId = 1086940 },
    @{ Name = 'Portal 2';                   AppId = 620 },
    @{ Name = 'Half-Life 2';                AppId = 220 },
    @{ Name = 'Disco Elysium';              AppId = 632470 },
    @{ Name = 'Sekiro';                     AppId = 814380 },
    @{ Name = 'DOOM Eternal';               AppId = 782330 },
    @{ Name = 'Ori and the Blind Forest';   AppId = 387290 },
    @{ Name = 'Cuphead';                    AppId = 268910 },
    @{ Name = 'Undertale';                  AppId = 391540 },
    @{ Name = 'Control';                    AppId = 870780 },
    @{ Name = 'God of War';                 AppId = 1593500 },
    @{ Name = 'It Takes Two';               AppId = 1426210 },
    @{ Name = 'Nier Automata';              AppId = 524220 },
    @{ Name = 'Persona 5 Royal';            AppId = 1687950 },
    @{ Name = 'Vampire Survivors';          AppId = 1794680 },
    @{ Name = 'Dave the Diver';             AppId = 1868140 },
    @{ Name = 'Lies of P';                  AppId = 1627720 }
)

$withoutCover = @(
    @{ Name = 'Untitled Adventure'; Icon = 'mspaint.exe' },
    @{ Name = 'Project Nebula';     Icon = 'calc.exe' },
    @{ Name = 'Lost in Orbit';      Icon = 'charmap.exe' },
    @{ Name = 'Paper Kingdom';      Icon = 'explorer.exe' },
    @{ Name = 'The Quiet Place';    Icon = 'magnify.exe' }
)

$coversDir = Join-Path $DataDir 'covers'
$thumbsDir = Join-Path $coversDir 'thumbs'
$gamesDir = Join-Path $DataDir 'seed-games'
New-Item -ItemType Directory -Force -Path $coversDir, $thumbsDir, $gamesDir | Out-Null

$jpegCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object { $_.MimeType -eq 'image/jpeg' } |
    Select-Object -First 1
$encoderParams = New-Object System.Drawing.Imaging.EncoderParameters 1
$encoderParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
    [System.Drawing.Imaging.Encoder]::Quality, [long]88)

function Save-Thumbnail([string]$source, [string]$target) {
    $image = [System.Drawing.Image]::FromFile($source)
    $bitmap = $null
    $graphics = $null
    try {
        $targetRatio = 480.0 / 720.0
        $sourceRatio = $image.Width / [double]$image.Height
        if ($sourceRatio -gt $targetRatio) {
            $cropHeight = $image.Height
            $cropWidth = [int][Math]::Round($image.Height * $targetRatio)
            $cropX = [int](($image.Width - $cropWidth) / 2)
            $cropY = 0
        }
        else {
            $cropWidth = $image.Width
            $cropHeight = [int][Math]::Round($image.Width / $targetRatio)
            $cropX = 0
            $cropY = [int](($image.Height - $cropHeight) / 2)
        }

        $bitmap = New-Object System.Drawing.Bitmap 480, 720
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $destination = New-Object System.Drawing.Rectangle 0, 0, 480, 720
        $graphics.DrawImage(
            $image,
            $destination,
            $cropX,
            $cropY,
            $cropWidth,
            $cropHeight,
            [System.Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save($target, $jpegCodec, $encoderParams)
    }
    finally {
        if ($graphics) { $graphics.Dispose() }
        if ($bitmap) { $bitmap.Dispose() }
        $image.Dispose()
    }
}

$library = New-Object System.Collections.Generic.List[object]
$index = 0

foreach ($entry in $games) {
    $id = [Guid]::NewGuid()
    $coverFileName = $null
    $sourcePath = Join-Path $env:TEMP ("viciotek-cover-$($entry.AppId).jpg")
    $downloaded = $false

    foreach ($asset in @('library_600x900.jpg', 'header.jpg', 'capsule_616x353.jpg')) {
        $url = "https://cdn.cloudflare.steamstatic.com/steam/apps/$($entry.AppId)/$asset"
        try {
            Invoke-WebRequest -Uri $url -OutFile $sourcePath -TimeoutSec 30 -UseBasicParsing
            $downloaded = $true
            break
        }
        catch {
            if (Test-Path $sourcePath) { Remove-Item $sourcePath -Force }
        }
    }

    if ($downloaded) {
        $coverFileName = "$($id.ToString('N'))-$([Guid]::NewGuid().ToString('N')).jpg"
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $coversDir $coverFileName) -Force
        Save-Thumbnail $sourcePath (Join-Path $thumbsDir "$($id.ToString('N')).jpg")
        Remove-Item -LiteralPath $sourcePath -Force -ErrorAction SilentlyContinue
    }

    $safeName = ($entry.Name -replace '[^A-Za-z0-9]+', '-').Trim('-')
    $exePath = Join-Path $gamesDir "$safeName.exe"
    if (-not (Test-Path $exePath)) {
        Copy-Item -LiteralPath "$env:WINDIR\System32\notepad.exe" -Destination $exePath
    }

    $library.Add([ordered]@{
        id           = $id
        name         = $entry.Name
        launchPath   = $exePath
        launchKind   = 'executable'
        workingDirectory = $gamesDir
        iconPath     = $exePath
        coverFileName = $coverFileName
        sortOrder    = $index
        addedAt      = (Get-Date).AddDays(-$index).ToUniversalTime().ToString('o')
    })
    $index++
}

foreach ($entry in $withoutCover) {
    $id = [Guid]::NewGuid()
    $safeName = ($entry.Name -replace '[^A-Za-z0-9]+', '-').Trim('-')
    $exePath = Join-Path $gamesDir "$safeName.exe"
    $iconSource = Join-Path $env:WINDIR "System32\$($entry.Icon)"
    if (Test-Path $iconSource) {
        Copy-Item -LiteralPath $iconSource -Destination $exePath -Force
    }
    else {
        Copy-Item -LiteralPath "$env:WINDIR\System32\notepad.exe" -Destination $exePath -Force
    }

    $library.Add([ordered]@{
        id           = $id
        name         = $entry.Name
        launchPath   = $exePath
        launchKind   = 'executable'
        workingDirectory = $gamesDir
        iconPath     = $exePath
        coverFileName = $null
        sortOrder    = $index
        addedAt      = (Get-Date).AddDays(-$index).ToUniversalTime().ToString('o')
    })
    $index++
}

$document = [ordered]@{
    schemaVersion = 1
    games         = $library
}
$document | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $DataDir 'library.json') -Encoding UTF8

$settings = [ordered]@{
    autoFetchCovers = $false
    windowWidth     = 1280
    windowHeight    = 800
    windowMaximized = $false
}
$settings | ConvertTo-Json | Set-Content -Path (Join-Path $DataDir 'settings.json') -Encoding UTF8

Write-Output "Seeded $($library.Count) games in $DataDir ($(@($library | Where-Object { $_.coverFileName }).Count) covers)."
