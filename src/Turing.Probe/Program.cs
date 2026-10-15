using System.Diagnostics;
using SkiaSharp;
using Turing.Device;

// Herramienta de prueba de la fase 1: comprobar que hablamos con la pantalla y medir la velocidad de envío.
// Sin argumentos ejecuta la prueba completa y espera Enter al final (para abrirla con doble clic).

var log = new StringWriter();
void Say(string line = "")
{
    Console.WriteLine(line);
    log.WriteLine(line);
}

var argsList = args.ToList();
string? port = TakeOption(argsList, "--puerto");
string command = argsList.Count > 0 ? argsList[0].ToLowerInvariant() : "prueba";
bool interactive = args.Length == 0;

int exitCode = 0;
try
{
    switch (command)
    {
        case "listar":
            ListPorts();
            break;
        case "prueba":
            RunTest();
            break;
        case "imagen" when argsList.Count > 1:
            using (var d = OpenDisplay())
                ShowImage(d, argsList[1]);
            break;
        case "brillo" when argsList.Count > 1 && int.TryParse(argsList[1], out int pct):
            using (var d = OpenDisplay())
                d.SetBrightness(pct);
            Say($"Brillo al {pct}%.");
            break;
        case "apagar":
            using (var d = OpenDisplay())
                d.ScreenOff();
            break;
        case "encender":
            using (var d = OpenDisplay())
                d.ScreenOn();
            break;
        default:
            Say("""
                Uso:
                  Turing.Probe                     prueba completa (igual que 'prueba')
                  Turing.Probe listar              lista los puertos COM
                  Turing.Probe prueba              enciende, cambia brillo, dibuja y mide la velocidad
                  Turing.Probe imagen <archivo>    muestra una imagen PNG o JPG
                  Turing.Probe brillo <0-100>
                  Turing.Probe apagar | encender
                Opción: --puerto COM5 para no detectarlo automáticamente
                (o --puerto archivo:salida.bin para simular la pantalla).
                """);
            exitCode = 2;
            break;
    }
}
catch (UnauthorizedAccessException)
{
    Say($"ERROR: el puerto {port} está ocupado. Cierra el programa original (UsbMonitor.exe) y vuelve a probar.");
    exitCode = 1;
}
catch (Exception ex)
{
    Say($"ERROR: {ex.Message}");
    exitCode = 1;
}

if (command == "prueba")
{
    var path = Path.Combine(AppContext.BaseDirectory, "resultado-prueba.txt");
    try
    {
        File.WriteAllText(path, log.ToString());
        Console.WriteLine($"\nResultado guardado en {path}");
    }
    catch (Exception)
    {
        // Carpeta sin permisos de escritura: el resultado ya salió por consola.
    }
}

if (interactive)
{
    Console.WriteLine("\nPresiona Enter para salir.");
    Console.ReadLine();
}
return exitCode;

static string? TakeOption(List<string> list, string name)
{
    int i = list.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (i < 0 || i + 1 >= list.Count)
        return null;
    var value = list[i + 1];
    list.RemoveRange(i, 2);
    return value;
}

void ListPorts()
{
    var ports = SerialDisplay.ListPorts();
    if (ports.Count == 0)
        Say("No hay puertos COM. ¿Está conectada la pantalla y instalado el driver (35inchENG\\Driver)?");
    foreach (var p in ports)
        Say($"{p.Port,-7} {(p.LooksLikeTuring ? "<- pantalla Turing" : ""),-20} {p.Description} {p.DeviceId}");
}

TuringRevA OpenDisplay()
{
    port ??= SerialDisplay.FindTuringPort();
    if (port is null)
    {
        ListPorts();
        throw new InvalidOperationException("No encontré la pantalla. Indica el puerto con --puerto COMx.");
    }
    if (port.StartsWith("archivo:", StringComparison.OrdinalIgnoreCase))
    {
        // Pantalla simulada: guarda los bytes que se enviarían, para revisarlos sin el dispositivo.
        var file = port["archivo:".Length..];
        Say($"Simulando la pantalla en {file}...");
        return new TuringRevA(new FileStream(file, FileMode.Create, FileAccess.ReadWrite));
    }
    Say($"Abriendo {port} a {SerialDisplay.BaudRate} baudios...");
    return SerialDisplay.Open(port);
}

void RunTest()
{
    Say($"Prueba de la pantalla Turing 3.5\", {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    Say();
    Say("Puertos encontrados:");
    ListPorts();
    Say();

    using var d = OpenDisplay();

    var hello = d.Hello(TimeSpan.FromSeconds(1));
    Say(hello.Length == 0
        ? "HELLO: sin respuesta (normal en la 3.5\" revisión A)."
        : $"HELLO: respuesta {Convert.ToHexString(hello)}");

    d.ScreenOn();
    d.SetOrientation(Orientation.Portrait);
    d.SetBrightness(60);
    Say("Pantalla encendida, vertical, brillo 60%.");
    Say();

    // 1. Pantalla completa negra.
    var black = Rgb565.Fill(d.Width, d.Height, 0, 0, 0);
    var full1 = Time(() => d.DrawRgb565(0, 0, d.Width, d.Height, black));

    // 2. Barras de colores a pantalla completa.
    var bars = ColorBars(d.Width, d.Height);
    var full2 = Time(() => d.DrawRgb565(0, 0, d.Width, d.Height, bars));
    int fullBytes = d.Width * d.Height * 2;
    Say($"Pantalla completa ({fullBytes / 1024} KB): {full1:F0} ms y {full2:F0} ms -> {KBps(fullBytes, full2)} KB/s");

    // 3. Muchas actualizaciones pequeñas, como un número que cambia (100x40 px).
    const int smallW = 100, smallH = 40, rounds = 30;
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < rounds; i++)
    {
        var block = Rgb565.Fill(smallW, smallH, (byte)(i * 8), (byte)(255 - i * 8), 128);
        d.DrawRgb565(110, 220, smallW, smallH, block);
    }
    sw.Stop();
    int smallBytes = smallW * smallH * 2;
    double perSmall = sw.Elapsed.TotalMilliseconds / rounds;
    Say($"Zona de {smallW}x{smallH} ({smallBytes / 1024} KB): {perSmall:F1} ms de media en {rounds} envíos -> {KBps(smallBytes, perSmall)} KB/s");

    // 4. Brillo de 10 a 100.
    foreach (var pct in new[] { 10, 40, 70, 100, 60 })
    {
        d.SetBrightness(pct);
        Thread.Sleep(400);
    }
    Say("Brillo probado de 10% a 100% (debería verse el cambio).");

    // 5. Esquinas: comprueba que las coordenadas llegan bien.
    d.DrawRgb565(0, 0, d.Width, d.Height, black);
    var red = Rgb565.Fill(40, 40, 255, 0, 0);
    d.DrawRgb565(0, 0, 40, 40, red);
    d.DrawRgb565(d.Width - 40, 0, 40, 40, Rgb565.Fill(40, 40, 0, 255, 0));
    d.DrawRgb565(0, d.Height - 40, 40, 40, Rgb565.Fill(40, 40, 0, 0, 255));
    d.DrawRgb565(d.Width - 40, d.Height - 40, 40, 40, Rgb565.Fill(40, 40, 255, 255, 255));
    Say("Dibujados 4 cuadrados en las esquinas: rojo arriba-izquierda, verde arriba-derecha, azul abajo-izquierda, blanco abajo-derecha.");

    Say();
    double fps = 1000 / perSmall;
    Say($"Conclusión: una pantalla completa tarda {full2 / 1000:F1} s; una zona pequeña permite unas {fps:F0} actualizaciones por segundo.");
}

static double Time(Action action)
{
    var sw = Stopwatch.StartNew();
    action();
    return sw.Elapsed.TotalMilliseconds;
}

static string KBps(int bytes, double ms) => ms <= 0 ? "?" : $"{bytes / 1024.0 / (ms / 1000):F0}";

static byte[] ColorBars(int w, int h)
{
    (byte r, byte g, byte b)[] colors =
    [
        (255, 255, 255), (255, 255, 0), (0, 255, 255), (0, 255, 0),
        (255, 0, 255), (255, 0, 0), (0, 0, 255), (0, 0, 0),
    ];
    var buffer = new byte[w * h * 2];
    for (int y = 0; y < h; y++)
    {
        var (r, g, b) = colors[y * colors.Length / h];
        ushort c = Rgb565.FromRgb(r, g, b);
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 2;
            buffer[i] = (byte)c;
            buffer[i + 1] = (byte)(c >> 8);
        }
    }
    return buffer;
}

void ShowImage(TuringRevA d, string path)
{
    using var source = SKBitmap.Decode(path) ?? throw new InvalidOperationException($"No pude leer la imagen {path}.");
    d.ScreenOn();
    d.SetOrientation(source.Width > source.Height ? Orientation.Landscape : Orientation.Portrait);

    var info = new SKImageInfo(d.Width, d.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var scaled = source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
        ?? throw new InvalidOperationException("No pude escalar la imagen.");
    var pixels = new byte[d.Width * d.Height * 2];
    Rgb565.FromBgra32(scaled.GetPixelSpan(), pixels);

    var ms = Time(() => d.DrawRgb565(0, 0, d.Width, d.Height, pixels));
    Say($"Imagen {Path.GetFileName(path)} enviada en {ms:F0} ms ({d.Width}x{d.Height}).");
}
