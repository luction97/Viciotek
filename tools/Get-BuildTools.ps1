param(
    [ValidateSet('InnoSetup', 'Gitleaks')]
    [string[]]$Tools = @('InnoSetup', 'Gitleaks'),
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Temp\opencode\viciotek-build-tools')
)

$ErrorActionPreference = 'Stop'
foreach ($module in @('Microsoft.PowerShell.Security', 'Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive')) {
    Import-Module (Join-Path $PSHOME "Modules\$module\$module.psd1") -Force
}
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-tools.json') -Raw | ConvertFrom-Json
$Destination = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory($Destination) | Out-Null

function Get-Sha256([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

foreach ($tool in $Tools) {
    $spec = if ($tool -eq 'InnoSetup') { $config.innoSetup } else { $config.gitleaks }
    $folder = Join-Path $Destination ($tool.ToLowerInvariant() + '-' + $spec.version)
    $file = Join-Path $Destination ([Uri]$spec.url).Segments[-1]
    if (-not (Test-Path -LiteralPath $file) -or (Get-Sha256 $file) -ne $spec.sha256) {
        $temporary = $file + '.download'
        Invoke-WebRequest -UseBasicParsing -Uri $spec.url -OutFile $temporary
        if ((Get-Sha256 $temporary) -ne $spec.sha256) { throw "Checksum incorrecto para $tool. No se ejecutara." }
        Move-Item -LiteralPath $temporary -Destination $file -Force
    }
    if ($tool -eq 'InnoSetup') {
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike ('*O=' + $spec.publisher + '*')) {
            throw 'La firma Authenticode del compilador no es valida o su editor no coincide.'
        }
        if (-not (Test-Path -LiteralPath (Join-Path $folder 'ISCC.exe'))) {
            # Official portable mode: no machine installation, file association or desktop icons.
            $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/PORTABLE=1', "/DIR=`"$folder`"")
            $process = Start-Process -FilePath $file -ArgumentList $arguments -PassThru -Wait
            try { if ($process.ExitCode -ne 0) { throw "No se pudo preparar Inno Setup ($($process.ExitCode))." } }
            finally { $process.Dispose() }
        }
        $compiler = Join-Path $folder 'ISCC.exe'
        $signature = Get-AuthenticodeSignature -LiteralPath $compiler
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike ('*O=' + $spec.publisher + '*')) {
            throw 'El compilador extraido no tiene la firma esperada.'
        }
        [pscustomobject]@{ Tool = $tool; Version = $spec.version; Path = $compiler }
    } else {
        if (-not (Test-Path -LiteralPath (Join-Path $folder 'gitleaks.exe'))) {
            Expand-Archive -LiteralPath $file -DestinationPath $folder
        }
        [pscustomobject]@{ Tool = $tool; Version = $spec.version; Path = (Join-Path $folder 'gitleaks.exe') }
    }
}
