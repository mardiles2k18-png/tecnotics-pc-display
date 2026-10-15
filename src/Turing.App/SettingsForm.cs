using Turing.Engine;
using Turing.Sensors;

namespace Turing.App;

/// <summary>
/// Ventana de configuración: lo mismo que el menú de la bandeja, más la vista previa de la pantalla
/// y los valores de los sensores en vivo. Cerrarla no cierra el programa.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TrayContext _app;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    private readonly PictureBox _preview = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Dock = DockStyle.Fill };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly TrackBar _brightness = new() { Minimum = 5, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Width = 180 };
    private readonly Label _brightnessValue = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    private readonly CheckBox _invert = new() { Text = "Girar 180°", AutoSize = true };
    private readonly CheckBox _power = new() { Text = "Pantalla encendida", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Arrancar con Windows", AutoSize = true };
    private readonly ListView _sensors = new()
    {
        View = View.Details, FullRowSelect = true, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Dock = DockStyle.Fill, MinimumSize = new Size(0, 150),
    };
    private int _previewVersion = -1;
    private bool _loading;

    private static readonly (string Label, string Id, string Format)[] SensorRows =
    [
        ("Uso de CPU", SensorIds.CpuLoad, "{0:0} %"),
        ("Temperatura CPU", SensorIds.CpuTemp, "{0:0} °C"),
        ("Consumo CPU", SensorIds.CpuPower, "{0:0} W"),
        ("Frecuencia CPU", SensorIds.CpuClockGhz, "{0:0.00} GHz"),
        ("Uso de GPU", SensorIds.GpuLoad, "{0:0} %"),
        ("Temperatura GPU", SensorIds.GpuTemp, "{0:0} °C"),
        ("Consumo GPU", SensorIds.GpuPower, "{0:0} W"),
        ("Memoria GPU usada", SensorIds.GpuMemUsedGb, "{0:0.0} GB"),
        ("Uso de RAM", SensorIds.RamLoad, "{0:0} %"),
        ("RAM usada", SensorIds.RamUsed, "{0:0.0} GB"),
        ("RAM total", SensorIds.RamTotal, "{0:0.0} GB"),
    ];

    public SettingsForm(TrayContext app)
    {
        _app = app;
        Text = "Turing Monitor - Configuración";
        Icon = Branding.AppIcon(new Size(32, 32));
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(820, 600);
        MinimumSize = new Size(700, 520);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        // Cabecera: logo y estado.
        var header = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        var logo = new PictureBox { Image = Branding.Logo(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(90, 72) };
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(12, 10, 0, 0) };
        titles.Controls.Add(new Label { Text = "Turing Monitor", AutoSize = true, Font = new Font("Segoe UI", 16f, FontStyle.Bold) });
        titles.Controls.Add(_status);
        header.Controls.Add(logo);
        header.Controls.Add(titles);
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);

        // Izquierda: vista previa de la pantalla.
        var previewBox = new GroupBox { Text = "Lo que muestra la pantalla", Dock = DockStyle.Fill, Padding = new Padding(8) };
        previewBox.Controls.Add(_preview);
        root.Controls.Add(previewBox, 0, 1);

        // Derecha: ajustes y sensores.
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(8, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(right, 1, 1);

        var screenBox = new GroupBox { Text = "Pantalla", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.Controls.Add(new Label { Text = "Tema", AutoSize = true, Margin = new Padding(3, 7, 12, 3) }, 0, 0);
        grid.Controls.Add(_theme, 1, 0);
        grid.Controls.Add(new Label { Text = "Brillo", AutoSize = true, Margin = new Padding(3, 10, 12, 3) }, 0, 1);
        var brightnessRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        brightnessRow.Controls.Add(_brightness);
        brightnessRow.Controls.Add(_brightnessValue);
        grid.Controls.Add(brightnessRow, 1, 1);
        var checks = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = Padding.Empty };
        checks.Controls.Add(_invert);
        checks.Controls.Add(_power);
        checks.Controls.Add(_autostart);
        grid.Controls.Add(checks, 1, 2);
        screenBox.Controls.Add(grid);
        right.Controls.Add(screenBox, 0, 0);

        var sensorsBox = new GroupBox { Text = "Sensores en vivo", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _sensors.Columns.Add("Sensor", 170);
        _sensors.Columns.Add("Valor", 140);
        sensorsBox.Controls.Add(_sensors);
        right.Controls.Add(sensorsBox, 0, 1);

        // Abajo: herramientas.
        var tools = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        tools.Controls.Add(Button("Carpeta de temas", (_, _) => _app.OpenThemesFolder()));
        tools.Controls.Add(Button("Recargar temas", (_, _) => { _app.ReloadThemes(); LoadValues(); }));
        tools.Controls.Add(Button("Lista de sensores", async (_, _) => await _app.SaveSensorList()));
        tools.Controls.Add(Button("Prueba de pantalla", async (_, _) => await _app.RunDiagnostics()));
        tools.Controls.Add(Button("Abrir registro", (_, _) => _app.OpenLog()));
        tools.Controls.Add(Button("Cerrar ventana", (_, _) => Close()));
        tools.Controls.Add(Button("Salir del programa", (_, _) => _app.Exit()));
        root.Controls.Add(tools, 0, 2);
        root.SetColumnSpan(tools, 2);

        foreach (var (label, _, _) in SensorRows)
            _sensors.Items.Add(new ListViewItem([label, "--"]));

        _theme.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _theme.SelectedItem is string name)
                _app.SetTheme(name);
        };
        _brightness.ValueChanged += (_, _) =>
        {
            _brightnessValue.Text = $"{_brightness.Value} %";
            if (!_loading)
                _app.SetBrightness(_brightness.Value);
        };
        _invert.CheckedChanged += (_, _) => { if (!_loading) _app.SetInverted(_invert.Checked); };
        _power.CheckedChanged += (_, _) => { if (!_loading) _app.SetPower(_power.Checked); };
        _autostart.CheckedChanged += (_, _) =>
        {
            if (_loading)
                return;
            _app.SetAutostart(_autostart.Checked);
            LoadValues(); // por si no se pudo cambiar
        };

        _timer.Tick += (_, _) => RefreshLive();
        LoadValues();
    }

    private static Button Button(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        b.Click += onClick;
        return b;
    }

    /// <summary>Pone los controles con los ajustes actuales (también si se cambiaron desde el menú).</summary>
    public void LoadValues()
    {
        _loading = true;
        try
        {
            var s = _app.Settings;
            _theme.Items.Clear();
            foreach (var name in _app.Themes.List())
                _theme.Items.Add(name);
            _theme.SelectedItem = s.Tema;
            _brightness.Value = Math.Clamp(s.Brillo, _brightness.Minimum, _brightness.Maximum);
            _brightnessValue.Text = $"{_brightness.Value} %";
            _invert.Checked = s.Invertida;
            _power.Checked = s.Encendida;
            _autostart.Checked = _app.IsAutostart();
        }
        finally
        {
            _loading = false;
        }
        RefreshLive();
    }

    private void RefreshLive()
    {
        var service = _app.Service;
        _status.Text = service.Connected && service.PortName is not null
            ? $"Pantalla: {service.Status} ({service.PortName})"
            : $"Pantalla: {service.Status}";
        _status.ForeColor = service.Connected ? Color.FromArgb(0x1E, 0x8E, 0x3E) : Color.FromArgb(0xC5, 0x22, 0x1F);

        if (service.LastSnapshot is { } snap)
        {
            for (int i = 0; i < SensorRows.Length; i++)
            {
                var (_, id, format) = SensorRows[i];
                _sensors.Items[i].SubItems[1].Text = snap.Get(id) is float v ? string.Format(format, v) : "--";
            }
            var cpu = snap.Names.GetValueOrDefault(SensorIds.CpuName);
            var gpu = snap.Names.GetValueOrDefault(SensorIds.GpuName);
            _sensors.Items[0].ToolTipText = cpu;
            _sensors.Items[4].ToolTipText = gpu;
        }

        if (service.PreviewVersion != _previewVersion && service.PreviewPng is { } png)
        {
            _previewVersion = service.PreviewVersion;
            using var ms = new MemoryStream(png);
            var old = _preview.Image;
            _preview.Image = new Bitmap(Image.FromStream(ms));
            old?.Dispose();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _app.Service.PreviewRequested = Visible;
        if (Visible)
            _timer.Start();
        else
            _timer.Stop();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _app.Service.PreviewRequested = false;
        _timer.Stop();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _preview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
