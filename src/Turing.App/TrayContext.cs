using System.Diagnostics;
using System.Drawing.Drawing2D;
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

    public TrayContext()
    {
        _themes.InstallBuiltins();
        _service = new MonitorService(_settings, _themes, () => new HardwareSensorSource());

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Turing Monitor",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.DoubleClick += (_, _) => OpenFolder(AppSettings.ThemesFolder);

        BuildMenu();
        _menu.Opening += (_, _) => RefreshMenu();
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
        _tooltipTimer.Start();
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
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_themesMenu);
        foreach (var pct in new[] { 10, 25, 50, 75, 100 })
        {
            var item = new ToolStripMenuItem($"{pct}%") { Tag = pct };
            item.Click += (_, _) =>
            {
                _settings.Brillo = pct;
                _settings.Save();
                _service.ApplyBrightness();
            };
            _brightnessMenu.DropDownItems.Add(item);
        }
        _menu.Items.Add(_brightnessMenu);
        _invert.Click += (_, _) =>
        {
            _settings.Invertida = !_settings.Invertida;
            _settings.Save();
            _service.Reload();
        };
        _menu.Items.Add(_invert);
        _power.Click += (_, _) =>
        {
            _settings.Encendida = !_settings.Encendida;
            _settings.Save();
            _service.ApplyPower();
            _service.Reload();
        };
        _menu.Items.Add(_power);
        _menu.Items.Add(new ToolStripSeparator());
        _autostart.Click += (_, _) => SetAutostart(!_autostart.Checked);
        _menu.Items.Add(_autostart);

        var tools = new ToolStripMenuItem("Herramientas");
        tools.DropDownItems.Add("Abrir carpeta de temas", null, (_, _) => OpenFolder(AppSettings.ThemesFolder));
        tools.DropDownItems.Add("Recargar temas", null, (_, _) => _service.Reload());
        tools.DropDownItems.Add("Guardar lista de sensores", null, async (_, _) => await SaveSensorList());
        tools.DropDownItems.Add("Prueba de pantalla (diagnóstico)", null, async (_, _) => await RunDiagnostics());
        tools.DropDownItems.Add("Abrir registro", null, (_, _) => OpenFile(Log.FilePath));
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
            item.Click += (_, _) =>
            {
                _settings.Tema = name;
                _settings.Save();
                _service.Reload();
            };
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

    private void SetAutostart(bool enable)
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

    private async Task SaveSensorList()
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

    private async Task RunDiagnostics()
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

    private void Exit()
    {
        _tooltipTimer.Stop();
        _icon.Visible = false;
        _service.Dispose();
        ExitThread();
    }

    /// <summary>Icono dibujado en código: un rectángulo azul con una barra, como una pantallita.</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var body = new SolidBrush(Color.FromArgb(0x18, 0x23, 0x3A));
            using var edge = new Pen(Color.FromArgb(0x3D, 0xA9, 0xFC), 2);
            using var bar = new SolidBrush(Color.FromArgb(0x76, 0xD6, 0x6F));
            using var bar2 = new SolidBrush(Color.FromArgb(0x3D, 0xA9, 0xFC));
            g.FillRectangle(body, 3, 5, 26, 22);
            g.DrawRectangle(edge, 3, 5, 26, 22);
            g.FillRectangle(bar2, 8, 11, 16, 4);
            g.FillRectangle(bar, 8, 18, 10, 4);
        }
        return Icon.FromHandle(bmp.GetHicon());
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
