namespace Turing.Sensors;

/// <summary>Una lectura de sensores: valores por id estable (p. ej. "cpu.load") y nombres de los componentes.</summary>
public sealed class SensorSnapshot
{
    public Dictionary<string, float> Values { get; } = new();
    public Dictionary<string, string> Names { get; } = new();

    public float? Get(string id) => Values.TryGetValue(id, out var v) ? v : null;

    /// <summary>Calcula valores derivados (totales y conversiones de unidades) a partir de los leídos.</summary>
    public void AddDerived()
    {
        if (Get(SensorIds.RamUsed) is float used && Get(SensorIds.RamAvailable) is float avail)
            Values[SensorIds.RamTotal] = used + avail;
        if (Get(SensorIds.GpuMemUsed) is float vu)
            Values[SensorIds.GpuMemUsedGb] = vu / 1024;
        if (Get(SensorIds.GpuMemTotal) is float vt)
            Values[SensorIds.GpuMemTotalGb] = vt / 1024;
        if (Get(SensorIds.CpuClock) is float mhz)
            Values[SensorIds.CpuClockGhz] = mhz / 1000;
    }
}

/// <summary>Fuente de valores de sensores.</summary>
public interface ISensorSource : IDisposable
{
    SensorSnapshot Read();
}

/// <summary>
/// Ids de sensores que entiende la app. Las unidades van en el comentario.
/// </summary>
public static class SensorIds
{
    public const string CpuLoad = "cpu.load";       // %
    public const string CpuTemp = "cpu.temp";       // °C
    public const string CpuPower = "cpu.power";     // W
    public const string CpuClock = "cpu.clock";     // MHz (media de núcleos)
    public const string GpuLoad = "gpu.load";       // %
    public const string GpuTemp = "gpu.temp";       // °C
    public const string GpuPower = "gpu.power";     // W
    public const string GpuMemUsed = "gpu.mem.used"; // MB
    public const string GpuMemTotal = "gpu.mem.total"; // MB
    public const string RamLoad = "ram.load";       // %
    public const string RamUsed = "ram.used";       // GB
    public const string RamAvailable = "ram.available"; // GB

    // Derivados (ver SensorSnapshot.AddDerived).
    public const string RamTotal = "ram.total";          // GB
    public const string GpuMemUsedGb = "gpu.mem.used.gb"; // GB
    public const string GpuMemTotalGb = "gpu.mem.total.gb"; // GB
    public const string CpuClockGhz = "cpu.clock.ghz";   // GHz

    /// <summary>Todos los ids numéricos, para el editor y la documentación.</summary>
    public static readonly string[] All =
    [
        CpuLoad, CpuTemp, CpuPower, CpuClock, CpuClockGhz,
        GpuLoad, GpuTemp, GpuPower, GpuMemUsed, GpuMemTotal, GpuMemUsedGb, GpuMemTotalGb,
        RamLoad, RamUsed, RamAvailable, RamTotal,
    ];

    public const string CpuName = "cpu";
    public const string GpuName = "gpu";
}
