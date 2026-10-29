param([switch]$Staged)

$ErrorActionPreference = 'Stop'
$root = (& git rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0) { throw 'Se necesita un repositorio Git.' }
$files = if ($Staged) { @(& git diff --cached --name-only --diff-filter=ACMR) } else { @(& git ls-files) }
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron enumerar archivos Git.' }
$forbidden = @()
foreach ($path in $files) {
    if ($path -match '(^|/)(bin|obj|dist|artifacts|TestResults|measurements|\.vs|\.tools)/' -or
        $path -match '(^|/)(settings|library)(\..*)?\.json$|(^|/)\.env($|\.(?!example$|sample$))|\.(pfx|p12|key|pem|log|bak|dmp)$') {
        $forbidden += $path
    }
}
if ($forbidden.Count -gt 0) { throw ('Archivos sensibles o generados versionados: ' + ($forbidden -join ', ')) }
Write-Output "Politica de archivos Git: $($files.Count) rutas revisadas, sin datos personales ni artefactos prohibidos."
