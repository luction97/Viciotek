# Diseño visual

Fuente de verdad: `src/Viciotek/Presentation/Theme/Theme.xaml` y `MainWindow.xaml`.

- Fondo `#0A0B0E`; superficies `#121419`/`#171A20`; texto principal `#F3F4F6`, secundario `#A2A8B3`; acciones y enlaces en azul suave `#83B9F5` (`ActionAccentBrush`); recursos legados de portadas intactos.
- Segoe UI Variable Text/Display; iconos Segoe Fluent Icons con alternativa MDL2.
- Barra superior de 64 DIP: marca, búsqueda, botón `+` (abre la modal de agregar), engranaje de Preferencias y controles de ventana discretos.
- Portadas 2:3, preferidas 280×420 DIP y ancho efectivo acotado a 1.15 veces el preset elegido, radio 10 DIP, separación 16 DIP; margen horizontal mínimo 40 DIP y vertical 34 DIP. Columnas centradas según ancho.
- Imagen llena todo el marco con recorte central 2:3. No deformar ni introducir bandas para conservar una imagen panorámica completa.
- Solo el nombre aparece temporalmente al hover o foco de teclado, sobre un degradado inferior corto. Sin instrucciones de clic, leyendas permanentes ni animaciones de texto.
- Hover de portada: elevación vertical de 6 DIP en 180 ms (ease-out) y contorno neutro tenue, también al foco de teclado; desactivable en Preferencias. Sin escala, deformación, sombras costosas ni blur; no se modifica el bitmap ni su resolución.
- Un único contorno dentro de la tarjeta animada para hover, selección, foco y drop. Hover: blanco al 22 % (`CardHoverBrush`, #38FFFFFF) de 1 DIP, con la elevación existente. Selección: blanco suave opaco #E0E3E8 (`CardSelectionBrush`) de 1.5 DIP; foco: #F3F4F6 de 1.5 DIP con prioridad sobre ambos; drop: azul #149DFF (`CardAccentBrush`) de 2 DIP, reservado para identificar el destino de arrastre. Acento específico de portadas, sin cambiar `AccentBrush` global. Sin anillo adicional en el ListBoxItem exterior. Menú contextual oscuro. Fallback de icono y monograma cuando no hay portada.
- Sin sombras costosas, blur, sidebar, contadores, noticias ni anuncios.
- Chrome WPF propio con esquinas DWM. Manifest PerMonitorV2; validación multimonitor/DPI todavía incompleta.
- Estado vacío centrado: «Tu biblioteca empieza acá», descripción breve, «Agregar mi primer juego» y ayuda de arrastre. Solo visible sin juegos; las búsquedas sin coincidencias conservan su estado propio. El botón abre la modal existente.
- Menú de tarjeta: personalización local directa mediante clic derecho → «Cambiar portada…» o drop de imagen; sin botón flotante sobre la portada que intercepte la selección del juego. Búsqueda automática solo visible con SteamGridDB configurado. Diálogos breves, conservando validaciones y aviso de protección de la clave con DPAPI.

- Iconos de acción sin fondo ni borde visible: área de 32 DIP, hover de color, tooltip y foco blanco de teclado. Acciones de diálogo como texto azul suave y secundarias neutras, sin relleno ni borde; hover tenue en cápsula. Único CTA claro en la bienvenida, con radio 18 DIP. Todos los modales usan DialogSurfaceStyle: superficie neutra #191A1E, radio 20 DIP, sin borde exterior, sombras ni separador de pie. Menús contextuales sin borde exterior. El foco de teclado y los campos enfocados conservan indicación visible.

- Preferencias: interruptores visuales de 30×18 DIP sobre CheckBox nativo, con estados por posición y color; conservan semántica accesible y teclado. Campos editables oscuros sin contorno permanente; la ruta local es texto seleccionable sin caja. No se alteran recursos ni comportamientos de portadas.

- SteamGridDB: dos pasos breves dentro de Preferencias (sitio oficial → pegar código). Usar «código de conexión» y mencionar «API Key» solo para reconocer el texto del proveedor. Pegar solo lee el portapapeles tras el clic explícito. Conectar verifica y confirma preferencias en una acción; Comprobar conserva la edición. Estado guardado no implica verificación al arrancar. Mensajes de conexión junto al campo, con reintento y anuncio accesible.

- Revisión 1.1.8: búsqueda sin borde (incluido foco), texto alineado con placeholder mediante columnas explícitas, sin doble padding. Configuración prioriza SteamGridDB, descripción de 13 DIP y pasos breves; Más opciones cerrado al abrir. Biblioteca y Datos locales dentro del desplegable; Acerca de siempre visible. Altura natural acotada al área de trabajo.

- Propuesta aprobada, 1.1.9: marca espaciada con punto azul, buscador discreto con Ctrl F, agregar antes de configuración. Modal con superficie #191B20, ancho visible 550 DIP, título 19 DIP, proveedor con icono y pasos alineados, acento #92BAF0, Listo como acción clara y Acerca de visible. Responsive solo en biblioteca principal; galería de selección de portadas conserva su geometría.

- 1.1.10: botones de texto sin relleno/borde ni forma de hover en todos los estados de puntero; se ilumina el contenido y se conserva el foco de teclado. Configuración 660 DIP, título 22, texto/acciones 14 y código 15 DIP. Slider en Más opciones con 4 presets (200/240/280/320 DIP), vista previa reversible y guardado explícito. Responsive 2:3 y virtualización conservados.

- 1.1.11: sin animaciones ni esqueletos adicionales al filtrar. Portadas disponibles se presentan antes del layout; pulsaciones agrupadas 120 ms con acciones de teclado inmediatas. Bienvenida reducida a título, agregar, arrastre y acceso opcional a portadas; instrucciones detalladas solo dentro de Configuración.

- 1.1.12: Editar juego prioriza Nombre y oculta datos de inicio tras Opciones avanzadas. DisclosureStyle de texto sin contenedor decorativo. Una sola acción Cambiar portada abre dos fuentes explícitas; elección automática queda en Más opciones cuando es aplicable.

- 1.1.14: hover y selección neutros sin modificar geometría, animación, imágenes ni layout. Dirección futura del logo en BRAND_DIRECTION.md; recursos oficiales conservados.

- Galería de búsqueda: modal preferida de 1000×780 DIP, acotada al área de trabajo; sugerencias en dos filas de 28 DIP con scroll. Portadas de 120×180 sin cambios; 12 completas desde 1000×700 DIP, menos columnas/filas cuando el área disponible es menor. Footer separado del viewport virtualizado.
