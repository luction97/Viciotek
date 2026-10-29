# Viciotek — continuidad de desarrollo

## Antes de cambiar código
- Leer `docs/PROJECT_STATE.md`, `docs/ARCHITECTURE.md` y `docs/DECISIONS.md`.
- Verificar archivos y pruebas reales; no reiniciar ni reimplementar funcionalidades terminadas.
- Proponer un plan breve, cambios pequeños y pruebas de regresión relevantes.
- No tocar datos personales en `%LOCALAPPDATA%\Viciotek`; usar `VICIOTEK_DATA_DIR` con una carpeta de prueba aislada.
- No cerrar aplicaciones ajenas ni descartar cambios existentes. Existe repositorio Git publicado (GitHub `luction97/Viciotek`): ramas breves desde `main`, Conventional Commits; push solo ante pedido explícito del propietario. Antes de commit: `tools/Test-Commit.ps1`; cambios en el instalador requieren `tools/SmokeInstaller.ps1`.

## Estructura y restricciones
- C# / WPF / .NET 8, Windows 11. MVVM; composición en `src/Viciotek/App.xaml.cs`.
- `Domain`: reglas puras; `Application`: importación/validación; `Infrastructure`: Windows, JSON, imágenes y red opcional; `Presentation`: UI y view models.
- Sin Electron, servidor web ni dependencias NuGet de ejecución. No usar un `WrapPanel` normal: conservar virtualización real y carga diferida de imágenes.
- Respetar `docs/DESIGN_SYSTEM.md`: portadas 2:3, barra mínima, sin nombres permanentes bajo las tarjetas.
- No red al inicio, no sondeo periódico, cierre tras aceptación del lanzamiento por Windows. No equivale a verificar que el juego terminó de arrancar.
- Mantener compatibilidad con `library.json` esquema 1 y copias `.bak`.

## Validación y entrega
```powershell
dotnet build Viciotek.sln -c Release --nologo
dotnet test Viciotek.sln -c Release --nologo
```
- No compilar/publicar sobre un ejecutable en uso. Cerrar solo las instancias de prueba iniciadas por el agente.
- Tests WPF en hilo STA con Dispatcher cuando intervienen bindings/controles.
- Distinguir CONFIRMADO, INFERIDO y DESCONOCIDO. No presentar tiempo hasta HWND como tiempo hasta primera pantalla completa; no afirmar que se cumple el presupuesto de RAM.
- Actualizar `docs/PROJECT_STATE.md` antes de finalizar: cambios, pruebas, límites y próximo paso concreto.
- Responder en español, de forma directa y técnica.
