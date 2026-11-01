# Auditoría de publicación — VICIOTEK 1.1.36

Fecha: 2026-11-01. Base: `b1c9e2d`, rama `codex/welcome-connection-first`.

## Dictamen

No enviar todavía a Microsoft Store. La biblioteca tiene una base funcional validada, pero hay defectos reproducidos en gamepad y la preparación de publicación contiene bloqueos concretos. No se recomienda otro rediseño: conservar portadas, jerarquía y estilo actuales.

Esta revisión cubre composición/inicio/cierre, importación y detectores, lanzamiento, selección/reversión, persistencia/DPAPI, búsqueda/portadas, estilos/accesibilidad, gamepad, paquetes y materiales Store. Es una auditoría de código y pruebas orientada a riesgos, no una certificación de Microsoft ni una prueba exhaustiva de todos los equipos. No se modificó código, instaladores ni materiales Store; únicamente documentación y evidencia aislada.

## Validación realizada ahora

- `dotnet build Viciotek.sln -c Release --nologo -warnaserror`: PASS, cero advertencias/errores.
- `dotnet test Viciotek.sln -c Release --nologo --no-build`: 377/377 PASS; TRX aislado en `artifacts/publication-audit-1.1.36`.
- Test-Dependencies: sin vulnerabilidades NuGet conocidas en dependencias directas/transitivas. Esto no certifica el código propio ni prolonga el soporte del runtime.
- Test-RepositorySafety y Test-ReleasePayload: PASS. AI_CONTEXT.local.md permanece local e ignorado.
- Test-StoreAssets: PASS dimensional, pero las tres capturas fallan la inspección visual semántica descrita abajo. Un PASS de este script no demuestra que los assets estén listos.
- Authenticode consultado sobre EXE instalado/distribuido e instalador: ambos NotSigned.
- Se verificaron los checksums actuales de los dos ZIP y del instalador; su evidencia de SmokePublished/SmokeInstaller 1.1.36 ya existente sigue correspondiendo a esos binarios. No se reinstaló el producto personal durante esta auditoría.
- Prueba aislada de marshaling XInput y normalización DirectInput contra el ensamblado actual: defectos reproducidos sin hardware y sin lanzar juegos.

## Hallazgos prioritarios

### P1 — Gamepad: formato nativo incorrecto y acciones sin foco

**CONFIRMADO.** `Infrastructure/Input/XInputGamepad.cs:8` declara una estructura de 32 bytes con botones en offset 28 y eje izquierdo en 16. El contrato XINPUT_STATE del SDK es 16 bytes: botones en 4 y eje izquierdo en 8. Se escribió una respuesta nativa sintética con botón A=4096 y LX=20000; la aplicación leyó botones=0 y LX=0. Si XInput detecta un mando, la selección de backend tampoco deja pasar a WinMM aunque los datos estén mal interpretados.

**CONFIRMADO.** `MainWindow.xaml.cs:330` devuelve -32768 al normalizar 0, aunque la rama documenta también soporte de ejes -1000..1000 (cuyo centro es 0). No puede deducirse el rango del dispositivo únicamente del valor actual. `DirectInputJoystick.cs:121` filtra tipo 4, mientras el SDK DirectInput8 define joystick=0x14 y gamepad=0x15. Además, 0x5 en SetCooperativeLevel es exclusivo/foreground, contrario al comentario no exclusivo/background; el formato declara dwFlags=0 frente a DIDF_ABSAXIS=1.

**CONFIRMADO en código; efecto físico INFERIDO.** `MainWindow.xaml.cs:224` procesa entradas cada 50 ms y solo verifica IsEnabled. No verifica ventana activa/minimizada ni foco del buscador; el timer se detiene recién al cerrar. Un mando WinMM puede mover selección o solicitar lanzamiento mientras se utiliza otra ventana. Tampoco hay detección de flanco/repetición controlada: una pulsación sostenida se procesa a 20 Hz. Arriba/abajo usan el mismo desplazamiento de un elemento que izquierda/derecha (`:318`), sin respetar filas de la cuadrícula.

**Plan:** corregir ABI; configurar/consultar rangos reales; usar constantes del SDK; atender gamepad solo con ventana/biblioteca apropiadamente enfocada; disparar lanzamiento por nueva pulsación; navegación vertical por columnas actuales. Agregar pruebas de estructura/marshaling, foco, repetición y cuadrícula; probar mandos XInput y genéricos reales. Si no puede validarse, deshabilitar provisionalmente ese soporte antes de distribuirlo como terminado.

Evidencia: `artifacts/publication-audit-1.1.36/gamepad-probe.json`. Contrato: [XINPUT_STATE](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_state), [XINPUT_GAMEPAD](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad); SDK local `Windows Kits/10/Include/10.0.17763.0/um/Xinput.h` y `dinput.h`.

### P1 — Ruta EXE de Microsoft Store sin firma

**CONFIRMADO.** El instalador y Viciotek.exe son NotSigned. `store/CHECKLIST.md:3` presupone publicación EXE sin costos de firma y refiere a 1.1.34. No es correcto asumir que la Store firma ese instalador: Microsoft distingue MSIX/AppX, que vuelve a firmar, de EXE/MSI, que el editor debe firmar previamente; los PE también deben cumplir el requisito de firma confiable.

**Plan:** decidir ruta antes de enviar. O conservar Inno/EXE y firmar los binarios requeridos, o evaluar MSIX para Store con firma de Microsoft. Para MSIX validar lanzamiento de juegos externos, actualización, ubicación de datos y conservación/recuperación de biblioteca: no asumir que equivale al instalador actual. Si se mantiene EXE, comprobar URL HTTPS directa, versionada e inmutable y pruebas silenciosas sobre el paquete firmado definitivo. Estado de Partner Center, reserva y hosting actuales: DESCONOCIDO.

Fuentes oficiales: [Requisitos EXE/MSI](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements), [Firma según formato](https://learn.microsoft.com/en-us/windows/apps/publish/faq/get-started-with-the-microsoft-store).

### P1 — Las tres capturas Store no muestran la aplicación

**CONFIRMADO por inspección de los PNG reales.** `store/assets/store-library.png` muestra un navegador con un video; `store-empty.png` muestra GitHub; `store-hover.png` muestra una terminal con trabajo de desarrollo. No deben publicarse esos archivos: además de representar otro contenido, exponen información ajena al producto.

`tools/Capture-StoreScreenshots.ps1` toma píxeles de la pantalla completa sin comprobar que la ventana de VICIOTEK haya quedado delante; ignora el resultado de SetForegroundWindow. `Test-StoreAssets.ps1` comprueba dimensiones/peso, no contenido. También deduce el tamaño esperado del ancho observado, en lugar del nombre icon-300/512/1080, por lo que puede aceptar tamaños cruzados.

**Plan:** regenerar capturas de la versión final con biblioteca de demostración sin secretos, capturando únicamente la ventana correcta; fallar si no se puede garantizar el objetivo. Inspección humana obligatoria del contenido de cada PNG. Actualizar ficha/captions a los flujos reales y es/en; corregir validaciones dimensionales por nombre. Conservar la estética de la aplicación.

### Alta prioridad técnica — .NET 8 queda sin soporte en nueve días

**CONFIRMADO.** Proyecto `net8.0-windows`, SDK 8.0.427. A la fecha de revisión, Microsoft fija fin de soporte de .NET 8 el **10-11-2026**. No es un rechazo automático de Store, pero iniciar ahora una distribución pública nueva sobre un runtime a punto de quedar sin actualizaciones es una mala base de mantenimiento. El portable y el instalador incluyen su propio runtime: actualizar el .NET del equipo del usuario no actualiza esos archivos automáticamente.

**Plan:** preparar migración a .NET 10 LTS antes de lanzar; actualizar SDK/proyectos auxiliares/locks y repetir tests WPF, DPI, interop, smokes y publicación. Sin cambios de arquitectura ni dependencias innecesarias.

Fuente: [Política oficial de soporte .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

### P2 — Detección sin feedback y con cancelación incompleta

**CONFIRMADO en código.** `MainViewModel.cs:557` inicia el escaneo en Task.Run, pero IsImporting solo se activa cuando termina y comienza la importación. El usuario no ve un estado explícito Buscando y el drop sigue habilitado durante el escaneo. Los detectores no reciben CancellationToken; cancelar al cerrar no detiene un escaneo ya iniciado. Se drena antes de cerrar, de modo que una lectura lenta puede prolongar el cierre. El scanner absorbe excepciones y no distingue plataforma inaccesible de cero resultados.

**Plan:** estado de búsqueda discreto, exclusión de operaciones incompatibles y cancelación cooperativa entre lecturas/plataformas. Conservar revisión obligatoria y comportamiento conservador. Informar cobertura real Steam/Epic/GOG sin prometer detección universal; Xbox/registro genérico siguen deshabilitados deliberadamente.

### P2 — Localización y ficha/privacidad pendientes de cerrar

**CONFIRMADO.** Los avisos detectados de cero resultados/importación en `MainViewModel.cs:630,659`, y algunas ramas de error en `GameLauncher.cs:86,97`, están escritos directamente en español. La paridad de claves resx no prueba que no existan textos hardcodeados.

La ficha y checklist Store siguen en 1.1.34. El listing atribuye “Lucas Siano (Pyrsys B.V.)”, mientras COPYRIGHT/metadatos de producto dicen Lucas Siano: verificar la identidad correcta, sin inventarla. Publicita autocompletado genéricamente aunque corresponde a búsqueda de portadas, y usa tecnicismos como virtualización/DPAPI. “Todo el manejo de datos es local” no explica que se envían consultas al proveedor.

PRIVACY.md declara solo contacto con la API, pero también hay descarga de imágenes HTTPS/CDN. Traducción inglesa se declara pendiente. En la UI no se encontró enlace a privacidad. El archivo local no demuestra existencia de URL pública ni configuración en Partner Center.

**Plan:** completar mensajes es/en y ficha final honesta: no hay cuenta propia de VICIOTEK, SteamGridDB es externo/opcional. Describir consultas y descargas, completar política y URL pública, añadir acceso discreto desde Acerca de y revisar identidad/soporte. Requisitos aplicables de datos deben contrastarse con [políticas Store 10.5](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies); no se evaluó asesoramiento legal ni derechos de imágenes promocionales.

## UX que conservar y ajustes pequeños

- Mantener jerarquía de bienvenida en dos etapas, conexión dedicada, explicación corta, Ahora no persistente y acceso por arrastre.
- Mantener revisión previa, eliminación confirmada y deshacer por lote. No introducir Eliminar todo ni retirar seguridad para ahorrar un clic.
- En revisión de importación, “Selecciona los juegos que quieres agregar” sería más claro que “Confirma…” cuando todas las entradas manuales están sin marcar. Un conteo en la acción puede comunicar selección sin texto adicional; no es bloqueo de publicación.
- Conservar búsqueda estable, tamaños de portadas, hover/contorno blanco, virtualización, opciones plegadas e idiomas. No añadir colores, onboarding adicional ni decoraciones.
- Verificación humana pendiente en teclado completo, Narrador, alto contraste real y monitores 125/150/200 % DPI. Hay pruebas de recursos/layout; no equivalen a haber recorrido físicamente toda la matriz.

## Rendimiento medido ahora

Portable 1.1.36, mismo equipo, ventana 1280×800, tres muestras calientes tras descartar calentamiento. 500 entradas con archivos de portada sintéticos distintos por revisión, imágenes planas; sin claves ni red. Primera pantalla vacía y primer frame con las portadas visibles decodificadas; no tiempo hasta HWND ni prueba de presentación física/GPU.

| Conjunto | Frame con portadas listas | Memoria privada inicial | Privada tras recorrido |
|---|---:|---:|---:|
| Vacío | 793 ms | 71,3 MiB | 70,5 MiB |
| 500 registros | 868 ms | 105,3 MiB | 188,2 MiB |

En 500 registros: 8 portadas visibles, máximo 20 contenedores realizados; cero fallos de carga. Tres recorridos de 83 pasos, mediana de p95 por muestra 36,8 ms. La virtualización está funcionando en este escenario; no hay evidencia aquí de un bloqueo de rendimiento. No se certifica RAM ≤100 MB, ausencia de fugas, arranque frío ni rendimiento idéntico con portadas reales/otros equipos.

Datos: `artifacts/publication-audit-1.1.36/metrics-empty.json`, `metrics-500.json` y TRX. Los resultados de 1.0.2 en PERFORMANCE.md son históricos, no la medición actual.

## Orden de salida recomendado

1. Corregir gamepad y agregar regresiones del contrato nativo/foco; validar hardware real o deshabilitarlo provisionalmente.
2. Migrar al runtime LTS vigente y repetir la validación completa.
3. Cerrar formato Store/firma y generar el paquete definitivo; volver a ejecutar smokes sobre esa entrega.
4. Rehacer las tres capturas y cerrar idiomas, ficha, identidad y privacidad coherentes con el producto.
5. Prueba privada con SteamGridDB real, detectar/ejecutar juegos instalados Steam/Epic/GOG, conexión inválida/revocada/offline, selector/arrastre, revisión/cancelar, duplicados, quitar/deshacer y reinicio. Verificar DPI/teclado con usuarios de distintos equipos.
6. Enviar a certificación con instrucciones de prueba y requisitos externos explícitos. No prometer aprobación sin respuesta de Microsoft.

No se conectó ninguna cuenta real, no se accedió a datos personales de VICIOTEK y no se modificaron juegos, código de aplicación, paquetes ni materiales de publicación. No hubo commit/push de correcciones.

## Actualización posterior — 1.1.39
El informe anterior conserva los hallazgos de la versión auditada. `ed51001` corrige el contrato XInput a 16 bytes y restringe el mando a la ventana activa/foco apropiado; también añade navegación por columnas y lanzamiento por flanco. Esos puntos específicos ya están corregidos y cubiertos por pruebas automatizadas. DirectInput y validación de mandos físicos siguen pendientes. Ver el estado actualizado en PROJECT_STATE.md; no interpretar este informe histórico como una nueva medición de la versión 1.1.39.
