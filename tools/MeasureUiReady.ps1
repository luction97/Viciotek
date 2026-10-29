param(
    [Parameter(Mandatory = $true)] [string]$ExePath,
    [Parameter(Mandatory = $true)] [string]$DataDir,
    [ValidateRange(1, 20)] [int]$Runs = 5,
    [ValidateRange(0, 20)] [int]$ScrollPasses = 0,
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$data = (Resolve-Path -LiteralPath $DataDir).Path
if ($data.TrimEnd('\') -ieq (Join-Path $env:LOCALAPPDATA 'Viciotek')) {
    throw 'Usa una biblioteca aislada. Esta prueba desplaza y cierra la ventana.'
}
$outputRoot = Join-Path $env:LOCALAPPDATA ('Temp\opencode\viciotek-ui-metrics-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$samples = @()
for ($run = 0; $run -le $Runs; $run++) {
    $report = Join-Path $outputRoot "$run.json"
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $exe
    $start.WorkingDirectory = Split-Path -Parent $exe
    $start.UseShellExecute = $false
    $start.EnvironmentVariables['VICIOTEK_DATA_DIR'] = $data
    $start.EnvironmentVariables['VICIOTEK_METRICS_FILE'] = $report
    $start.EnvironmentVariables['VICIOTEK_METRICS_SCROLL_PASSES'] = [string]$ScrollPasses
    $start.EnvironmentVariables['VICIOTEK_METRICS_EXIT'] = '1'
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    $started = $false
    try {
        $started = $process.Start()
        if (-not $started -or -not $process.WaitForExit(180000)) { throw 'Timeout midiendo la interfaz.' }
        if ($process.ExitCode -ne 0) { throw "La app termino con codigo $($process.ExitCode)." }
        if (-not (Test-Path -LiteralPath $report)) { throw 'No se genero el informe de UI.' }
        $sample = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($sample.Error) { throw $sample.Error }
        if ($sample.FailedVisibleCoverLoads -ne 0) { throw 'Hay portadas que no cargaron: no se acepta una medicion incompleta.' }
        if ($run -gt 0) { $samples += $sample }
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}

function Median($values) {
    $ordered = @($values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if ($ordered.Count % 2) { return $ordered[$middle] }
    return ($ordered[$middle - 1] + $ordered[$middle]) / 2
}

$result = [ordered]@{
    MeasuredAt = [DateTimeOffset]::Now.ToString('o')
    ExePath = $exe
    ExeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    AssemblySha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $exe) 'Viciotek.dll') -Algorithm SHA256).Hash
    DataDir = $data
    Method = 'Warm; one discarded warmup. Process creation to WPF frame-ready with decoded visible covers, not hardware presentation.'
    Samples = $samples
    Median = [ordered]@{
        FirstContentRenderedMs = Median $samples.FirstContentRenderedMs
        VisibleCoversReadyMs = Median $samples.VisibleCoversReadyMs
        InitialPrivateMiB = Median $samples.InitialPrivateMiB
        InitialWorkingSetMiB = Median $samples.InitialWorkingSetMiB
        AfterScrollPrivateMiB = Median $samples.AfterScrollPrivateMiB
        AfterScrollWorkingSetMiB = Median $samples.AfterScrollWorkingSetMiB
        ScrollStepReadyP95Ms = Median $samples.ScrollStepReadyP95Ms
    }
}
$json = $result | ConvertTo-Json -Depth 9
if ($OutputFile) { $json | Set-Content -LiteralPath $OutputFile -Encoding UTF8 }
$json
