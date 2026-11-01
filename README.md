# Notitas

Notas adhesivas para el escritorio de Windows, con tags, panel de búsqueda y archivos Markdown.
Es un solo `Notitas.exe`, sin instalación. Solo necesita .NET Framework 4.8, que ya viene con Windows 10 (1903 o posterior) y Windows 11.

## Uso

| Acción | Cómo |
| --- | --- |
| Nueva nota | `Ctrl+Alt+N` |
| Abrir el panel | `Ctrl+Alt+K` o clic en el icono de la bandeja |
| Traer las notas al frente | `Ctrl+Alt+F` |
| Despejar el escritorio o devolver las notas | `Ctrl+Alt+D` |
| Mover una nota | Arrastrar la franja superior; `Alt` desactiva el imán |
| Plegar una nota a una línea | Doble clic en la franja |
| Agregar un tag | Escribirlo abajo y pulsar `Enter`, espacio o coma |
| Guardar una nota en el panel | La X de la nota; no la borra |

El clic derecho en el icono de la bandeja da las demás opciones: ordenar en columnas, ordenar por tag, plegar todas, configuración y salir.

## Datos

Todo vive junto al exe, así que copiar la carpeta se lleva todo:

```
Notitas.exe
datos/
  config.json       configuración
  layout.json       posición, tamaño y estado de cada nota
  notas/*.md        una nota por archivo
  notas/.papelera/  notas borradas
```

## Obsidian

Cada nota es Markdown con frontmatter (`title`, `tags`, `color`, `created`, `updated`).
En **Configuración > Cambiar carpeta**, elige una carpeta dentro de tu bóveda, por ejemplo `MiBoveda/Postits`.
Los cambios viajan en ambos sentidos, y las propiedades que agregues en Obsidian se conservan.
Si una nota se edita en los dos lados a la vez, la versión externa queda como una nota aparte con "(conflicto)" en el título.

## Compilar y probar

Requiere el SDK de .NET.

```powershell
.\build.ps1                 # deja el exe en dist\
.\tests\roundtrip.ps1       # formato Markdown
.\tests\run-ui-tests.ps1    # UI dentro del proceso, sin teclado ni mouse globales
```
