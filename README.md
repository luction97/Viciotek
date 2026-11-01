# VICIOTEK

_Todos tus juegos en un solo lugar._

Biblioteca personal de videojuegos para Windows 11. Organiza, busca y abre tus juegos desde una interfaz oscura y minimalista. Sin servidor ni telemetría.

## Instalación — versión 1.1.39

- **Instalador recomendado:** `dist/Viciotek-1.1.39-setup-win-x64.exe`. Incluye .NET y se instala para tu usuario, sin permisos de administrador. Para compartirlo basta con enviar este archivo.
- **Portable:** `dist/Viciotek-1.1.39-win-x64.zip`. Incluye .NET: extrae todo el ZIP y abre `Viciotek.exe`. Conserva todos los archivos de la carpeta; el ejecutable por sí solo no es suficiente.
- **Variante sin .NET incluido:** `dist/Viciotek-1.1.39-win-x64-framework-dependent.zip`. Requiere .NET 8 Desktop Runtime x64.

Requisito: Windows 11 x64. Cierra VICIOTEK antes de actualizar. La biblioteca y las preferencias se conservan en `%LOCALAPPDATA%\Viciotek`; instalar o desinstalar no las elimina. El portable utiliza esa misma carpeta de datos. Una segunda apertura reutiliza la instancia existente.

Cada descarga generada tiene un archivo `.sha256` de integridad. Los binarios todavía no tienen firma digital y Windows puede mostrar una advertencia de SmartScreen. Esta entrega está destinada a uso y pruebas privadas; no implica certificación de Microsoft Store.

## Primer uso

1. Conecta SteamGridDB para mostrar las portadas de tus juegos automáticamente, o elige **Ahora no**. La clave se valida y guarda protegida por Windows; puedes desconectar desde Configuración.
2. Elige **Detectar mis juegos** para revisar los resultados antes de agregarlos, o **Agregar manualmente**. También puedes arrastrar un ejecutable o acceso directo a la ventana.

Los archivos elegidos manualmente se agregan directamente. Selector y arrastre comparten identificación, descarga de portadas y control de duplicados. Las imágenes se guardan localmente; también puedes elegir portadas desde tu equipo.

La detección automática es conservadora: Steam, Epic y GOG requieren metadatos que identifiquen videojuegos instalados. Xbox/WindowsApps y la detección genérica por registro están deshabilitados mientras no puedan distinguir juegos de otras aplicaciones con suficiente confianza. Los resultados pueden omitir juegos; puedes agregarlos manualmente.

## Biblioteca y controles

- Doble clic o **Enter** sobre el juego seleccionado para jugar.
- Al abrir no hay una portada seleccionada. La primera flecha del teclado o dirección del mando selecciona el primer juego visible; las siguientes recorren la cuadrícula. El hover del mouse es independiente.
- **Ctrl + clic** selecciona varios; **Shift + clic**, un rango; **Ctrl + A** selecciona los juegos visibles cuando la biblioteca tiene el foco.
- **Supr** o **Quitar de la biblioteca** pide confirmación. Nunca borra instalaciones ni archivos personales. El menú del fondo permite deshacer el último lote importado, previa confirmación.
- **Ctrl + F** buscar · **Ctrl + O** agregar · **Ctrl + ,** Configuración · **F2** editar · **Escape** limpiar búsqueda/selección. **Enter** en la búsqueda abre el primer resultado visible.
- En Configuración puedes cambiar idioma (español, inglés o sistema), tamaño de portadas y efectos de movimiento.

El mando se atiende únicamente con la ventana activa y fuera de búsqueda, diálogos y menús. Se corrigió la lectura nativa XInput; la compatibilidad con mandos físicos y DirectInput sigue pendiente de validación completa.

## Desarrollo

C# / WPF / .NET 8, MVVM, sin dependencias NuGet de ejecución. SDK fijado en `global.json`.

```powershell
dotnet build Viciotek.sln -c Release --nologo -warnaserror
dotnet test Viciotek.sln -c Release --no-build --nologo
./tools/Publish.ps1 -Variant Portable
./tools/Publish.ps1 -Variant FrameworkDependent
./tools/Build-Installer.ps1 -CompilerPath <ruta-a-ISCC.exe>
```

Usa `VICIOTEK_DATA_DIR` para pruebas aisladas. Antes de un commit ejecuta `tools/Test-Commit.ps1`; valida los paquetes con `tools/SmokePublished.ps1` y el instalador con `tools/SmokeInstaller.ps1`. La memoria `AI_CONTEXT.local.md` es exclusivamente local, ignorada por Git y excluida de los paquetes.

## Documentación y licencia

[Estado y validaciones](docs/PROJECT_STATE.md) · [Arquitectura](docs/ARCHITECTURE.md) · [Decisiones](docs/DECISIONS.md) · [Diseño](docs/DESIGN_SYSTEM.md) · [Rendimiento](docs/PERFORMANCE.md) · [Auditoría de publicación](docs/PUBLICATION_AUDIT.md).

**© 2026 Lucas Siano. Todos los derechos reservados.** Código propietario; su publicación en GitHub no otorga una licencia de código abierto. [Derechos](COPYRIGHT.md) · [Seguridad](SECURITY.md) · [Contribución](CONTRIBUTING.md) · [Terceros](THIRD_PARTY_NOTICES.md).

Antes de publicar en Microsoft Store quedan pendientes firma/formato de distribución, actualización del runtime, materiales de publicación y pruebas con cuentas, mandos y monitores reales. Consulta la auditoría y las actualizaciones posteriores en el estado del proyecto.
