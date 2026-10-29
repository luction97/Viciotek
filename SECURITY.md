# Seguridad de VICIOTEK

## Alcance y reporte

Aplicación Windows de usuario, no sandbox para ejecutables ni servicio web. El usuario debe confiar en los juegos/accesos que incorpora. No ejecutar VICIOTEK como administrador para usar drag-and-drop con Explorer sin elevación.

Reportar vulnerabilidades de forma **privada** desde Security → Report a vulnerability del repositorio si el propietario lo habilitó; de lo contrario contactar al mantenedor por su perfil de GitHub antes de publicar detalles. No adjuntar API keys, `settings.json`, bibliotecas personales ni logs sin revisar. La existencia de este archivo no implica que el reporte privado o las protecciones de ramas estén habilitados en GitHub.

## Controles y límites (OWASP Top 10:2025)

La guía [OWASP Top 10:2025](https://owasp.org/Top10/2025/) se adapta al contexto de escritorio; no representa certificación ni sustituye un pentest.

| Riesgo | Control concreto / alcance |
|---|---|
| A01 Acceso indebido | Instalación/desinstalación por usuario, sin UAC; datos fuera del directorio instalado. No se eliminan archivos ajenos ni se cierran procesos por fuerza. |
| A02 Configuración insegura | Sin red al arrancar; datos, `.env`, logs y certificados privados excluidos de Git/paquetes; CI con `contents: read`, sin credenciales persistidas en checkout. |
| A03 Cadena de suministro | SDK y paquetes fijados/locks; auditoría NuGet directa/transitiva; actions fijadas por SHA, herramientas por versión/SHA256; compilador con Authenticode; actualizaciones mediante PR. |
| A04 Criptografía | Clave propia del usuario protegida con Windows DPAPI, oculta en UI; migración inmediata del JSON legado y protección de su `.bak`, verificando descifrado antes de reemplazar. DPAPI no impide extraer una clave de la memoria de un proceso propio. |
| A05 Inyección | Protocolos/rutas de lanzamiento validados; argumentos no se concatenan a una shell para ejecutar juegos; scripts de build rechazan comillas/saltos de línea en defines del compilador. No hay SQL, HTML ni servidor web. |
| A06 Diseño inseguro | Portadas/configuración con guardado atómico, archivos preparados antes del commit y rollback; mutex de presencia bloquea Setup mientras la aplicación usa sus binarios. |
| A07 Autenticación | SteamGridDB recibe Bearer únicamente en la API; no se incluye una clave de servicio en el programa, artefactos o CI. La verificación usa lo escrito sin alterar la clave activa hasta Guardar. |
| A08 Integridad | Checksums de descargas/artefactos, revisión de PR y rechazo de datos personales en el payload. SHA256 sin canal autenticado no prueba autoría; la firma propia del instalador sigue pendiente. |
| A09 Registro seguro | Errores del proveedor se transforman en mensajes sin headers/clave; Gitleaks usa redacción completa. No publicar logs o mediciones crudas con rutas/nombres locales. |
| A10 Excepciones | Timeout/cancelación y límites HTTP; fallo de portada no impide usar la biblioteca; errores de cifrado/escritura se informan sin mostrar la clave; tests de error/rollback. |

## Prácticas obligatorias

- No incluir claves personales (ni siquiera cifradas) en Git, instaladores o fixtures. Usar secretos sintéticos en tests y una variable de entorno de proceso solo para pruebas reales opt-in.
- Preparar Gitleaks con `tools/Get-BuildTools.ps1`; ejecutar `tools/Test-Commit.ps1`, `tools/Test-RepositorySafety.ps1`, `tools/Test-SecurityControls.ps1` y `tools/Test-Dependencies.ps1`. `.gitignore` no sustituye un scanner ni elimina secretos ya publicados.
- Si una clave se compartió o filtró, **revocarla/regenerarla** en el proveedor. No basta borrarla en un commit posterior. Eliminar historial publicado requiere coordinación explícita; no hacer force-push automáticamente.
- Revisar respaldos manuales o archivos `settings.corrupt-*.json` anteriores: pueden contener texto plano legado y se conservan para no destruir recuperación. No compartirlos; la migración automática cubre `settings.json` y su `.bak`, no copias externas o el historial del chat.
- `VICIOTEK_DATA_DIR` cambia el destino de datos y solo debe configurarse con rutas controladas. Una biblioteca compartida con versiones antiguas no tiene garantías de concurrencia entre esas versiones.
- No hay control de acceso de servidor, sesiones web, cookies, CSRF o SQL en este stack; no se añaden frameworks innecesarios para aparentar cobertura.

## Antes de distribución pública/comercial

- .NET 8 finaliza soporte el **10 de noviembre de 2026**: planificar y validar migración a un LTS soportado. Actualizar también el runtime autocontenido, no solo los paquetes de tests.
- Firmar y sellar temporalmente el instalador con un certificado del titular; claves privadas solo en almacenamiento/servicio de firma seguro. Esta entrega local no está firmada y puede mostrar SmartScreen.
- Revisar licencias de componentes, términos de SteamGridDB y derechos sobre cada imagen/marca. No distribuir portadas descargadas ni atribuirse material ajeno. Ver `COPYRIGHT.md` y `THIRD_PARTY_NOTICES.md`.
- Mantener revisiones de dependencias y reglas de GitHub activas. Esta configuración se valida localmente; GitHub Actions solo se ejecutará después de publicar el código y habilitar los servicios correspondientes.
