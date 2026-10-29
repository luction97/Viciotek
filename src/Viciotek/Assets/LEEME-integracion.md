# VICIOTEK — Identidad visual para Windows

Este kit utiliza exactamente el último logo circular aprobado, con **aro blanco fino**, corona abstracta, mando en V y detalles azules. No es un rediseño. Se eliminó el fondo negro **exterior al círculo** mediante un recorte alfa; el interior oscuro del emblema se conservó.

## Qué archivo usar

| Archivo | Uso |
|---|---|
| `viciotek.ico` | Ejecutable Windows, accesos directos, instalador y desinstalador |
| `viciotek.png` | Imagen principal RGBA transparente de 1024 × 1024 para UI/Acerca de |
| `viciotek-512.png`, `viciotek-256.png` | Gráficos intermedios y publicaciones |
| `viciotek-16.png` ... `viciotek-128.png` | Miniaturas de tamaño exacto cuando sean necesarias |
| `Branding/instalador-panel.png` / `.bmp` | Panel lateral para instalador tipo Inno Setup (480 × 919) |
| `instalador-cabecera.png` / `.bmp` | Gráfico cuadrado de encabezado para instalador |
| `vista-previa.png` | Comprobación en fondo oscuro y claro (no incluir en app) |

## Integración de C# WPF (proyecto Viciotek)

Copiá `viciotek.ico` y `viciotek.png` a `src/Viciotek/Assets/`.

En el archivo de proyecto `.csproj`, si corresponde a la ruta relativa a ese proyecto:

```xml
<PropertyGroup>
  <ApplicationIcon>Assets\viciotek.ico</ApplicationIcon>
</PropertyGroup>
```

Para la ventana WPF, configurar su `Icon` usando el recurso empacado, por ejemplo:

```xml
<Window Icon="/Assets/viciotek.ico" ...>
```

Verificá que el `.ico` se incluya como recurso WPF apropiado y que la ventana ya no sobrescriba el icono desde el código.

Luego recompilá desde cero, y actualizá el acceso directo o reinstalá si Windows muestra su versión cacheada.

## Instalador

Para **Inno Setup 6.5+**, las imágenes del asistente pueden ser PNG; para mayor compatibilidad se incluyen BMP. Si ya existe un instalador, adaptá las rutas existentes, no reemplaces el proyecto del instalador.

Ejemplo para el `[Setup]` del `.iss` (ajustar las rutas reales):

```ini
[Setup]
SetupIconFile=assets\viciotek.ico
WizardImageFile=assets\instalador-panel.png
WizardSmallImageFile=assets\instalador-cabecera.png
UninstallDisplayIcon={app}\Viciotek.exe
```

El último valor presupone que el ejecutable se llama `Viciotek.exe`; corregilo si tiene otro nombre. Para instaladores MSI, MSIX u otros, los recursos se importan de otra manera. El logo no equivale a generar o firmar digitalmente el instalador.

## Nota sobre seguridad y firma de software

Los iconos y logos **no son una firma digital**. Para que Windows identifique al editor verificado del ejecutable/instalador, se requiere firma de código (certificado) y un proceso de publicación apropiado.
