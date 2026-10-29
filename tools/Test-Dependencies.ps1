$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot '..\Viciotek.sln'
$raw = & dotnet list $solution package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar la auditoria de dependencias.' }
$report = ($raw -join "`n") | ConvertFrom-Json
if ($report.problems) { throw 'La auditoria no pudo completarse; revisar fuentes NuGet.' }
$issues = @()
foreach ($project in $report.projects) {
    foreach ($framework in $project.frameworks) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($package -and $package.vulnerabilities) { $issues += "$($package.id) $($package.resolvedVersion)" }
        }
    }
}
if ($issues.Count -gt 0) { throw ('Dependencias vulnerables: ' + (($issues | Select-Object -Unique) -join ', ')) }
Write-Output 'Auditoria NuGet: sin vulnerabilidades conocidas en dependencias directas/transitivas.'
