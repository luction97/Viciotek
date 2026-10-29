param([string]$GitleaksPath = $env:GITLEAKS_EXE)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-RepositorySafety.ps1') -Staged
if (-not $GitleaksPath) {
    $config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-tools.json') -Raw | ConvertFrom-Json
    $GitleaksPath = Join-Path $env:LOCALAPPDATA "Temp\opencode\viciotek-build-tools\gitleaks-$($config.gitleaks.version)\gitleaks.exe"
}
if (-not (Test-Path -LiteralPath $GitleaksPath)) {
    throw 'Falta Gitleaks. Ejecuta tools/Get-BuildTools.ps1 -Tools Gitleaks o indica GITLEAKS_EXE.'
}
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& $GitleaksPath git --pre-commit --staged --redact --ignore-gitleaks-allow --no-banner --no-color
$gitleaksExit = $LASTEXITCODE
$ErrorActionPreference = $previousPreference
if ($gitleaksExit -ne 0) { throw 'Commit bloqueado: el escaneo de secretos fallo.' }
