namespace Turing.Device;

/// <summary>Orientación de la pantalla. Los valores coinciden con el protocolo (se envían + 100).</summary>
public enum Orientation
{
    Portrait = 0,
    ReversePortrait = 1,
    Landscape = 2,
    ReverseLandscape = 3,
}

/// <summary>Una pantalla a la que se le envían rectángulos de píxeles RGB565.</summary>
public interface IDisplayDevice : IDisposable
{
    /// <summary>Ancho en la orientación actual.</summary>
    int Width { get; }

    /// <summary>Alto en la orientación actual.</summary>
    int Height { get; }

    Orientation Orientation { get; }

    void ScreenOn();
    void ScreenOff();

    /// <summary>Brillo de 0 (apagada) a 100 (máximo).</summary>
    void SetBrightness(int percent);

    void SetOrientation(Orientation orientation);

    /// <summary>Dibuja un rectángulo. <paramref name="pixels"/> son w*h píxeles RGB565 little-endian (2 bytes por píxel).</summary>
    void DrawRgb565(int x, int y, int width, int height, ReadOnlySpan<byte> pixels);
}
