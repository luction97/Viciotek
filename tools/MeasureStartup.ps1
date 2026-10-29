param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,
    [Parameter(Mandatory = $true)]
    [string]$DataDir,
    [ValidateRange(1, 20)]
    [int]$Runs = 5,
    [ValidateRange(1000, 30000)]
    [int]$SettleMilliseconds = 2500,
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$data = (Resolve-Path -LiteralPath $DataDir).Path
if ($data.TrimEnd('\') -ieq (Join-Path $env:LOCALAPPDATA 'Viciotek')) {
    throw 'Usa una biblioteca aislada; esta prueba abre y guarda los datos al cerrar.'
}

$samples = @()
for ($run = 0; $run -le $Runs; $run++) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $exe
    $start.WorkingDirectory = Split-Path -Parent $exe
    $start.UseShellExecute = $false
    $start.EnvironmentVariables['VICIOTEK_DATA_DIR'] = $data
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    $started = $false
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw 'No se pudo iniciar Viciotek.' }
        $started = $true
        do {
            Start-Sleep -Milliseconds 10
            $process.Refresh()
            if ($process.HasExited) { throw "Viciotek termino antes de crear ventana ($($process.ExitCode))." }
            if ($timer.Elapsed.TotalSeconds -gt 15) { throw 'Timeout esperando HWND.' }
        } while ($process.MainWindowHandle -eq [IntPtr]::Zero)
        $timer.Stop()

        Start-Sleep -Milliseconds $SettleMilliseconds
        $process.Refresh()
        $cpuBefore = $process.TotalProcessorTime.TotalMilliseconds
        Start-Sleep -Milliseconds 1000
        $process.Refresh()
        $cpuAfter = $process.TotalProcessorTime.TotalMilliseconds
        if ($run -gt 0) {
            $samples += [pscustomobject]@{
                Run = $run
                HwndMs = [Math]::Round($timer.Elapsed.TotalMilliseconds, 1)
                WorkingSetMiB = [Math]::Round($process.WorkingSet64 / 1MB, 1)
                PrivateMiB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 1)
                IdleCpuMsPerSecond = [Math]::Round($cpuAfter - $cpuBefore, 1)
                Handles = $process.HandleCount
            }
        }
    }
    finally {
        # Only close the process started by this sample, never other apps/instances.
        if ($started -and -not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
        }
        $process.Dispose()
    }
}

function Get-Median($values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

$result = [ordered]@{
    MeasuredAt = [DateTimeOffset]::Now.ToString('o')
    ExePath = $exe
    ExeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    DataDir = $data
    Method = 'Warm; one discarded warmup; time until HWND, not complete rendering; memory after settle+1s'
    SettleMilliseconds = $SettleMilliseconds
    Samples = $samples
    Median = [ordered]@{
        HwndMs = Get-Median $samples.HwndMs
        WorkingSetMiB = Get-Median $samples.WorkingSetMiB
        PrivateMiB = Get-Median $samples.PrivateMiB
        IdleCpuMsPerSecond = Get-Median $samples.IdleCpuMsPerSecond
    }
}
$json = $result | ConvertTo-Json -Depth 8
if ($OutputFile) { $json | Set-Content -LiteralPath $OutputFile -Encoding UTF8 }
$json
