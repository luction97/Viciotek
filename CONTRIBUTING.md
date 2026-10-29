# Desarrollo y flujo Git

## Preparación (Windows 11 x64)

Instalar el SDK indicado en `global.json`. No agregar dependencias de ejecución sin justificar su necesidad; WPF/MVVM y la biblioteca JSON siguen siendo locales.

```powershell
dotnet restore Viciotek.sln --locked-mode
dotnet build Viciotek.sln -c Release --no-restore --nologo
dotnet test Viciotek.sln -c Release --no-build --nologo
.\tools\Test-Dependencies.ps1
```

Los archivos `packages.lock.json` se versionan. Para actualizar paquetes intencionalmente: cambiar versiones, restaurar con `--force-evaluate`, revisar el diff de los locks y repetir auditoría/tests. Dependabot propone actualizaciones; no aprobarlas sin revisar compatibilidad y procedencia.

## Ramas, commits y revisión

- `main`: base estable; ramas breves `feat/<tema>`, `fix/<tema>`, `chore/<tema>`. No añadir Git Flow/develop sin una necesidad de equipo.
- Pull requests pequeños con objetivo, evidencia de pruebas y riesgos. Preferir squash merge; no hacer push forzado sobre ramas compartidas ni reescribir historial publicado.
- Conventional Commits, asunto de hasta 72 caracteres: `feat(installer): add per-user Windows installer`, `fix(security): protect legacy credential backups`.
- Configurar un correo **noreply** de GitHub si no se desea publicar el correo personal en los commits. No cambiar identidades globales de otros desarrolladores.
- Tags `vX.Y.Z` solo para entregas verificadas. Un tag o SHA256 no equivale a firma Authenticode.
- En GitHub, el propietario debe habilitar rulesets para `main`: PR obligatorio, checks de CI, aprobación de otro revisor cuando haya colaboradores, resolución de conversaciones y bloqueo de force-push/borrado. `CODEOWNERS` por sí solo **no** aplica estas restricciones.

## Controles antes de un commit

```powershell
.\tools\Get-BuildTools.ps1 -Tools Gitleaks
git config --local core.hooksPath .githooks
git add -- <archivos-revisados>
.\tools\Test-Commit.ps1
```

Los hooks bloquean archivos de datos/artefactos y secretos detectados en staging; `commit-msg` valida el formato. Revisar los hooks antes de activarlos. La CI repite los controles sobre el historial completo porque los hooks locales pueden omitirse. No usar `git add .` sin revisar `git diff --cached`.

## Datos y pruebas de escritorio

- Nunca probar con `%LOCALAPPDATA%\Viciotek` personal. Usar `VICIOTEK_DATA_DIR` o los sandboxes de los scripts.
- No ejecutar juegos ni cerrar aplicaciones ajenas durante automatizaciones.
- Tests de bindings/controles en STA con Dispatcher; mantener virtualización y carga diferida.
- Leer `docs/PROJECT_STATE.md`, `docs/ARCHITECTURE.md` y `docs/DECISIONS.md` antes de cambiar flujos compartidos. Actualizar estado, compatibilidad, pruebas y límites al terminar.
- `dotnet run --project tools/SteamGridSmoke/SteamGridSmoke.csproj -c Release` no usa internet. `--live` es opt-in mediante una clave de entorno; nunca pegarla en comandos versionados, capturas o reportes.

## Compilar y probar el instalador

```powershell
$tools = @(.\tools\Get-BuildTools.ps1)
$iscc = ($tools | Where-Object Tool -eq InnoSetup).Path
.\tools\Build-Installer.ps1 -CompilerPath $iscc
.\tools\SmokeInstaller.ps1 -CompilerPath $iscc -PublishDirectory dist\Viciotek-1.1.1-win-x64
```

El bootstrap descarga herramientas fijadas por versión/SHA256; Inno Setup se verifica también con Authenticode y se extrae en modo portátil. No se instala globalmente. Los tests redirigen accesos directos y desactivan el registro de desinstalación mediante una compilación de smoke separada; no instalan en el escritorio real ni alteran la biblioteca personal. El instalador de distribución no contiene esos valores de prueba.

Las herramientas y NuGet requieren red al preparar/compilar, nunca al iniciar VICIOTEK. No publicar instaladores de smoke ni credenciales de firma. Las contribuciones deben respetar `COPYRIGHT.md`; este documento no concede una licencia de distribución sobre el código del titular.
