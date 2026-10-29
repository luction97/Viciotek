param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
$archive = (Resolve-Path -LiteralPath $ZipPath).Path
$root = Join-Path $env:LOCALAPPDATA ("Temp\opencode\viciotek-smoke-" + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $root 'app'
$data = Join-Path $root 'data'
New-Item -ItemType Directory -Path $install, $data -Force | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $install
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if (Test-Path -LiteralPath "$archive.sha256") {
    $expectedHash = ((Get-Content -LiteralPath "$archive.sha256" -Raw).Trim() -split '\s+')[0]
    if ($actualHash -ne $expectedHash) { throw 'El checksum SHA256 no coincide.' }
}

$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $install 'Viciotek.exe'
$start.WorkingDirectory = $install
$start.UseShellExecute = $false
$start.EnvironmentVariables['VICIOTEK_DATA_DIR'] = $data
$process = New-Object System.Diagnostics.Process
$process.StartInfo = $start
$started = $false
try {
    if (-not $process.Start()) { throw 'No se pudo iniciar el ejecutable extraido.' }
    $started = $true
    $timeout = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 25
        $process.Refresh()
        if ($process.HasExited) { throw "La app termino prematuramente ($($process.ExitCode))." }
        if ([DateTime]::UtcNow -gt $timeout) { throw 'No aparecio la ventana.' }
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero)

    Start-Sleep -Milliseconds 1500
    $process.Refresh()
    if ($process.MainWindowTitle -ne 'VICIOTEK') { throw "Ventana inesperada: $($process.MainWindowTitle)" }
    $runtime = @($process.Modules | Where-Object { $_.ModuleName -ieq 'coreclr.dll' })
    if ($runtime.Count -ne 1) { throw 'No se encontro coreclr.dll en el proceso.' }
    $runtimePath = $runtime[0].FileName
    $bundledRuntime = Test-Path -LiteralPath (Join-Path $install 'coreclr.dll')
    if ($bundledRuntime -and -not $runtimePath.StartsWith($install, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'El portable no cargo el runtime incluido.'
    }

    $second = New-Object System.Diagnostics.Process
    $second.StartInfo = $start
    $secondStarted = $false
    try {
        $secondStarted = $second.Start()
        if (-not $secondStarted -or -not $second.WaitForExit(5000)) { throw 'Una segunda instancia quedo abierta sobre los mismos datos.' }
        if ($second.ExitCode -ne 0) { throw 'La segunda apertura fallo en lugar de activar la instancia existente.' }
        $process.Refresh()
        if ($process.HasExited) { throw 'La segunda apertura cerro la instancia original.' }
    }
    finally {
        if ($secondStarted -and -not $second.HasExited) { $second.Kill(); $second.WaitForExit() }
        $second.Dispose()
    }

    $null = $process.CloseMainWindow()
    if (-not $process.WaitForExit(8000)) { throw 'La app no se cerro dentro de 8 segundos.' }
    if ($process.ExitCode -ne 0) { throw "Cierre con error: $($process.ExitCode)" }
    if (Test-Path -LiteralPath (Join-Path $data 'viciotek.log')) { throw 'La app genero un log de excepciones.' }

    $library = Get-Content -LiteralPath (Join-Path $data 'library.json') -Raw | ConvertFrom-Json
    $settings = Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json
    if ($library.schemaVersion -ne 1 -or @($library.games).Count -ne 0) { throw 'Biblioteca inicial inesperada.' }
    if ($settings.windowWidth -lt 760 -or $settings.windowHeight -lt 540) { throw 'Geometria guardada invalida.' }

    $result = [ordered]@{
        MeasuredAt = [DateTimeOffset]::Now.ToString('o')
        Archive = $archive
        Sha256 = $actualHash
        Passed = $true
        Sandbox = $root
        BundledRuntime = $bundledRuntime
        LoadedRuntime = $runtimePath
        ExitCode = $process.ExitCode
        LibraryGameCount = @($library.games).Count
        Checks = @('ZIP extraction', 'SHA256', 'Main window', 'Runtime location', 'Single library instance', 'Clean exit', 'No exception log', 'Library/settings persistence')
    }
    $json = $result | ConvertTo-Json -Depth 5
    if ($OutputFile) { $json | Set-Content -LiteralPath $OutputFile -Encoding UTF8 }
    $json
}
finally {
    if ($started -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
