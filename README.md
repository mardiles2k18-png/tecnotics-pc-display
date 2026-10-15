# Turing Smart Screen: app propia

Solución .NET 10 para Windows. Arquitectura: ver el documento "Arquitectura de la app Turing Smart Screen".

| Proyecto | Qué hace |
| --- | --- |
| `src/Turing.Device` | Busca la pantalla (VID 1A86 / USB35INCHIPSV2), abre el puerto COM y envía órdenes del protocolo revisión A |
| `src/Turing.Probe` | Herramienta de prueba de la fase 1: enciende, brillo, dibuja, muestra imágenes y mide la velocidad |
| `src/Turing.Sensors` | Lee CPU, GPU y RAM con LibreHardwareMonitor (necesita administrador y el driver PawnIO para la temperatura de CPU) |
| `src/Turing.Render` | Dibuja el tema con SkiaSharp y devuelve solo las zonas que cambiaron. Incluye la fuente Inter (OFL) |
| `src/Turing.Themes` | Formato de tema en JSON (`theme.json`), cargador y temas incluidos (`Builtin/`, generados con `tools/generar_temas_basicos.py`) |
| `src/Turing.Engine` | Motor: mantiene la pantalla, lee sensores, dibuja el tema; ajustes (`turing.json`), registro (`turing.log`), arranque con Windows y diagnóstico |
| `src/Turing.App` | App de bandeja `TuringMonitor.exe` (WinForms): tema, brillo, giro, encender/apagar, arranque con Windows, herramientas |
| `src/Turing.Monitor` | Consola para desarrollo con el mismo motor: `--simular`, `--tema`, `--png`, `--diagnostico`, `--sensores` |
| `tools/simulador.py` | Decodifica lo que se enviaría a la pantalla y lo guarda como PNG (para probar sin el dispositivo) |

## Compilar

```
dotnet build
dotnet publish src/Turing.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
dotnet publish src/Turing.Probe -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

## Probar sin la pantalla

```
Turing.Probe prueba --puerto archivo:salida.bin
Turing.Monitor --simular salida.bin --segundos 5 --tema "Básico horizontal" --png frame.png   # sensores inventados
python3 tools/simulador.py salida.png salida.bin
```

Formato de los temas: ver `COMO-CREAR-TEMAS.md`.

`tools/analisis/` tiene el lector de los temas `.data` del software original (`nrbf.py`) y el exportador a JSON,
para la fase 4 (importar temas originales). El software original no se incluye en este repositorio.

## Estado

- Fase 1: comunicación con la pantalla (revisión A, COM por USB). Hecho.
- Fase 2: monitor de CPU, GPU y RAM, vertical y horizontal. Hecho.
- Fase 3: temas en JSON, app de bandeja y arranque con Windows. Hecho.
- Fase 4: importar los temas originales. Pendiente.
- Fase 5: editor visual de temas. Pendiente.
