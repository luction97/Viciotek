# VICIOTEK

_Todos tus vicios, en un solo lugar._

Biblioteca personal de videojuegos para Windows 11: portadas, búsqueda y lanzamiento sin paneles adicionales. C# / WPF / .NET 8, sin servidor ni Electron.

## Instalar y ejecutar (Windows 11 x64)
- **Instalador**: `Viciotek-1.1.1-setup-win-x64.exe`, generado en `dist/`. Instala para el usuario actual en `%LOCALAPPDATA%\Programs\VICIOTEK`, incluye .NET y crea automáticamente un acceso directo **VICIOTEK en el escritorio** y otro en Inicio. No requiere administrador. Permite actualizar/desinstalar desde Windows sin borrar la biblioteca.
- **Portable x64**: extraer **todo** `Viciotek-1.1.1-win-x64.zip` y abrir `Viciotek.exe`. Incluye .NET; no requiere instalación ni administrador.
- **Framework-dependent x64**: requiere **.NET 8 Desktop Runtime x64** (el runtime básico de .NET no alcanza).
- Los datos están en `%LOCALAPPDATA%\Viciotek`; una segunda apertura reutiliza la instancia existente. Si venís de una versión anterior, mové el contenido de la carpeta de datos previa a `%LOCALAPPDATA%\Viciotek` antes de abrir 1.1.1: no hay migración automática.
- Al actualizar, cerrar la versión anterior antes de abrir la nueva: comparten la misma biblioteca y el bloqueo de instancia. No ejecutar como administrador para arrastrar desde un Explorador sin elevación.
- Instalador/ejecutable locales aún sin firma digital; Windows puede mostrar SmartScreen. Comparar el archivo `.sha256` entregado por un canal confiable. No se afirma certificación de producción ni cumplimiento completo de OWASP.
- Los binarios no se versionan en Git. Publicarlos en [GitHub Releases](https://github.com/luction97/Viciotek/releases) es una acción separada del mantenedor; la CI produce artefactos de prueba, no hace releases automáticas.

## Uso
- El botón **+** abre una ventana con las instrucciones para agregar juegos (arrastrar `.exe`, `.lnk` o `.url`, o elegir archivos). Doble clic o Enter sobre una tarjeta para jugar.
- Las portadas se elevan con un sutil resaltado al pasar el cursor; el efecto se puede desactivar en Preferencias.
- Los `.lnk` conservan destino ejecutable, argumentos y directorio. Los `.url` admiten protocolos conocidos de Steam, Epic, Ubisoft, EA, GOG, Battle.net, Rockstar y Xbox; el launcher correspondiente debe estar instalado.
- Arrastrar una imagen **sobre la tarjeta** para reemplazar su portada; también con el lápiz que aparece al pasar el cursor o desde el menú contextual. Se recorta al centro a 2:3.
- PNG, JPEG, BMP, GIF (primer frame), TIFF e ICO mediante codecs WPF; WebP depende de codecs del sistema. Máximo 30 MiB/30 megapíxeles.
- `Ctrl+F`: buscar · `Ctrl+O`: agregar · `Ctrl+,`: preferencias · `F2`: editar · Enter en búsqueda: jugar · Escape: limpiar búsqueda/selección.
- SteamGridDB es opcional: escribir la clave en Preferencias, usar «Verificar conexión» y **Guardar**. La verificación comprueba lo recién escrito, sin cambiar la clave guardada hasta confirmar. Activar «Buscar portadas automáticamente al agregar juegos». Solo consulta al importar (si está activado) o al pedir portadas; nunca al iniciar. La clave se muestra oculta y se guarda cifrada en este equipo (DPAPI); no viene incluida en el ejecutable. Nota comercial: las imágenes de SteamGridDB son aportes de usuarios; revisar sus condiciones antes de redistribuir VICIOTEK.
- La identificación prioriza manifiestos Steam y metadatos locales sobre el nombre del ejecutable. Si no encuentra una coincidencia fiable, abre la galería con «Ingresá el nombre del juego» para buscar y elegir manualmente. No expande siglas ambiguas ni reemplaza automáticamente portadas existentes. La acción contextual de búsqueda automática solo aparece cuando falta portada.

VICIOTEK se cierra cuando Windows acepta el lanzamiento. Eso no garantiza que el juego complete su inicio; ante un error detectable la biblioteca sigue abierta.

## Acerca de
La autoría se muestra de forma discreta en **Preferencias → Acerca de VICIOTEK**: creado por Lucas Siano, © 2026 Lucas Siano. Detalles de marca en [docs/BRANDING.md](docs/BRANDING.md) y atribución en [COPYRIGHT.md](COPYRIGHT.md).

## Datos y recuperación
Respaldar `%LOCALAPPDATA%\Viciotek` completa con la app cerrada. El guardado usa reemplazo atómico y `.bak`; un JSON dañado se conserva como `*.corrupt-*.json` antes de intentar recuperar el respaldo.

## Desarrollo reproducible

SDK fijado en `global.json` (.NET 8.0.427 o parche compatible). WPF/MVVM en `src/Viciotek`, reglas en Domain, importación en Application, IO en Infrastructure y UI en Presentation. No hay dependencias NuGet de ejecución; tests y herramientas son independientes del instalador final.

```powershell
dotnet restore Viciotek.sln --locked-mode
dotnet build Viciotek.sln -c Release --no-restore --nologo
dotnet test Viciotek.sln -c Release --no-build --nologo
.\tools\Test-Dependencies.ps1
.\tools\Publish.ps1
.\tools\Publish.ps1 -Variant FrameworkDependent
.\tools\SmokePublished.ps1 -ZipPath '.\dist\Viciotek-1.1.1-win-x64.zip'
```
Los archivos `packages.lock.json` se versionan. Cada publicación genera carpeta, ZIP y SHA256 bajo `dist/`; para repetir usar `-OutputRoot` nuevo, sin borrar distribuciones existentes. Para el instalador: preparar Inno Setup con `tools/Get-BuildTools.ps1` y ejecutar `tools/Build-Installer.ps1 -CompilerPath <ruta-a-ISCC.exe>`; ver los comandos de build/smoke en [CONTRIBUTING.md](CONTRIBUTING.md).

### Git, CI y seguridad
- Ramas breves desde `main` (`feat/*`, `fix/*`), Conventional Commits, PR revisado y sin force-push a ramas compartidas. Flujo y hooks opcionales: [CONTRIBUTING.md](CONTRIBUTING.md).
- La CI compila, prueba, escanea secretos con Gitleaks, audita dependencias y verifica el instalador. Actions fijadas por SHA y permisos mínimos; nunca usar una clave personal para la CI normal. Dependabot propone actualizaciones semanales.
- `.gitignore` excluye binarios, cachés, datos personales, secretos, certificados y diagnósticos. Antes de un commit revisar staging y ejecutar `tools/Test-Commit.ps1`; los controles de seguridad incluyen pruebas negativas.
- Amenazas, controles OWASP Top 10:2025 y limitaciones reales: [SECURITY.md](SECURITY.md). Las reglas de protección de `main` deben habilitarse en GitHub; añadir estos archivos no las activa automáticamente.

Prueba adicional del flujo WPF de arrastre, galería y persistencia:
```powershell
dotnet run --project tools/SteamGridSmoke/SteamGridSmoke.csproj -c Release
# Red real opcional: definir VICIOTEK_TEST_API_KEY solo en el entorno y agregar -- --live.
```
Sin `--live` usa HTTP simulado. Con red usa la misma implementación de la aplicación; no ejecuta juegos, no guarda la clave y crea datos aislados en `%LOCALAPPDATA%\Temp\opencode`. Los eventos WPF son automatizados: no equivale a un arrastre físico desde Explorer ni verifica diferencias de elevación/UIPI.

Para no tocar datos personales:
```powershell
$env:VICIOTEK_DATA_DIR = Join-Path $env:TEMP 'opencode\viciotek-sandbox'
dotnet run --project src/Viciotek
Remove-Item Env:VICIOTEK_DATA_DIR
```

## Estado y límites
Interfaz sin tutoriales ni instrucciones de clic, portadas personalizables y virtualización verificada con tests WPF. Apertura caliente medida hasta portadas listas en ~0,6 s; el objetivo de RAM ≤100 MB **no se cumple**. Pendientes: arranque frío, DPI/multimonitor, UAC y launchers comerciales. Revisar actualización de .NET LTS antes de su fin de soporte.

Continuidad: [estado](docs/PROJECT_STATE.md), [arquitectura](docs/ARCHITECTURE.md), [decisiones](docs/DECISIONS.md), [diseño](docs/DESIGN_SYSTEM.md), [rendimiento](docs/PERFORMANCE.md) y [pendientes](docs/ROADMAP.md).

## Derechos de autor
**© 2026 Lucas Siano. Todos los derechos reservados.** Código original bajo política propietaria; que el repositorio sea visible en GitHub no concede una licencia open source. Alcance y distribución: [COPYRIGHT.md](COPYRIGHT.md). Dependencias, marcas e imágenes de terceros conservan sus derechos: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
