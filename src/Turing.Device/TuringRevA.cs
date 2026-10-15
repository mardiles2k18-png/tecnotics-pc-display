namespace Turing.Device;

/// <summary>
/// Protocolo "revisión A" de la Turing Smart Screen 3.5" (número de serie USB USB35INCHIPSV2).
/// Cada orden es un paquete de 6 bytes con x0, y0, x1, y1 (10 bits cada uno) y el código de orden.
/// </summary>
public sealed class TuringRevA : IDisplayDevice
{
    public const int NativeWidth = 320;
    public const int NativeHeight = 480;

    public enum Command : byte
    {
        Hello = 69,
        Reset = 101,
        Clear = 102,
        ScreenOff = 108,
        ScreenOn = 109,
        SetBrightness = 110,
        SetOrientation = 121,
        DisplayBitmap = 197,
    }

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly object _lock = new();

    /// <param name="stream">El flujo del puerto serie (o uno simulado en pruebas).</param>
    public TuringRevA(Stream stream, bool ownsStream = true)
    {
        _stream = stream;
        _ownsStream = ownsStream;
    }

    public Orientation Orientation { get; private set; } = Orientation.Portrait;

    /// <summary>
    /// Máximo de bytes de píxeles por orden de dibujo (0 = sin límite). Las zonas más grandes se envían en tiras.
    /// </summary>
    public int MaxBytesPerCommand { get; set; }

    /// <summary>Pausa en milisegundos después de cada orden de dibujo, para que la pantalla termine de pintar.</summary>
    public int PauseAfterBitmapMs { get; set; }

    public int Width => IsLandscape ? NativeHeight : NativeWidth;
    public int Height => IsLandscape ? NativeWidth : NativeHeight;

    private bool IsLandscape => Orientation is Orientation.Landscape or Orientation.ReverseLandscape;

    /// <summary>Empaqueta una orden de 6 bytes.</summary>
    public static byte[] Pack(Command cmd, int x0, int y0, int x1, int y1)
    {
        var b = new byte[6];
        PackInto(b, cmd, x0, y0, x1, y1);
        return b;
    }

    private static void PackInto(Span<byte> b, Command cmd, int x0, int y0, int x1, int y1)
    {
        b[0] = (byte)(x0 >> 2);
        b[1] = (byte)(((x0 & 3) << 6) + (y0 >> 4));
        b[2] = (byte)(((y0 & 15) << 4) + (x1 >> 6));
        b[3] = (byte)(((x1 & 63) << 2) + (y1 >> 8));
        b[4] = (byte)(y1 & 255);
        b[5] = (byte)cmd;
    }

    private void Send(ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            _stream.Write(data);
            _stream.Flush();
        }
    }

    public void ScreenOn() => Send(Pack(Command.ScreenOn, 0, 0, 0, 0));

    public void ScreenOff() => Send(Pack(Command.ScreenOff, 0, 0, 0, 0));

    public void SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        // La pantalla usa 0 = máximo y 255 = apagada.
        int level = 255 - percent * 255 / 100;
        Send(Pack(Command.SetBrightness, level, 0, 0, 0));
    }

    public void SetOrientation(Orientation orientation)
    {
        Orientation = orientation;
        var b = new byte[16];
        PackInto(b, Command.SetOrientation, 0, 0, 0, 0);
        b[6] = (byte)((int)orientation + 100);
        b[7] = (byte)(Width >> 8);
        b[8] = (byte)(Width & 255);
        b[9] = (byte)(Height >> 8);
        b[10] = (byte)(Height & 255);
        Send(b);
    }

    /// <summary>Reinicia la pantalla. El puerto COM desaparece unos segundos y hay que volver a abrirlo.</summary>
    public void Reset() => Send(Pack(Command.Reset, 0, 0, 0, 0));

    public void DrawRgb565(int x, int y, int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (width <= 0 || height <= 0)
            return;
        if (x < 0 || y < 0 || x + width > Width || y + height > Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"El rectángulo {x},{y} {width}x{height} se sale de la pantalla {Width}x{Height}.");
        if (pixels.Length != width * height * 2)
            throw new ArgumentException($"Se esperaban {width * height * 2} bytes y llegaron {pixels.Length}.", nameof(pixels));

        int rowBytes = width * 2;
        int rowsPerCommand = MaxBytesPerCommand > 0 ? Math.Max(1, MaxBytesPerCommand / rowBytes) : height;
        for (int row = 0; row < height; row += rowsPerCommand)
        {
            int rows = Math.Min(rowsPerCommand, height - row);
            SendBitmap(x, y + row, width, rows, pixels.Slice(row * rowBytes, rows * rowBytes));
            if (PauseAfterBitmapMs > 0)
                Thread.Sleep(PauseAfterBitmapMs);
        }
    }

    private void SendBitmap(int x, int y, int width, int height, ReadOnlySpan<byte> pixels)
    {
        lock (_lock)
        {
            _stream.Write(Pack(Command.DisplayBitmap, x, y, x + width - 1, y + height - 1));
            // Enviar en trozos de 8 veces el ancho de la pantalla, como turing-smart-screen-python.
            int chunk = Width * 8;
            for (int offset = 0; offset < pixels.Length; offset += chunk)
                _stream.Write(pixels.Slice(offset, Math.Min(chunk, pixels.Length - offset)));
            _stream.Flush();
        }
    }

    /// <summary>
    /// Envía HELLO y devuelve lo que conteste la pantalla (vacío si no contesta).
    /// La 3.5" original no suele contestar; los modelos de 5" y 7" responden con 6 bytes iguales.
    /// </summary>
    public byte[] Hello(TimeSpan timeout)
    {
        Send(new byte[] { 69, 69, 69, 69, 69, 69 });
        var response = new List<byte>();
        var deadline = DateTime.UtcNow + timeout;
        var buffer = new byte[6];
        while (response.Count < 6 && DateTime.UtcNow < deadline)
        {
            try
            {
                int n = _stream.Read(buffer, 0, 6 - response.Count);
                if (n <= 0) break;
                response.AddRange(buffer.AsSpan(0, n).ToArray());
            }
            catch (TimeoutException)
            {
                break;
            }
        }
        return response.ToArray();
    }

    public void Dispose()
    {
        if (_ownsStream)
            _stream.Dispose();
    }
}
