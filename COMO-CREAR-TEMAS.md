# Cómo crear un tema

Cada tema es una carpeta dentro de `temas` (junto a TuringMonitor.exe) con un archivo `theme.json`
y, si quieres, sus imágenes. Copia la carpeta de un tema existente, cámbiale el nombre y edita el
`theme.json` con el Bloc de notas. Después, en el icono de la bandeja: Herramientas > Recargar temas.

- `orientation`: `portrait` (vertical, 320x480) o `landscape` (horizontal, 480x320).
- `background`: `color`, `gradientTo` (degradado opcional) e `image` (imagen de fondo opcional).
- `elements`: lista de elementos con `type`, `x`, `y`, `w` (ancho) y `h` (alto) en píxeles.

| type | Para qué | Campos |
| --- | --- | --- |
| panel | Rectángulo fijo | color, borderColor, radius |
| label | Texto fijo | text, size, color, align (left/center/right), bold |
| image | Imagen fija | image |
| text | Texto con valores | text, size, color, align, bold, colorRules |
| bar | Barra de un valor | sensor, max, color, backColor |

En `text` los valores van entre llaves, con formato opcional: `{cpu.load:0}%`, `{ram.used:0.0} GB`,
`{time:HH:mm}`, `{date:dd/MM}`, `{model.gpu}`.

Valores disponibles: cpu.load, cpu.temp, cpu.power, cpu.clock, cpu.clock.ghz, gpu.load, gpu.temp,
gpu.power, gpu.mem.used, gpu.mem.total, gpu.mem.used.gb, gpu.mem.total.gb, ram.load, ram.used,
ram.available, ram.total. Nombres: name.cpu, name.gpu (completos) y model.cpu, model.gpu (cortos).

`colorRules` cambia el color según el valor, p. ej. temperatura verde, amarilla y roja:
`[{"max": 60, "color": "#76D66F"}, {"max": 80, "color": "#FFC14D"}, {"color": "#FF5C5C"}]`
