namespace Turing.Device;

/// <summary>Conversión de colores a RGB565 little-endian, el formato que espera la pantalla.</summary>
public static class Rgb565
{
    public static ushort FromRgb(byte r, byte g, byte b) =>
        (ushort)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));

    /// <summary>Convierte píxeles BGRA de 32 bits (el formato habitual de Windows y SkiaSharp) a RGB565.</summary>
    public static void FromBgra32(ReadOnlySpan<byte> bgra, Span<byte> destination)
    {
        int count = bgra.Length / 4;
        if (destination.Length < count * 2)
            throw new ArgumentException("El destino es demasiado pequeño.", nameof(destination));

        for (int i = 0; i < count; i++)
        {
            ushort c = FromRgb(bgra[i * 4 + 2], bgra[i * 4 + 1], bgra[i * 4]);
            destination[i * 2] = (byte)c;
            destination[i * 2 + 1] = (byte)(c >> 8);
        }
    }

    /// <summary>Crea un búfer de w*h píxeles de un solo color.</summary>
    public static byte[] Fill(int width, int height, byte r, byte g, byte b)
    {
        ushort c = FromRgb(r, g, b);
        var buffer = new byte[width * height * 2];
        for (int i = 0; i < buffer.Length; i += 2)
        {
            buffer[i] = (byte)c;
            buffer[i + 1] = (byte)(c >> 8);
        }
        return buffer;
    }
}
