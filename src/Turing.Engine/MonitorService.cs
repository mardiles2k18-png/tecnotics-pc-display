using System.Collections.Concurrent;
using System.Diagnostics;
using Turing.Device;
using Turing.Render;
using Turing.Sensors;
using Turing.Themes;

namespace Turing.Engine;

/// <summary>
/// El motor del monitor: en un hilo propio mantiene la pantalla conectada, lee los sensores cada segundo,
/// dibuja el tema activo y envía las zonas que cambian. Los cambios de ajustes se aplican en caliente.
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ThemeStore _themes;
    private readonly Func<ISensorSource> _sensorFactory;
    private readonly Func<TuringRevA?> _openDevice;
    private readonly ConcurrentQueue<(Action<TuringRevA, ISensorSource> Action, TaskCompletionSource Done)> _jobs = new();
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private volatile bool _reload = true;
    private volatile bool _brightnessDirty = true;
    private volatile bool _powerDirty = true;

    public MonitorService(AppSettings settings, ThemeStore themes, Func<ISensorSource> sensorFactory, Func<TuringRevA?>? openDevice = null)
    {
        _settings = settings;
        _themes = themes;
        _sensorFactory = sensorFactory;
        _openDevice = openDevice ?? OpenSerial;
    }

    /// <summary>Última lectura de sensores (para el texto del icono de la bandeja).</summary>
    public SensorSnapshot? LastSnapshot { get; private set; }

    /// <summary>Estado legible: "Conectada", "Esperando la pantalla"...</summary>
    public string Status { get; private set; } = "Iniciando";

    public bool Connected { get; private set; }

    /// <summary>Se dispara (desde el hilo del motor) cuando cambia <see cref="Status"/>.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Puerto de la pantalla conectada (p. ej. "COM6").</summary>
    public string? PortName { get; private set; }

    /// <summary>Si es true, tras cada fotograma se guarda una copia en <see cref="PreviewPng"/> (la pide la ventana de configuración).</summary>
    public volatile bool PreviewRequested;

    /// <summary>Último fotograma en PNG; <see cref="PreviewVersion"/> sube cada vez que cambia.</summary>
    public byte[]? PreviewPng { get; private set; }

    public int PreviewVersion { get; private set; }

    /// <summary>El último fotograma dibujado (para depurar).</summary>
    public Renderer? CurrentRenderer { get; private set; }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Turing.Monitor" };
        _thread.Start();
    }

    /// <summary>Vuelve a cargar el tema y la orientación (tras cambiar Tema o Invertida).</summary>
    public void Reload() => _reload = true;

    public void ApplyBrightness() => _brightnessDirty = true;

    public void ApplyPower() => _powerDirty = true;

    /// <summary>Ejecuta una acción con la pantalla en el hilo del motor (p. ej. el diagnóstico) y luego redibuja todo.</summary>
    public Task RunOnDevice(Action<TuringRevA> action) => Enqueue((d, _) => action(d));

    /// <summary>Lista de todos los sensores, leída en el hilo del motor (solo con la pantalla conectada).</summary>
    public async Task<string> DumpSensorsAsync()
    {
        string text = "";
        await Enqueue((_, sensors) => text = sensors is HardwareSensorSource hw ? hw.DumpAll() : "(sensores simulados)");
        return text;
    }

    private Task Enqueue(Action<TuringRevA, ISensorSource> action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _jobs.Enqueue((action, tcs));
        return tcs.Task;
    }

    private void SetStatus(string status, bool connected)
    {
        Connected = connected;
        if (status == Status)
            return;
        Status = status;
        Log.Write(status);
        StatusChanged?.Invoke(status);
    }

    private void Run()
    {
        var token = _cts.Token;
        ISensorSource? sensors = null;
        try
        {
            SetStatus("Leyendo sensores", false);
            sensors = _sensorFactory();
        }
        catch (Exception ex)
        {
            Log.Write($"No pude iniciar los sensores: {ex}");
            sensors = new EmptySensorSource();
        }

        while (!token.IsCancellationRequested)
        {
            TuringRevA? device = null;
            try
            {
                device = _openDevice();
                if (device is null)
                {
                    SetStatus("Esperando la pantalla", false);
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                    continue;
                }

                SetStatus("Conectada", true);
                _reload = _brightnessDirty = _powerDirty = true;
                Loop(device, sensors, token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
            {
                SetStatus(ex is UnauthorizedAccessException
                    ? "Puerto ocupado (¿está abierto UsbMonitor.exe?)"
                    : $"Se perdió la conexión: {ex.Message}", false);
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                Log.Write($"Error inesperado: {ex}");
                SetStatus($"Error: {ex.Message}", false);
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(3));
            }
            finally
            {
                device?.Dispose();
            }
        }
        sensors.Dispose();
    }

    private void Loop(TuringRevA device, ISensorSource sensors, CancellationToken token)
    {
        Renderer? renderer = null;
        bool full = true;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();

                while (_jobs.TryDequeue(out var job))
                {
                    try
                    {
                        job.Action(device, sensors);
                        job.Done.SetResult();
                    }
                    catch (Exception ex)
                    {
                        job.Done.SetException(ex);
                    }
                    _reload = _brightnessDirty = true;
                }

                if (_powerDirty)
                {
                    _powerDirty = false;
                    if (_settings.Encendida) device.ScreenOn(); else device.ScreenOff();
                }
                if (_brightnessDirty)
                {
                    _brightnessDirty = false;
                    device.SetBrightness(_settings.Brillo);
                }
                if (_reload)
                {
                    _reload = false;
                    var theme = LoadTheme();
                    device.SetOrientation(theme.IsLandscape
                        ? (_settings.Invertida ? Orientation.ReverseLandscape : Orientation.Landscape)
                        : (_settings.Invertida ? Orientation.ReversePortrait : Orientation.Portrait));
                    renderer?.Dispose();
                    renderer = ThemeLoader.CreateRenderer(theme);
                    CurrentRenderer = renderer;
                    full = true;
                }

                var snap = sensors.Read();
                LastSnapshot = snap;
                if (_settings.Encendida && renderer is not null)
                {
                    var regions = renderer.Render(snap, DateTime.Now, full);
                    foreach (var r in regions)
                        device.DrawRgb565(r.X, r.Y, r.Width, r.Height, r.Rgb565);
                    if (PreviewRequested && (regions.Count > 0 || PreviewPng is null))
                    {
                        PreviewPng = renderer.EncodePng();
                        PreviewVersion++;
                    }
                    full = false;
                }

                var wait = TimeSpan.FromSeconds(1) - sw.Elapsed;
                if (wait > TimeSpan.Zero)
                    token.WaitHandle.WaitOne(wait);
            }
        }
        finally
        {
            CurrentRenderer = null;
            renderer?.Dispose();
        }
    }

    /// <summary>Carga el tema activo; la primera vez el de TECNOTICS; si no existe o está mal, el primero disponible.</summary>
    private Theme LoadTheme()
    {
        var names = _themes.List();
        foreach (var name in new[] { _settings.Tema, ThemeStore.DefaultTheme }.Concat(names))
        {
            if (name is null || !names.Contains(name))
                continue;
            try
            {
                var theme = _themes.Load(name);
                if (_settings.Tema != name)
                {
                    _settings.Tema = name;
                    _settings.Save();
                }
                return theme;
            }
            catch (Exception ex)
            {
                Log.Write($"El tema \"{name}\" tiene un error: {ex.Message}");
            }
        }
        throw new InvalidOperationException($"No hay temas válidos en {_themes.Folder}.");
    }

    private TuringRevA? OpenSerial()
    {
        var port = string.IsNullOrWhiteSpace(_settings.Puerto) ? SerialDisplay.FindTuringPort() : _settings.Puerto;
        PortName = port;
        return port is null ? null : SerialDisplay.Open(port);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(5));
        _cts.Dispose();
    }

    private sealed class EmptySensorSource : ISensorSource
    {
        public SensorSnapshot Read() => new();
        public void Dispose() { }
    }
}
