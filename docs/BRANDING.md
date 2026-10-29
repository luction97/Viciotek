# Marca e identidad

## Nombre oficial

**VICIOTEK**

Eslogan: _Todos tus vicios, en un solo lugar._

## Convenciones de escritura

- **VICIOTEK**: identidad visual (logotipo, título de ventana, encabezados).
- **Viciotek**: capitalización normal en frases y nombres de archivos.
- **viciotek**: identificadores técnicos nuevos, si se crean (minúsculas).
- El espacio de nombres, el proyecto y el ensamblado usan `Viciotek`, igual
  que la marca; no queda ningún identificador con el nombre anterior.

## Autoría

- Creador: **Lucas Siano**.
- Año de creación: 2026.
- Aviso: © 2026 Lucas Siano. Todos los derechos reservados.

La autoría se muestra de forma discreta, únicamente en la pantalla **Acerca de**
(accesible desde Preferencias → «Acerca de VICIOTEK»). No hay firmas ni marcas
de agua en la pantalla principal ni en las portadas.

## Ubicación de los metadatos

- `src/Viciotek/Viciotek.csproj`: `Product` (VICIOTEK), `Company`/`Authors`
  (Lucas Siano), `Copyright`, `Description`, `AssemblyName` (Viciotek).
- `src/Viciotek/app.manifest`: identidad del ensamblado (`Viciotek.app`).
- `COPYRIGHT.md`: atribución y alcance de los derechos.
- `src/Viciotek/Presentation/Dialogs/AboutDialog.xaml`: pantalla Acerca de.

## Identificadores vigentes

Todos los identificadores usan la marca desde 1.1.0:

- Carpeta de datos: `%LOCALAPPDATA%\Viciotek`.
- Variable de entorno de pruebas: `VICIOTEK_DATA_DIR`.
- Archivo de registro: `viciotek.log`.
- Espacio de nombres, solución y proyecto: `Viciotek` / `Viciotek.sln` /
  `Viciotek.csproj`.

La carpeta de datos cambió respecto de versiones anteriores. La migración es
manual: mover el contenido de la carpeta previa a `%LOCALAPPDATA%\Viciotek` antes
de abrir 1.1.0.

## Ícono

El ícono oficial es `Assets/icono/viciotek.png` (1254×1254, con transparencia). Se genera
`Assets/viciotek.ico` a partir de él con resoluciones 16/24/32/48/64/128/256 (entradas
PNG, calidad alta). El ejecutable (`ApplicationIcon`), la ventana principal y el
instalador (`SetupIconFile`) usan `Assets/viciotek.ico`; el PNG original se conserva
sin modificaciones.

## SteamGridDB y distribución comercial

- La integración es opcional y solo se activa con una clave configurada por el usuario.
- Las imágenes obtenidas son aportes de usuarios; sus condiciones de uso y redistribución
  deben revisarse antes de distribuir VICIOTEK comercialmente.
- No se eliminan metadatos de autoría ni se eluden límites de la API.
- La clave se protege en reposo con DPAPI y no se incluye en el ejecutable.

## Indicaciones para futuras versiones

- Mantener `VICIOTEK` como nombre visible y `Lucas Siano` como autor.
- Conservar la autoría solo en Acerca de y en metadatos.
- Actualizar la versión en `Viciotek.csproj` y en `AboutDialog` si se añade.
- No introducir firmas ni telemetría para acreditar autoría.
