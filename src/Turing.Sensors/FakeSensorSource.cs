namespace Turing.Sensors;

/// <summary>Valores inventados que cambian con el tiempo, para probar sin hardware real.</summary>
public sealed class FakeSensorSource : ISensorSource
{
    private int _tick;

    public SensorSnapshot Read()
    {
        _tick++;
        double t = _tick;
        var s = new SensorSnapshot();
        s.Values[SensorIds.CpuLoad] = (float)(35 + 30 * Math.Sin(t / 3));
        s.Values[SensorIds.CpuTemp] = (float)(55 + 10 * Math.Sin(t / 5));
        s.Values[SensorIds.GpuLoad] = (float)(60 + 35 * Math.Sin(t / 4));
        s.Values[SensorIds.GpuTemp] = (float)(50 + 12 * Math.Sin(t / 6));
        s.Values[SensorIds.RamLoad] = 48;
        s.Values[SensorIds.RamUsed] = 7.6f;
        s.Values[SensorIds.RamAvailable] = 8.2f;
        s.Names[SensorIds.CpuName] = "AMD Ryzen 7 3700X";
        s.Values[SensorIds.CpuClock] = (float)(3900 + 300 * Math.Sin(t / 2));
        s.Values[SensorIds.CpuPower] = (float)(60 + 25 * Math.Sin(t / 3));
        s.Values[SensorIds.GpuPower] = (float)(90 + 60 * Math.Sin(t / 4));
        s.Values[SensorIds.GpuMemUsed] = 1740;
        s.Values[SensorIds.GpuMemTotal] = 16311;
        s.Names[SensorIds.GpuName] = "NVIDIA GeForce RTX 5060 Ti";
        s.AddDerived();
        return s;
    }

    public void Dispose() { }
}
