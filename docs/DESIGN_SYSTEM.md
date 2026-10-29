# Diseño visual

Fuente de verdad: `src/Viciotek/Presentation/Theme/Theme.xaml` y `MainWindow.xaml`.

- Fondo `#0A0B0E`; superficies `#121419`/`#171A20`; texto principal `#F3F4F6`, secundario `#A2A8B3`; acento cálido `#E7BE7B`.
- Segoe UI Variable Text/Display; iconos Segoe Fluent Icons con alternativa MDL2.
- Barra superior de 48 DIP: marca, búsqueda, botón `+` (abre la modal de agregar), engranaje de Preferencias y controles de ventana discretos.
- Portadas 200×300 DIP, radio 10 DIP, separación 16 DIP; margen horizontal mínimo 40 DIP y vertical 24 DIP. Columnas centradas según ancho.
- Imagen llena todo el marco con recorte central 2:3. No deformar ni introducir bandas para conservar una imagen panorámica completa.
- Solo el nombre aparece temporalmente al hover o foco de teclado, sobre un degradado inferior corto. Sin instrucciones de clic, leyendas permanentes ni animaciones de texto.
- Hover de portada: elevación suave (escala) y borde de acento; desactivable en Preferencias. Sin sombras costosas ni blur.
- Selección/drop con borde de acento; foco de teclado contrastado; menú contextual oscuro. Fallback de icono y monograma cuando no hay portada.
- Sin sombras costosas, blur, sidebar, contadores, noticias ni anuncios.
- Chrome WPF propio con esquinas DWM. Manifest PerMonitorV2; validación multimonitor/DPI todavía incompleta.
- Estado vacío: «Tu biblioteca» y una acción para agregar. El botón `+` abre una modal que explica cómo agregar juegos; las instrucciones no aparecen en la pantalla principal. Toasts para errores o información no evidente.
- Menú de tarjeta: personalización local directa; búsqueda automática solo visible con SteamGridDB configurado. Un lápiz de portada aparece solo en hover/foco para cambiar la imagen sin abrir el menú. Diálogos breves, conservando validaciones y aviso de almacenamiento sin cifrar.
