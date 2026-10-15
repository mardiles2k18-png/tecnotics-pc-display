using Turing.Device;
using Turing.Engine;
using Turing.Sensors;
using Turing.Themes;

// Herramienta de consola para desarrollo: el mismo motor que la app de bandeja, con opciones de prueba.
//   --simular salida.bin   pantalla simulada (bytes a un archivo) y sensores inventados
//   --segundos N           termina tras N segundos
//   --png archivo.png      guarda el último fotograma al terminar
//   --tema "Nombre"        tema a usar
//   --diagnostico          prueba de las 8 formas de envío (vertical y horizontal)
//   --sensores             lista todos los sensores del PC

var argsList = args.ToList();
string? simulateFile = TakeOption(argsList, "--simular");
int? seconds = int.TryParse(TakeOption(argsList, "--segundos"), out var sec) ? sec : null;
string? pngFile = TakeOption(argsList, "--png");
string? themeName = TakeOption(argsList, "--tema");
bool diagnose = argsList.Remove("--diagnostico");
bool dumpSensors = argsList.Remove("--sensores");

Console.Title = "Turing Monitor (consola)";

if (dumpSensors)
{
    using var hw = new HardwareSensorSource();
    Console.WriteLine(hw.DumpAll());
    return 0;
}

TuringRevA? OpenDevice()
{
    if (simulateFile is not null)
        return new TuringRevA(new FileStream(simulateFile, FileMode.Create, FileAccess.ReadWrite));
    var p = SerialDisplay.FindTuringPort();
    return p is null ? null : SerialDisplay.Open(p);
}

if (diagnose)
{
    foreach (var o in new[] { Orientation.Portrait, Orientation.Landscape })
    {
        using var device = OpenDevice() ?? throw new InvalidOperationException("No encontré la pantalla.");
        Diagnostics.Run(device, o);
        Console.WriteLine("Saca una foto de la pantalla y pulsa Enter.");
        if (simulateFile is null)
            Console.ReadLine();
    }
    return 0;
}

var settings = simulateFile is null ? AppSettings.Load() : new AppSettings();
if (themeName is not null)
    settings.Tema = themeName;
var store = new ThemeStore(AppSettings.ThemesFolder);
store.InstallBuiltins();

using var service = new MonitorService(settings, store,
    () => simulateFile is null ? new HardwareSensorSource() : new FakeSensorSource(),
    OpenDevice);
service.StatusChanged += s => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {s}");
service.PreviewRequested = pngFile is not null;
service.Start();

using var done = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
done.Wait(seconds is int n ? TimeSpan.FromSeconds(n) : Timeout.InfiniteTimeSpan);

// La copia del fotograma se hace en el hilo del motor, así nunca sale a medio dibujar.
if (pngFile is not null && service.PreviewPng is { } png)
    File.WriteAllBytes(pngFile, png);
return 0;

static string? TakeOption(List<string> list, string name)
{
    int i = list.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (i < 0 || i + 1 >= list.Count)
        return null;
    var value = list[i + 1];
    list.RemoveRange(i, 2);
    return value;
}
