param(
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -Force
$root = Join-Path $env:LOCALAPPDATA ('Temp\opencode\viciotek-installer-smoke-' + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $root 'app'
$data = Join-Path $root 'data'
[IO.Directory]::CreateDirectory($root) | Out-Null
[IO.Directory]::CreateDirectory($data) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $root 'desktop')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $root 'start-menu')) | Out-Null
$appProcess = $null

function Invoke-TestInstaller([string]$Executable, [string[]]$Arguments, [bool]$ExpectSuccess = $true) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -PassThru
    try {
        if (-not $process.WaitForExit(30000)) {
            $process.Kill() # This script started this installer process, never an unrelated app.
            $process.WaitForExit()
            throw 'La instalacion/desinstalacion de prueba excedio 30 segundos.'
        }
        if ($ExpectSuccess -and $process.ExitCode -ne 0) { throw "Instalador de prueba: salida $($process.ExitCode)." }
        if (-not $ExpectSuccess -and $process.ExitCode -eq 0) { throw 'El instalador no bloqueo la aplicacion en uso.' }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}

function Assert-True([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }

try {
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') -CompilerPath $CompilerPath -PublishDirectory $PublishDirectory -OutputRoot (Join-Path $root 'artifacts') -SmokeRoot $root | Out-Host
    $installer = (Get-ChildItem -LiteralPath (Join-Path $root 'artifacts') -Filter '*-smoke.exe' -File).FullName
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=`"$install`"", "/LOG=`"$(Join-Path $root 'install.log')`"")
    $installExit = Invoke-TestInstaller $installer $arguments
    $exe = Join-Path $install 'Viciotek.exe'
    $desktopLink = Join-Path $root 'desktop\VICIOTEK.lnk'
    $menuLink = Join-Path $root 'start-menu\VICIOTEK.lnk'
    Assert-True (Test-Path -LiteralPath $exe) 'No se instalo el ejecutable.'
    Assert-True (Test-Path -LiteralPath $desktopLink) 'No se creo el acceso directo de escritorio.'
    Assert-True (Test-Path -LiteralPath $menuLink) 'No se creo el acceso directo del menu Inicio.'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($desktopLink)
    try {
        Assert-True ($link.TargetPath -eq $exe) 'El acceso directo no apunta al ejecutable instalado.'
        Assert-True ($link.WorkingDirectory -eq $install) 'El directorio de trabajo del acceso es incorrecto.'
    }
    finally {
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) | Out-Null
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    }

    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe
    $start.WorkingDirectory = $install
    $start.UseShellExecute = $false
    $start.EnvironmentVariables['VICIOTEK_DATA_DIR'] = $data
    $appProcess = New-Object Diagnostics.Process
    $appProcess.StartInfo = $start
    Assert-True ($appProcess.Start()) 'No se pudo iniciar la app instalada.'
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 25
        $appProcess.Refresh()
        Assert-True (-not $appProcess.HasExited) 'La aplicacion instalada termino prematuramente.'
        Assert-True ([DateTime]::UtcNow -lt $deadline) 'No aparecio la ventana instalada.'
    } while ($appProcess.MainWindowHandle -eq [IntPtr]::Zero)

    $blockedExit = Invoke-TestInstaller $installer $arguments $false
    $appProcess.Refresh()
    Assert-True (-not $appProcess.HasExited) 'Setup cerro la aplicacion sin autorizacion.'
    $null = $appProcess.CloseMainWindow()
    Assert-True ($appProcess.WaitForExit(10000)) 'La app instalada no cerro correctamente.'
    Assert-True ($appProcess.ExitCode -eq 0) 'La app instalada termino con error.'

    # These are isolated test files, not the user's real library or desktop.
    $sentinel = Join-Path $data 'preserve-me.txt'
    [IO.File]::WriteAllText($sentinel, 'Keep all personal data during upgrade/uninstall.')
    $unknown = Join-Path $install 'user-added-file.txt'
    [IO.File]::WriteAllText($unknown, 'An unknown file must not be recursively deleted.')
    $protectedFiles = @('library.json','settings.json','preserve-me.txt')
    $before = @{}
    foreach ($name in $protectedFiles) { $before[$name] = (Get-FileHash -LiteralPath (Join-Path $data $name) -Algorithm SHA256).Hash }
    $upgradeExit = Invoke-TestInstaller $installer $arguments
    foreach ($name in $protectedFiles) {
        Assert-True ((Get-FileHash -LiteralPath (Join-Path $data $name) -Algorithm SHA256).Hash -eq $before[$name]) "La actualizacion altero $name."
    }
    $uninstaller = (Get-ChildItem -LiteralPath $install -Filter 'unins*.exe' -File).FullName
    $uninstallExit = Invoke-TestInstaller $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$(Join-Path $root 'uninstall.log')`"")
    Assert-True (-not (Test-Path -LiteralPath $exe)) 'La desinstalacion dejo el ejecutable.'
    Assert-True (-not (Test-Path -LiteralPath $desktopLink)) 'La desinstalacion dejo el acceso de escritorio.'
    Assert-True (-not (Test-Path -LiteralPath $menuLink)) 'La desinstalacion dejo el acceso del menu Inicio.'
    Assert-True (Test-Path -LiteralPath $unknown) 'La desinstalacion borro un archivo no administrado.'
    foreach ($name in $protectedFiles) {
        Assert-True ((Get-FileHash -LiteralPath (Join-Path $data $name) -Algorithm SHA256).Hash -eq $before[$name]) "La desinstalacion altero $name."
    }
    $result = [ordered]@{ Passed = $true; Sandbox = $root; DesktopShortcut = $true; InstallExitCode = $installExit;
        UpgradeExitCode = $upgradeExit; UninstallExitCode = $uninstallExit; BlockedWhileRunning = ($blockedExit -ne 0);
        PersonalDataPreserved = $true; UnknownFilePreserved = $true; RealDesktopModified = $false }
    $json = $result | ConvertTo-Json -Depth 4
    if ($OutputFile) { $json | Set-Content -LiteralPath $OutputFile -Encoding UTF8 }
    $json
}
finally {
    if ($appProcess) {
        if (-not $appProcess.HasExited) { $appProcess.Kill(); $appProcess.WaitForExit() }
        $appProcess.Dispose()
    }
}
