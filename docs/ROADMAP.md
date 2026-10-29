# Pendientes priorizados

## Antes de considerar una versión de producción
1. **Instancia única**: implementada en 1.0.1 con pruebas de exclusión, directorios distintos y terminación anómala. Pendiente completar validación de activación/restauración bajo restricciones de foco y múltiples monitores.
2. **Rendimiento**: medido "covers listas" y pasos de scroll con `StartupProbe` (ver `docs/PERFORMANCE.md`). Queda perfilar la memoria que crece a ~217 MiB privados tras recorrer 500 juegos; el objetivo ≤100 MB sigue incumplido.
3. **Validación Windows real**: DPI 100/125/150/200 %, dos monitores, maximizar/restaurar/Snap, navegación solo con teclado, drop mixto y múltiples imágenes.
4. **Ciclo de vida asíncrono**: revisar cierre durante importación/descarga/escritura y eliminación durante actualización de portada; probar que no queden tareas accediendo a servicios liberados.
5. **Launchers**: probar accesos reales Steam/Epic/Ubisoft/EA/GOG/Battle.net/Rockstar/Xbox; cancelación UAC, destino ausente y errores Shell. Confirmar integración SteamGridDB con clave real sin guardarla en tests.
6. **Soporte .NET**: planificar actualización al siguiente LTS antes de finalizar soporte de .NET 8; conservar WPF/MVVM y validar nuevamente publicaciones.
7. **Distribución pública**: firma digital, política de licencia y empaquetado/actualización; comprobar portable en una máquina Windows sin .NET instalado.

## Mejoras pequeñas pendientes de confirmación/pruebas
- Monograma y filtro tras renombrar: **corregidos y cubiertos por regresión en 1.0.1**.
- Duplicados: identidad actual considera ruta+argumentos, no directorio de trabajo. Evaluar casos legítimos antes de cambiar una regla compartida.
- Reciclaje explícito de contenedores solo si el perfil lo justifica; actualmente existe virtualización mediante creación/eliminación acotada.
- Recuperación de miniaturas faltantes desde el original local: **implementada en 1.0.2** con regeneración diferida y pruebas.

No agregar sidebar, estadísticas, telemetría, noticias ni servicios residentes.
