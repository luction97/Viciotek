# Rendimiento

## Interfaz lista — 2026-10-28 (1.0.2)
`StartupProbe` + `tools/MeasureUiReady.ps1` miden desde la creación del proceso hasta el fotograma de WPF con las portadas visibles decodificadas (`VisibleCoversReadyMs`), y no la presentación por GPU. Portátil x64; un calentamiento descartado. Seed con revisiones distintas por juego.

| Biblioteca | Primer frame | Portadas listas | Privada inicial | Privada tras scroll |
|---|---:|---:|---:|---:|
| Vacía | 517 ms | 541 ms | 105 MiB | 104 MiB |
| 30 juegos | 565 ms | 597 ms | 145 MiB | 160 MiB |
| 500 juegos | 590 ms | 621 ms | 155 MiB | 217 MiB |

- 500 juegos: 10 portadas visibles decodificadas sin fallos; 30 contenedores realizados (viewport + 1 fila de margen). 2 pasadas completas de scroll, 92 pasos: p95 **72,6 ms** por paso (picos ~100 ms, incluye decodificar/maquetar/esperar fotograma). Working set inicial 189 MiB → 240 MiB tras scroll.
- **Interpretación honesta:** la apertura "con portadas listas" ronda 0,62 s caliente. El coste de memoria crece al recorrer la biblioteca y el objetivo ≤100 MB sigue incumplido; no se forzó GC ni se vació el working set.

Datos crudos: `docs/measurements/2026-10-28-ui-ready-*.json`. Reproducir:
```powershell
.\tools\SeedUiData.ps1 -DataDir "$env:TEMP\opencode\viciotek-ui"
.\tools\SeedPerformanceData.ps1 -Count 500 -SourceDir "$env:TEMP\opencode\viciotek-ui" -TargetDir "$env:TEMP\opencode\viciotek-500"
.\tools\MeasureUiReady.ps1 -ExePath '.\dist\Viciotek-1.0.2-win-x64\Viciotek.exe' -DataDir "$env:TEMP\opencode\viciotek-500" -ScrollPasses 2
```
`SeedPerformanceData` ahora genera una revisión de portada distinta por juego (cache keys realistas) y se niega a sobrescribir una biblioteca de destino existente.

## Comparación de auditoría — 2026-10-28
Mismo equipo, método y bibliotecas; portable 1.0.0 frente a 1.0.1. Cinco muestras tras un calentamiento por caso, sin compilaciones simultáneas. Resultados crudos en `measurements/2026-10-28-before-*.json` y `after-*.json`.

| Biblioteca | Hasta HWND 1.0.0 → 1.0.1 | Privada MiB | Working set MiB |
|---|---:|---:|---:|
| Vacía | 566,1 → 588,7 ms | 106,4 → 105,7 | 146,5 → 146,3 |
| 500 juegos | 656,6 → 627,5 ms | 150,6 → 152,5 | 183,9 → 185,2 |

**Conclusión:** se mantiene apertura caliente hasta HWND en aproximadamente 0,6 s. La diferencia es pequeña y las muestras de memoria tienen variación: no se demuestra una reducción significativa de RAM ni una aceleración general para todos los casos. No confundir esta medición con arranque frío o pantalla completamente renderizada. El objetivo ≤100 MB sigue incumplido.

Mejoras estructurales verificadas por tests/código: 20 contenedores iniciales en lugar de 25 a 1280×800; máximo dos decodificaciones simultáneas; cancelación de decodificaciones en cola al descargar tarjetas; bindings agrupados; creación diferida del worker de iconos; claves de búsqueda y brushes de fallback bajo demanda. En render WPF instrumentado se verificaron 20 controles de portada y 20 bitmaps cargados con 500 entradas, sin modificar datos personales. No constituye una medición del primer frame.

En este harness PowerShell 5.1 con rutas de módulos de PowerShell 7 hubo que cargar explícitamente el módulo nativo `Microsoft.PowerShell.Utility` para usar `Get-FileHash`. No se modificó la configuración global del equipo.

## Medición confirmada — 2026-10-27
Windows 11 Home build 26300, Ryzen 7 7840HS (16 hilos), 16 GB RAM; NVIDIA RTX 4060/Radeon 780M. Publicación Release **portable x64**, runtime incluido 8.0.33, SDK 8.0.427.

`tools/MeasureStartup.ps1`: un calentamiento descartado + 5 ejecuciones secuenciales por biblioteca, 10 ms de intervalo de observación del HWND. Memoria tras 2,5 s de espera + 1 s de muestra CPU. Ventanas no maximizadas; datos con 100/300/500 entradas, 85/250/420 portadas. No se vaciaron cachés del SO ni se reinició Windows.

| Juegos | Mediana hasta HWND | Privada | Working set |
|---:|---:|---:|---:|
| 0 | 607 ms | 106,7 MiB | 147,6 MiB |
| 100 | 687 ms | 180,4 MiB | 212,5 MiB |
| 300 | 652 ms | 182,4 MiB | 215,0 MiB |
| 500 | 688 ms | 182,5 MiB | 215,0 MiB |

Datos crudos: `docs/measurements/2026-10-27-portable-*.json`. CPU temprana mediana: 31,2–46,9 ms de procesador por segundo; **no** es un estudio de reposo prolongado ni porcentaje global de CPU.

## Interpretación y presupuestos
- **CONFIRMADO**: tiempo caliente hasta HWND <1 s en estas muestras. No equivale al tiempo hasta que todas las portadas están visibles.
- **CONFIRMADO por 1.0.2**: "portadas listas" ≈ 0,62 s caliente con 500 juegos; ver la tabla de interfaz de arriba.
- **CONFIRMADO**: objetivo de RAM ≤100 MB **incumplido**, tanto privada como working set. La virtualización evita crecimiento proporcional a todas las portadas, pero no elimina el coste base de WPF.
- **CONFIRMADO por tests WPF**: con 500 elementos se mantiene un número acotado de contenedores durante inicio, scroll, resize y reset de colección; no se crean 500 controles simultáneos.
- **DESCONOCIDO**: frío ≤2 s, primer frame completo, p95 de frames/scroll, fugas tras uso prolongado y matriz DPI.
- **Red**: inspección de los puntos de llamada y test con handler contador confirman que inicializar el view model con clave/configuración activa no solicita portadas. No hubo captura de paquetes del sistema.

## Reproducir
```powershell
.\tools\SeedUiData.ps1 -DataDir "$env:TEMP\opencode\viciotek-ui"
.\tools\SeedPerformanceData.ps1 -Count 500 -SourceDir "$env:TEMP\opencode\viciotek-ui" -TargetDir "$env:TEMP\opencode\viciotek-500"
.\tools\MeasureStartup.ps1 -ExePath '.\dist\Viciotek-1.0.1-win-x64\Viciotek.exe' -DataDir "$env:TEMP\opencode\viciotek-500" -OutputFile '.\sample.json'
```
El primer script descarga ilustraciones desde Steam y crea ejecutables de demostración; no usarlos para validar que un juego real arranca. La app no descarga esas imágenes al inicio. Usar solo carpetas desechables: los scripts de seed sobrescriben sus JSON.
