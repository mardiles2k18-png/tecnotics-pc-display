using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace Turing.Device;

/// <summary>Un puerto COM encontrado en el equipo.</summary>
public sealed record PortInfo(string Port, string? Description, string? DeviceId)
{
    /// <summary>True si parece una Turing Smart Screen (VID 1A86 o número de serie USB35INCHIPSV2).</summary>
    public bool LooksLikeTuring =>
        DeviceId is not null &&
        (DeviceId.Contains("VID_1A86", StringComparison.OrdinalIgnoreCase) ||
         DeviceId.Contains("USB35INCHIPSV2", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Busca y abre la pantalla por el puerto serie.</summary>
public static class SerialDisplay
{
    public const int BaudRate = 115200;

    /// <summary>Lista los puertos COM. En Windows añade la descripción e id de dispositivo USB.</summary>
    public static IReadOnlyList<PortInfo> ListPorts()
    {
        var names = SerialPort.GetPortNames().Distinct().OrderBy(p => p).ToList();
        var details = new Dictionary<string, PortInfo>(StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"] as string;
                    var id = obj["PNPDeviceID"] as string;
                    var m = name is null ? null : Regex.Match(name, @"\((COM\d+)\)");
                    if (m is { Success: true })
                        details[m.Groups[1].Value] = new PortInfo(m.Groups[1].Value, name, id);
                }
            }
            catch (Exception)
            {
                // WMI no disponible: seguimos solo con los nombres.
            }
        }

        return names
            .Select(n => details.TryGetValue(n, out var d) ? d : new PortInfo(n, null, null))
            .ToList();
    }

    /// <summary>Devuelve el puerto de la pantalla, o null si no se encuentra.</summary>
    public static string? FindTuringPort() => ListPorts().FirstOrDefault(p => p.LooksLikeTuring)?.Port;

    /// <summary>
    /// Abre el puerto igual que el software original (UsbMonitor.exe, descompilado): 115200 8N1, DTR y RTS activos,
    /// sin control de flujo y con los búferes por defecto.
    /// </summary>
    public static TuringRevA Open(string port)
    {
        var serial = new SerialPort(port, BaudRate, Parity.None, 8, StopBits.One)
        {
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = 1000,
            WriteTimeout = 10000,
        };
        serial.Open();
        return new TuringRevA(new SerialStream(serial));
    }

    /// <summary>Envuelve SerialPort para que al cerrarse el flujo también se cierre el puerto.</summary>
    private sealed class SerialStream(SerialPort port) : Stream
    {
        private readonly Stream _inner = port.BaseStream;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => port.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                port.Dispose();
            base.Dispose(disposing);
        }
    }
}
