using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace Turing.Sensors;

/// <summary>
/// Lee los sensores con LibreHardwareMonitor. Necesita ejecutarse como administrador, y para la
/// temperatura de CPU AMD/Intel y la placa base necesita el driver PawnIO instalado (pawnio.eu).
/// </summary>
public sealed class HardwareSensorSource : ISensorSource
{
    private readonly Computer _computer;
    private readonly SystemCounters _counters = new();

    public HardwareSensorSource()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true, // ventiladores de la placa (CPU)
            IsStorageEnabled = true,     // temperatura de discos
        };
        _computer.Open();
    }

    /// <summary>True si el driver PawnIO está instalado (sin él faltan temperaturas de CPU y placa).</summary>
    public static bool IsPawnIoInstalled()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        return key is not null;
    }

    private IEnumerable<IHardware> AllHardware()
    {
        foreach (var hw in _computer.Hardware)
        {
            yield return hw;
            foreach (var sub in hw.SubHardware)
                yield return sub;
        }
    }

    public SensorSnapshot Read()
    {
        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware)
                sub.Update();
        }

        var s = new SensorSnapshot();

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu is not null)
        {
            s.Names[SensorIds.CpuName] = cpu.Name;
            var sensors = cpu.Sensors;
            Put(s, SensorIds.CpuLoad, Find(sensors, SensorType.Load, "CPU Total"));
            Put(s, SensorIds.CpuTemp,
                Find(sensors, SensorType.Temperature, "Core (Tctl/Tdie)", "Tctl/Tdie", "CPU Package", "Core (Tctl)", "Core Average")
                ?? sensors.Where(x => x.SensorType == SensorType.Temperature && x.Value is > 0).Max(x => x.Value));
            Put(s, SensorIds.CpuPower, Find(sensors, SensorType.Power, "Package", "CPU Package"));
            var clocks = sensors.Where(x => x.SensorType == SensorType.Clock && x.Name.StartsWith("Core #") && x.Value is > 0)
                .Select(x => x.Value!.Value).ToList();
            if (clocks.Count > 0)
                s.Values[SensorIds.CpuClock] = clocks.Average();
        }

        var gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
                  ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
        if (gpu is not null)
        {
            s.Names[SensorIds.GpuName] = gpu.Name;
            var sensors = gpu.Sensors;
            Put(s, SensorIds.GpuLoad, Find(sensors, SensorType.Load, "GPU Core", "D3D 3D"));
            Put(s, SensorIds.GpuTemp, Find(sensors, SensorType.Temperature, "GPU Core", "GPU Hot Spot"));
            Put(s, SensorIds.GpuPower, Find(sensors, SensorType.Power, "GPU Package", "GPU Power", "GPU Core"));
            Put(s, SensorIds.GpuMemUsed, Find(sensors, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"));
            Put(s, SensorIds.GpuMemTotal, Find(sensors, SensorType.SmallData, "GPU Memory Total"));
            Put(s, SensorIds.GpuFan, Find(sensors, SensorType.Fan, "GPU Fan", "GPU Fan 1", "Fan #1")
                                     ?? sensors.FirstOrDefault(x => x.SensorType == SensorType.Fan && x.Value is > 0)?.Value);
        }

        // Ventilador de CPU: en el chip de la placa (SuperIO); el que se llame "CPU" o, si no, el primero que gire.
        var boardFans = AllHardware().Where(h => h.HardwareType == HardwareType.SuperIO)
            .SelectMany(h => h.Sensors).Where(x => x.SensorType == SensorType.Fan && x.Value is > 0).ToList();
        Put(s, SensorIds.CpuFan, (boardFans.FirstOrDefault(x => x.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)) ?? boardFans.FirstOrDefault())?.Value);

        var disk = AllHardware().Where(h => h.HardwareType == HardwareType.Storage)
            .Select(h => h.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature && x.Value is > 0)).FirstOrDefault(x => x is not null);
        Put(s, SensorIds.DiskTemp, disk?.Value);

        // LibreHardwareMonitor tiene dos "Memory": la física y la virtual. Queremos la física.
        var mem = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory &&
                                                        !h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase));
        if (mem is not null)
        {
            Put(s, SensorIds.RamLoad, Find(mem.Sensors, SensorType.Load, "Memory"));
            Put(s, SensorIds.RamUsed, Find(mem.Sensors, SensorType.Data, "Memory Used"));
            Put(s, SensorIds.RamAvailable, Find(mem.Sensors, SensorType.Data, "Memory Available"));
        }

        _counters.AddTo(s);
        s.AddDerived();
        return s;
    }

    private static float? Find(ISensor[] sensors, SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            var match = sensors.FirstOrDefault(x => x.SensorType == type && x.Name == name && x.Value is not null);
            if (match is not null)
                return match.Value;
        }
        return null;
    }

    private static void Put(SensorSnapshot s, string id, float? value)
    {
        if (value is not float v || float.IsNaN(v))
            return;
        // Sin el driver PawnIO algunas temperaturas y potencias llegan como 0: mejor mostrar "--".
        if (v <= 0 && (id.EndsWith(".temp") || id.EndsWith(".power")))
            return;
        s.Values[id] = v;
    }

    /// <summary>Todos los sensores como texto, para diagnosticar qué nombres hay en este PC.</summary>
    public string DumpAll()
    {
        Read();
        var sb = new System.Text.StringBuilder();
        foreach (var hw in AllHardware())
        {
            sb.AppendLine($"[{hw.HardwareType}] {hw.Name}");
            foreach (var x in hw.Sensors.OrderBy(x => x.SensorType).ThenBy(x => x.Name))
                sb.AppendLine($"    {x.SensorType,-12} {x.Name,-32} {x.Value?.ToString("F1") ?? "-"}");
        }
        return sb.ToString();
    }

    public void Dispose() => _computer.Close();
}
