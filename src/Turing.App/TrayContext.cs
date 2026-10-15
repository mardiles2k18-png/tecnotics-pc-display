using System.Diagnostics;
using Turing.Engine;
using Turing.Sensors;
using Turing.Themes;

namespace Turing.App;

/// <summary>Icono en la bandeja de Windows con el menú para manejar el monitor.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ThemeStore _themes = new(AppSettings.ThemesFolder);
    private readonly MonitorService _service;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _tooltipTimer = new() { Interval = 2000 };
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _themesMenu = new("Tema");
    private readonly ToolStripMenuItem _brightnessMenu = new("Brillo");
    private readonly ToolStripMenuItem _invert = new("Girar 180°");
    private readonly ToolStripMenuItem _power = new("Pantalla encendida");
    private readonly ToolStripMenuItem _autostart = new("Arrancar con Windows");
    private SettingsForm? _form;

    internal AppSettings Settings => _settings;
    internal ThemeStore Themes => _themes;
    internal MonitorService Service => _service;

    public TrayContext()
    {
        _themes.InstallBuiltins();
        _service = new MonitorService(_settings, _themes, () => new HardwareSensorSource());

        _icon = new NotifyIcon
        {
            Icon = Branding.AppIcon(SystemInformation.SmallIconSize),
            Text = "Turing Monitor",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        BuildMenu();
        _menu.Opening += (_, _) => RefreshMenu();
        _menu.Closed += (_, _) => _form?.LoadValues(); // por si se cambió algo desde el menú
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
        _tooltipTimer.Start();
        OfferSensorDriver();
        _service.Start();

        // La primera vez, ofrecer el arranque automático.
        if (!_settings.ArranquePreguntado)
        {
            _settings.ArranquePreguntado = true;
            _settings.Save();
            if (!SafeIsAutostart() && MessageBox.Show("¿Quieres que Turing Monitor arranque solo al iniciar Windows?",
                    "Turing Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                SetAutostart(true);
        }
    }

    private void BuildMenu()
    {
        _menu.Items.Add(_status);
        var open = new ToolStripMenuItem("Configuración...", null, (_, _) => ShowSettings()) { Font = new Font(_menu.Font, FontStyle.Bold) };
        _menu.Items.Add(open);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_themesMenu);
        foreach (var pct in new[] { 10, 25, 50, 75, 100 })
        {
            var item = new ToolStripMenuItem($"{pct}%") { Tag = pct };
            item.Click += (_, _) => SetBrightness(pct);
            _brightnessMenu.DropDownItems.Add(item);
        }
        _menu.Items.Add(_brightnessMenu);
        _invert.Click += (_, _) => SetInverted(!_settings.Invertida);
        _menu.Items.Add(_invert);
        _power.Click += (_, _) => SetPower(!_settings.Encendida);
        _menu.Items.Add(_power);
        _menu.Items.Add(new ToolStripSeparator());
        _autostart.Click += (_, _) => SetAutostart(!_autostart.Checked);
        _menu.Items.Add(_autostart);

        var tools = new ToolStripMenuItem("Herramientas");
        tools.DropDownItems.Add("Abrir carpeta de temas", null, (_, _) => OpenThemesFolder());
        tools.DropDownItems.Add("Recargar temas", null, (_, _) => ReloadThemes());
        tools.DropDownItems.Add("Guardar lista de sensores", null, async (_, _) => await SaveSensorList());
        tools.DropDownItems.Add("Prueba de pantalla (diagnóstico)", null, async (_, _) => await RunDiagnostics());
        tools.DropDownItems.Add("Abrir registro", null, (_, _) => OpenLog());
        tools.DropDownItems.Add("Instalar driver de sensores (PawnIO)", null, (_, _) => InstallSensorDriver(null));
        _menu.Items.Add(tools);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Salir", null, (_, _) => Exit());
    }

    private void RefreshMenu()
    {
        RefreshStatus();

        _themesMenu.DropDownItems.Clear();
        foreach (var name in _themes.List())
        {
            var item = new ToolStripMenuItem(name) { Checked = name == _settings.Tema };
            item.Click += (_, _) => SetTheme(name);
            _themesMenu.DropDownItems.Add(item);
        }
        if (_themesMenu.DropDownItems.Count == 0)
            _themesMenu.DropDownItems.Add(new ToolStripMenuItem("(no hay temas)") { Enabled = false });

        foreach (ToolStripMenuItem item in _brightnessMenu.DropDownItems)
            item.Checked = (int)item.Tag! == _settings.Brillo;
        _invert.Checked = _settings.Invertida;
        _power.Checked = _settings.Encendida;
        _autostart.Checked = SafeIsAutostart();
    }

    /// <summary>
    /// La primera vez, si falta el driver PawnIO, ofrece instalarlo. Va antes de arrancar el motor porque
    /// LibreHardwareMonitor solo detecta el driver al abrir los sensores.
    /// </summary>
    private void OfferSensorDriver()
    {
        if (_settings.DriverPreguntado || SensorDriver.IsInstalled)
            return;
        _settings.DriverPreguntado = true;
        _settings.Save();
        if (MessageBox.Show(
                "Para mostrar la temperatura, el consumo y la frecuencia de la CPU hace falta el driver gratuito PawnIO " +
                "(de pawnio.eu, firmado digitalmente).\n\n¿Lo descargo e instalo ahora? Solo se hace una vez.",
                "Turing Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            SensorDriver.InstallInteractive(null);
    }

    /// <summary>Instala el driver desde el menú o la ventana y reinicia el programa para que los sensores lo usen.</summary>
    internal void InstallSensorDriver(IWin32Window? owner)
    {
        if (SensorDriver.InstalledVersion() is { } v)
        {
            MessageBox.Show(owner, $"El driver de sensores PawnIO {v} ya está instalado.", "Turing Monitor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!SensorDriver.InstallInteractive(owner))
            return;
        MessageBox.Show(owner, "Driver instalado. Turing Monitor se va a reiniciar para leer los sensores nuevos.", "Turing Monitor",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        Program.RestartRequested = true;
        Exit();
    }

    /// <summary>Abre la ventana de configuración (o la trae al frente si ya está abierta).</summary>
    private void ShowSettings()
    {
        if (_form is null || _form.IsDisposed)
        {
            _form = new SettingsForm(this);
            _form.FormClosed += (_, _) => _form = null;
            _form.Show();
        }
        else
        {
            _form.LoadValues();
            if (_form.WindowState == FormWindowState.Minimized)
                _form.WindowState = FormWindowState.Normal;
        }
        _form.Activate();
    }

    internal void SetTheme(string name)
    {
        _settings.Tema = name;
        _settings.Save();
        _service.Reload();
    }

    internal void SetBrightness(int percent)
    {
        _settings.Brillo = percent;
        _settings.Save();
        _service.ApplyBrightness();
    }

    internal void SetInverted(bool inverted)
    {
        _settings.Invertida = inverted;
        _settings.Save();
        _service.Reload();
    }

    internal void SetPower(bool on)
    {
        _settings.Encendida = on;
        _settings.Save();
        _service.ApplyPower();
        _service.Reload();
    }

    internal void OpenThemesFolder() => OpenFolder(AppSettings.ThemesFolder);

    internal void ReloadThemes() => _service.Reload();

    internal void OpenLog() => OpenFile(Log.FilePath);

    internal bool IsAutostart() => SafeIsAutostart();

    private void RefreshStatus() => _status.Text = $"Pantalla: {_service.Status}";

    private void UpdateTooltip()
    {
        var s = _service.LastSnapshot;
        string text = s is null || !_service.Connected
            ? $"Turing Monitor: {_service.Status}"
            : $"CPU {V(s, SensorIds.CpuLoad, "%")} {V(s, SensorIds.CpuTemp, "°")} · GPU {V(s, SensorIds.GpuLoad, "%")} {V(s, SensorIds.GpuTemp, "°")}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    private static string V(SensorSnapshot s, string id, string unit) => s.Get(id) is float v ? $"{v:0}{unit}" : "--";

    internal void SetAutostart(bool enable)
    {
        try
        {
            if (enable)
                AutoStart.Enable(Environment.ProcessPath!);
            else
                AutoStart.Disable();
        }
        catch (Exception ex)
        {
            Log.Write($"Arranque automático: {ex}");
            MessageBox.Show($"No pude cambiar el arranque automático: {ex.Message}", "Turing Monitor",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool SafeIsAutostart()
    {
        try
        {
            return AutoStart.IsEnabled();
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal async Task SaveSensorList()
    {
        var path = Path.Combine(AppSettings.BaseFolder, "sensores.txt");
        try
        {
            if (!_service.Connected)
            {
                MessageBox.Show("Conecta la pantalla primero: los sensores se leen con el monitor en marcha.", "Turing Monitor");
                return;
            }
            File.WriteAllText(path, await _service.DumpSensorsAsync());
            OpenFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No pude leer los sensores: {ex.Message}", "Turing Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal async Task RunDiagnostics()
    {
        if (!_service.Connected)
        {
            MessageBox.Show("La pantalla no está conectada.", "Turing Monitor");
            return;
        }
        try
        {
            foreach (var o in new[] { Turing.Device.Orientation.Portrait, Turing.Device.Orientation.Landscape })
            {
                await _service.RunOnDevice(d => Diagnostics.Run(d, o));
                MessageBox.Show("Saca una foto de la pantalla y pulsa Aceptar para seguir.", "Prueba de pantalla");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"La prueba falló: {ex.Message}", "Turing Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private static void OpenFile(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    internal void Exit()
    {
        _form?.Close();
        _tooltipTimer.Stop();
        _icon.Visible = false;
        _service.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
            _menu.Dispose();
            _tooltipTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
