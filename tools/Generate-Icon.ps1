param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\Viciotek\Assets\icono\viciotek.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Viciotek\Assets\viciotek.ico')
)

# Genera el ICO oficial de VICIOTEK a partir del PNG de marca.
# - Marcos 16..64 en DIB 32bpp (compatibilidad total con shell/instalador).
# - Marcos 128/256 en PNG (menor tamano, soportado desde Windows Vista).
# - Redimensionado bicubico de alta calidad conservando alfa.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$destinationPath = [IO.Path]::GetFullPath($Destination)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

$image = [Drawing.Image]::FromFile($sourcePath)
try {
    if ($image.Width -ne $image.Height) {
        throw "El icono debe ser cuadrado; el origen mide $($image.Width)x$($image.Height)."
    }
    if ($image.Width -lt 256) {
        throw "El origen debe medir al menos 256x256; mide $($image.Width)x$($image.Height)."
    }

    function New-ScaledBitmap([Drawing.Image]$Source, [int]$Size) {
        $bitmap = New-Object Drawing.Bitmap($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bitmap.SetResolution(96, 96)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.Clear([Drawing.Color]::Transparent)
            $attributes = New-Object Drawing.Imaging.ImageAttributes
            try {
                $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
                $rectangle = New-Object Drawing.Rectangle(0, 0, $Size, $Size)
                $graphics.DrawImage(
                    $Source, $rectangle, 0, 0, $Source.Width, $Source.Height,
                    [Drawing.GraphicsUnit]::Pixel, $attributes)
            }
            finally {
                $attributes.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }
        return $bitmap
    }

    function Get-DibBytes([Drawing.Bitmap]$Bitmap) {
        $width = $Bitmap.Width
        $height = $Bitmap.Height
        $pixels = $width * $height * 4
        $maskStride = [int]([Math]::Ceiling($width / 32.0) * 4)
        $maskBytes = $maskStride * $height
        $stream = New-Object IO.MemoryStream
        $writer = New-Object IO.BinaryWriter($stream)
        try {
            # BITMAPINFOHEADER. La altura se duplica: XOR + mascara AND.
            $writer.Write([int]40)
            $writer.Write([int]$width)
            $writer.Write([int]($height * 2))
            $writer.Write([int16]1)
            $writer.Write([int16]32)
            $writer.Write([int]0)
            $writer.Write([int]$pixels)
            $writer.Write([int]0)
            $writer.Write([int]0)
            $writer.Write([int]0)
            $writer.Write([int]0)

            # XOR: BGRA de abajo hacia arriba.
            for ($y = $height - 1; $y -ge 0; $y--) {
                for ($x = 0; $x -lt $width; $x++) {
                    $color = $Bitmap.GetPixel($x, $y)
                    $writer.Write([byte]$color.B)
                    $writer.Write([byte]$color.G)
                    $writer.Write([byte]$color.R)
                    $writer.Write([byte]$color.A)
                }
            }

            # AND: 1 donde el pixel es totalmente transparente.
            for ($y = $height - 1; $y -ge 0; $y--) {
                $row = New-Object byte[] $maskStride
                for ($x = 0; $x -lt $width; $x++) {
                    if ($Bitmap.GetPixel($x, $y).A -eq 0) {
                        $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8))
                    }
                }
                $writer.Write($row)
            }
            $writer.Flush()
            return $stream.ToArray()
        }
        finally {
            $writer.Dispose()
            $stream.Dispose()
        }
    }

    $frames = New-Object System.Collections.Generic.List[object]
    foreach ($size in $sizes) {
        $bitmap = New-ScaledBitmap $image $size
        try {
            if ($size -le 64) {
                # PowerShell desenrolla byte[] al devolverlos por pipeline: forzar byte[].
                [byte[]]$data = Get-DibBytes $bitmap
            }
            else {
                $stream = New-Object IO.MemoryStream
                try {
                    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
                    [byte[]]$data = $stream.ToArray()
                }
                finally {
                    $stream.Dispose()
                }
            }
            if ($data.Length -lt 64) {
                throw "Marco $size generado con $($data.Length) bytes; se esperaba mas."
            }
            $frames.Add([pscustomobject]@{ Size = $size; Data = $data })
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $directory = [IO.Path]::GetDirectoryName($destinationPath)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $file = [IO.File]::Create($destinationPath)
    $writer = New-Object IO.BinaryWriter($file)
    try {
        $writer.Write([int16]0)
        $writer.Write([int16]1)
        $writer.Write([int16]$frames.Count)
        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $payload = [byte[]]$frame.Data
            $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([int16]1)
            $writer.Write([int16]32)
            $writer.Write([int]$payload.Length)
            $writer.Write([int]$offset)
            $offset += $payload.Length
        }
        foreach ($frame in $frames) {
            $payload = [byte[]]$frame.Data
            $writer.Write($payload, 0, $payload.Length)
        }
        $writer.Flush()
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }

    # Verificacion estructural: offsets, longitudes y firma de cada marco.
    $written = [IO.File]::ReadAllBytes($destinationPath)
    if ([BitConverter]::ToUInt16($written, 2) -ne 1 -or [BitConverter]::ToUInt16($written, 4) -ne $frames.Count) {
        throw 'El ICO generado tiene una cabecera invalida.'
    }
    for ($index = 0; $index -lt $frames.Count; $index++) {
        $entry = 6 + (16 * $index)
        $length = [BitConverter]::ToInt32($written, $entry + 8)
        $start = [BitConverter]::ToInt32($written, $entry + 12)
        if ($length -le 0 -or $start -lt 0 -or ($start + $length) -gt $written.Length) {
            throw "El marco $index queda fuera del archivo."
        }
        $isPng = $written[$start] -eq 0x89 -and $written[$start + 1] -eq 0x50 -and
                 $written[$start + 2] -eq 0x4E -and $written[$start + 3] -eq 0x47
        if ($index -ge 7 -and -not $isPng) {
            throw "El marco $index deberia ser PNG."
        }
        if ($index -lt 7 -and $isPng) {
            throw "El marco $index deberia ser DIB."
        }
    }

    Write-Output ("Icono generado: {0} ({1} bytes, {2} marcos: {3})" -f
        $destinationPath, (Get-Item -LiteralPath $destinationPath).Length, $frames.Count, ($sizes -join '/'))
}
finally {
    $image.Dispose()
}
