param([Parameter(Mandatory = $true)][string]$GitleaksPath)

$ErrorActionPreference = 'Stop'
$root = (& git rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0) { throw 'Se necesita Git para verificar los ignores.' }
foreach ($path in @('.env','data/settings.json','data/library.json','signing/release.pfx','dist/Viciotek.exe',
    'src/Viciotek/bin/Release/Viciotek.dll','tests/Viciotek.Tests/TestResults/run.trx','docs/measurements/local.json')) {
    & git check-ignore --quiet -- $path
    if ($LASTEXITCODE -ne 0) { throw "El ignore no protege $path." }
}
foreach ($path in @('src/Viciotek/Infrastructure/Covers/SteamGridDbClient.cs','src/Viciotek/Assets/viciotek.ico',
    'tests/Viciotek.Tests/packages.lock.json','.env.example')) {
    & git check-ignore --quiet --no-index -- $path
    if ($LASTEXITCODE -eq 0) { throw "Se esta ignorando codigo o un lock necesario: $path." }
}
$fixture = Join-Path $env:LOCALAPPDATA ('Temp\opencode\viciotek-security-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
# Synthesized fake credential, never a real user's key. The output is always redacted.
$synthetic = [Guid]::NewGuid().ToString('N')
$source = 'var client = new SteamGridDbClient("' + $synthetic + '");'
[IO.File]::WriteAllText((Join-Path $fixture 'bad.cs'), $source)
$reportPath = Join-Path $fixture 'report.json'
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& $GitleaksPath dir $fixture --config (Join-Path $root '.gitleaks.toml') --enable-rule steamgriddb-literal-key --redact --no-banner --no-color --report-path $reportPath --report-format json *> $null
$gitleaksExit = $LASTEXITCODE
$ErrorActionPreference = $previousPreference
if ($gitleaksExit -ne 1) { throw 'La prueba negativa no detecto una credencial literal sintetica.' }
$report = [IO.File]::ReadAllText($reportPath)
if ($report.Contains($synthetic)) { throw 'El reporte de gitleaks contiene la credencial sintetica sin redactar.' }
if (-not $report.Contains('steamgriddb-literal-key')) { throw 'El hallazgo no identifica la regla esperada.' }
$payload = Join-Path $fixture 'payload'
[IO.Directory]::CreateDirectory($payload) | Out-Null
[IO.File]::WriteAllText((Join-Path $payload 'settings.json'), '{}')
$blocked = $false
try { & (Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1') -Directory $payload }
catch { $blocked = $_.Exception.Message -like '*settings.json*' }
if (-not $blocked) { throw 'El empaquetado no rechazo settings.json.' }
Write-Output 'Seguridad: ignores, locks/assets permitidos, deteccion redactada de secreto sintetico y rechazo de datos personales aprobados.'
